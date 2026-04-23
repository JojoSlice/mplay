using Multiplay.Shared;

namespace Multiplay.Server.Infrastructure.GameState;

/// <summary>
/// Single-threaded in-memory game state.
/// All mutations must occur on the LiteNetLib poll-loop thread.
/// </summary>
public sealed class InMemoryGameState : IGameState
{
    private readonly Dictionary<int, PlayerInfo> _players = [];

    public IReadOnlyCollection<PlayerInfo> All => _players.Values;

    public bool TryGet(int id, out PlayerInfo player) =>
        _players.TryGetValue(id, out player);

    public void Add(PlayerInfo player) => _players[player.Id] = player;

    public bool Remove(int id, out PlayerInfo final) =>
        _players.Remove(id, out final);

    public bool TryMove(int id, float x, float y)
    {
        if (!_players.TryGetValue(id, out var cur)) return false;
        _players[id] = cur with { X = x, Y = y };
        return true;
    }
}
