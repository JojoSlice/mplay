using LiteNetLib;
using LiteNetLib.Utils;

namespace Multiplay.Server.Infrastructure.Network;

public interface IGameBroadcaster
{
    void SendTo(int peerId, NetDataWriter writer, DeliveryMethod delivery);
    void Broadcast(NetDataWriter writer, DeliveryMethod delivery, int except = -1);
}
