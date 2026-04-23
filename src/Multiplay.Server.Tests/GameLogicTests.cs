using LiteNetLib.Utils;
using Multiplay.Server.Infrastructure.GameState;
using Multiplay.Server.Tests.Helpers;
using Multiplay.Shared;

namespace Multiplay.Server.Tests;

public class GameLogicTests
{
    private readonly InMemoryGameState _state;
    private readonly FakeBroadcaster   _broadcaster;
    private readonly GameLogic         _logic;

    public GameLogicTests()
    {
        _state       = new InMemoryGameState();
        _broadcaster = new FakeBroadcaster();
        _logic       = new GameLogic(_state, _broadcaster);
    }

    // ── OnPlayerConnected ──────────────────────────────────────────────────────

    [Fact]
    public void OnPlayerConnected_AddsPlayerToState()
    {
        _logic.OnPlayerConnected(1, "Alice", CharacterType.Zink);

        Assert.Single(_state.All);
        var player = _state.All.First();
        Assert.Equal(1, player.Id);
        Assert.Equal("Alice", player.Name);
        Assert.Equal(CharacterType.Zink, player.CharacterType);
    }

    [Fact]
    public void OnPlayerConnected_SendsWorldSnapshotToNewPlayer()
    {
        _logic.OnPlayerConnected(1, "Alice", CharacterType.Zink);

        var snapshots = _broadcaster.OfType(PacketType.WorldSnapshot).ToList();
        Assert.Single(snapshots);
        Assert.Equal(1, snapshots[0].TargetPeerId);
    }

    [Fact]
    public void OnPlayerConnected_SecondPlayer_BroadcastsJoinToFirst()
    {
        _logic.OnPlayerConnected(1, "Alice", CharacterType.Zink);
        _broadcaster.Clear();

        _logic.OnPlayerConnected(2, "Bob", CharacterType.ShieldKnight);

        // Should broadcast PlayerJoined to everyone except peer 2
        var joins = _broadcaster.OfType(PacketType.PlayerJoined).ToList();
        Assert.Single(joins);
        Assert.Equal(2, joins[0].BroadcastExcept);
    }

    [Fact]
    public void OnPlayerConnected_FirstPlayer_PlayerJoinedBroadcastExcludesNewPlayer()
    {
        // Even with one player the broadcast is emitted (reaches nobody in practice).
        // What matters is that the new player is excluded so they don't receive
        // their own join event.
        _logic.OnPlayerConnected(1, "Alice", CharacterType.Zink);

        var joins = _broadcaster.OfType(PacketType.PlayerJoined).ToList();
        Assert.Single(joins);
        Assert.Equal(1, joins[0].BroadcastExcept); // excludes the joining peer
    }

    // ── OnPlayerDisconnected ──────────────────────────────────────────────────

    [Fact]
    public void OnPlayerDisconnected_RemovesFromState()
    {
        _logic.OnPlayerConnected(1, "Alice", CharacterType.Zink);
        _logic.OnPlayerDisconnected(1);
        Assert.Empty(_state.All);
    }

    [Fact]
    public void OnPlayerDisconnected_ReturnsFinalState()
    {
        _logic.OnPlayerConnected(1, "Alice", CharacterType.Zink);
        var final = _logic.OnPlayerDisconnected(1);
        Assert.NotNull(final);
        Assert.Equal("Alice", final.Value.Name);
    }

    [Fact]
    public void OnPlayerDisconnected_BroadcastsPlayerLeft()
    {
        _logic.OnPlayerConnected(1, "Alice", CharacterType.Zink);
        _broadcaster.Clear();
        _logic.OnPlayerDisconnected(1);

        Assert.Single(_broadcaster.OfType(PacketType.PlayerLeft));
    }

    [Fact]
    public void OnPlayerDisconnected_UnknownPlayer_ReturnsNull()
    {
        var result = _logic.OnPlayerDisconnected(99);
        Assert.Null(result);
    }

    [Fact]
    public void OnPlayerDisconnected_UnknownPlayer_NoBroadcast()
    {
        _logic.OnPlayerDisconnected(99);
        Assert.Empty(_broadcaster.Calls);
    }

    // ── OnMove ────────────────────────────────────────────────────────────────

    [Fact]
    public void OnMove_UpdatesPositionInState()
    {
        _logic.OnPlayerConnected(1, "Alice", CharacterType.Zink);
        _logic.OnMove(1, 150f, 250f);

        Assert.True(_state.TryGet(1, out var p));
        Assert.Equal(150f, p.X);
        Assert.Equal(250f, p.Y);
    }

    [Fact]
    public void OnMove_BroadcastsExceptMover()
    {
        _logic.OnPlayerConnected(1, "Alice", CharacterType.Zink);
        _broadcaster.Clear();
        _logic.OnMove(1, 150f, 250f);

        var moves = _broadcaster.OfType(PacketType.PlayerMoved).ToList();
        Assert.Single(moves);
        Assert.Equal(1, moves[0].BroadcastExcept);
    }

    [Fact]
    public void OnMove_UnknownPlayer_NoBroadcast()
    {
        _logic.OnMove(99, 0f, 0f);
        Assert.Empty(_broadcaster.Calls);
    }
}
