using Multiplay.Shared;

namespace Multiplay.Client.Network;

public interface INetworkManager : IDisposable
{
    int  LocalId     { get; }
    bool IsConnected { get; }

    event Action<int, PlayerInfo[]>?    WorldSnapshotReceived;
    event Action<PlayerInfo>?           PlayerJoined;
    event Action<int, float, float>?    PlayerMoved;
    event Action<int>?                  PlayerLeft;

    event Action<EnemyInfo[]>?          EnemySnapshotReceived;
    event Action<EnemyInfo>?            EnemyMoved;

    void Connect(string host, int port, string token);
    void SendMove(float x, float y);
    void PollEvents();
}
