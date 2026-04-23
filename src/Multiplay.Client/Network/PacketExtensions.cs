using LiteNetLib.Utils;
using Multiplay.Shared;

namespace Multiplay.Client.Network;

internal static class PacketExtensions
{
    internal static PlayerInfo ReadPlayerInfo(this NetDataReader r) =>
        new(r.GetInt(), r.GetString(), r.GetFloat(), r.GetFloat(), r.GetString());
}
