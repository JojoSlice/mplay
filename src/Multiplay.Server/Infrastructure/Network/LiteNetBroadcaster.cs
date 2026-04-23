using LiteNetLib;
using LiteNetLib.Utils;

namespace Multiplay.Server.Infrastructure.Network;

public sealed class LiteNetBroadcaster(NetManager net) : IGameBroadcaster
{
    private const byte DefaultChannel = 0;
    private readonly List<NetPeer> _buffer = [];

    public void SendTo(int peerId, NetDataWriter writer, DeliveryMethod delivery)
    {
        _buffer.Clear();
        net.GetConnectedPeers(_buffer);
        foreach (var peer in _buffer)
            if (peer.Id == peerId)
                peer.Send(writer, DefaultChannel, delivery);
    }

    public void Broadcast(NetDataWriter writer, DeliveryMethod delivery, int except = -1)
    {
        _buffer.Clear();
        net.GetConnectedPeers(_buffer);
        foreach (var peer in _buffer)
            if (peer.Id != except)
                peer.Send(writer, DefaultChannel, delivery);
    }
}
