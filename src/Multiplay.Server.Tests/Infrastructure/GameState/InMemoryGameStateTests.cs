using Multiplay.Server.Infrastructure.GameState;
using Multiplay.Shared;

namespace Multiplay.Server.Tests.Infrastructure.GameState;

public class InMemoryGameStateTests
{
    private static PlayerInfo Player(int id, string name = "Test") =>
        new(id, name, 0f, 0f, CharacterType.Zink);

    [Fact]
    public void Add_ThenAll_ContainsPlayer()
    {
        var state = new InMemoryGameState();
        state.Add(Player(1, "Alice"));
        Assert.Single(state.All);
        Assert.Equal("Alice", state.All.First().Name);
    }

    [Fact]
    public void TryGet_AfterAdd_ReturnsPlayer()
    {
        var state = new InMemoryGameState();
        state.Add(Player(1));
        Assert.True(state.TryGet(1, out var p));
        Assert.Equal(1, p.Id);
    }

    [Fact]
    public void TryGet_UnknownId_ReturnsFalse()
    {
        var state = new InMemoryGameState();
        Assert.False(state.TryGet(99, out _));
    }

    [Fact]
    public void Remove_KnownPlayer_ReturnsTrueWithFinalState()
    {
        var state = new InMemoryGameState();
        state.Add(Player(1, "Alice"));

        Assert.True(state.Remove(1, out var final));
        Assert.Equal("Alice", final.Name);
        Assert.Empty(state.All);
    }

    [Fact]
    public void Remove_UnknownPlayer_ReturnsFalse()
    {
        var state = new InMemoryGameState();
        Assert.False(state.Remove(99, out _));
    }

    [Fact]
    public void TryMove_UpdatesPosition()
    {
        var state = new InMemoryGameState();
        state.Add(Player(1));

        Assert.True(state.TryMove(1, 100f, 200f));

        Assert.True(state.TryGet(1, out var p));
        Assert.Equal(100f, p.X);
        Assert.Equal(200f, p.Y);
    }

    [Fact]
    public void TryMove_UnknownPlayer_ReturnsFalse()
    {
        var state = new InMemoryGameState();
        Assert.False(state.TryMove(99, 0f, 0f));
    }

    [Fact]
    public void Add_OverwritesExistingPlayer()
    {
        var state = new InMemoryGameState();
        state.Add(Player(1, "Alice"));
        state.Add(new PlayerInfo(1, "AliceRenamed", 50f, 50f, CharacterType.Zink));

        Assert.Single(state.All);
        Assert.Equal("AliceRenamed", state.All.First().Name);
    }
}
