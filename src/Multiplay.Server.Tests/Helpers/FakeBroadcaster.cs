using LiteNetLib;
using LiteNetLib.Utils;
using Multiplay.Server.Infrastructure.Network;
using Multiplay.Shared;

namespace Multiplay.Server.Tests.Helpers;

/// <summary>
/// Test double for <see cref="IGameBroadcaster"/>.
/// Captures every call so tests can assert on what was sent.
/// </summary>
public sealed class FakeBroadcaster : IGameBroadcaster
{
    public record Capture(
        PacketType PacketType,
        byte[] Data,
        int? TargetPeerId,
        int? BroadcastExcept);

    public List<Capture> Calls { get; } = [];

    public void SendTo(int peerId, NetDataWriter writer, DeliveryMethod delivery) =>
        Calls.Add(new(ReadPacketType(writer), CopyData(writer), TargetPeerId: peerId, BroadcastExcept: null));

    public void Broadcast(NetDataWriter writer, DeliveryMethod delivery, int except = -1) =>
        Calls.Add(new(ReadPacketType(writer), CopyData(writer), TargetPeerId: null,
            BroadcastExcept: except == -1 ? null : except));

    public void Clear() => Calls.Clear();

    public IEnumerable<Capture> OfType(PacketType type) =>
        Calls.Where(c => c.PacketType == type);

    private static PacketType ReadPacketType(NetDataWriter writer)
    {
        var r = new NetDataReader(CopyData(writer));
        return (PacketType)r.GetByte();
    }

    private static byte[] CopyData(NetDataWriter writer)
    {
        var data = new byte[writer.Length];
        Array.Copy(writer.Data, data, writer.Length);
        return data;
    }
}
