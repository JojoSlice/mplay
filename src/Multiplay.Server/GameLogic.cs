using LiteNetLib;
using LiteNetLib.Utils;
using Multiplay.Server.Infrastructure.GameState;
using Multiplay.Server.Infrastructure.Network;
using Multiplay.Shared;

namespace Multiplay.Server;

/// <summary>
/// Pure game logic: manages player state and emits network packets.
/// No sockets, no DB, no ASP.NET — fully testable in isolation.
/// </summary>
public sealed class GameLogic(IGameState state, IGameBroadcaster broadcaster)
{
    public void OnPlayerConnected(int peerId, string displayName, string characterType)
    {
        var player = new PlayerInfo(peerId, displayName, 400f, 300f, characterType);
        state.Add(player);

        // Send the full world snapshot to the joining player
        var snap = new NetDataWriter();
        snap.Put((byte)PacketType.WorldSnapshot);
        snap.Put(peerId);
        var all = state.All;
        snap.Put(all.Count);
        foreach (var p in all)
            snap.WritePlayerInfo(p);
        broadcaster.SendTo(peerId, snap, DeliveryMethod.ReliableOrdered);

        // Announce the new player to everyone else
        var join = new NetDataWriter();
        join.Put((byte)PacketType.PlayerJoined);
        join.WritePlayerInfo(player);
        broadcaster.Broadcast(join, DeliveryMethod.ReliableOrdered, except: peerId);
    }

    /// <returns>The player's final state, or null if the player was unknown.</returns>
    public PlayerInfo? OnPlayerDisconnected(int peerId)
    {
        if (!state.Remove(peerId, out var final)) return null;

        var left = new NetDataWriter();
        left.Put((byte)PacketType.PlayerLeft);
        left.Put(peerId);
        broadcaster.Broadcast(left, DeliveryMethod.ReliableOrdered);

        return final;
    }

    public void OnMove(int peerId, float x, float y)
    {
        if (!state.TryMove(peerId, x, y)) return;

        var w = new NetDataWriter();
        w.Put((byte)PacketType.PlayerMoved);
        w.Put(peerId);
        w.Put(x);
        w.Put(y);
        broadcaster.Broadcast(w, DeliveryMethod.Unreliable, except: peerId);
    }
}
