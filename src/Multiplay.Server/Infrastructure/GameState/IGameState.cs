using Multiplay.Shared;

namespace Multiplay.Server.Infrastructure.GameState;

public interface IGameState
{
    IReadOnlyCollection<PlayerInfo> All { get; }
    bool TryGet(int id, out PlayerInfo player);
    void Add(PlayerInfo player);
    bool Remove(int id, out PlayerInfo final);
    bool TryMove(int id, float x, float y);
}
