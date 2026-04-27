using Multiplay.Shared;

namespace Multiplay.Client.Network;

public interface INetworkManager : IDisposable
{
    int  LocalId     { get; }
    bool IsConnected { get; }

    event Action<int, PlayerInfo[]>?      WorldSnapshotReceived;
    event Action<PlayerInfo>?             PlayerJoined;
    event Action<int, float, float>?      PlayerMoved;
    event Action<int>?                    PlayerLeft;

    event Action<EnemyInfo[], int[]>?     EnemySnapshotReceived;
    event Action<EnemyInfo>?              EnemyMoved;
    event Action<EnemyInfo, int>?         EnemyDamaged;      // (enemy, newHealth)

    event Action<int, float, float, int>? PlayerDamaged;     // (playerId, newX, newY, newHealth)
    event Action<PlayerStats>?            PlayerStatsReceived;

    void Connect(string host, int port, string token);
    void SendMove(float x, float y);
    void SendAttack(float dirX, float dirY);
    void PollEvents();
}
