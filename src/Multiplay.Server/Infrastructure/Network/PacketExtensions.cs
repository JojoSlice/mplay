using LiteNetLib.Utils;
using Multiplay.Shared;

namespace Multiplay.Server.Infrastructure.Network;

internal static class PacketExtensions
{
    internal static void WritePlayerInfo(this NetDataWriter w, PlayerInfo p)
    {
        w.Put(p.Id);
        w.Put(p.Name ?? string.Empty);
        w.Put(p.X);
        w.Put(p.Y);
        w.Put(p.CharacterType ?? CharacterType.Zink);
    }

    internal static void WriteEnemyInfo(this NetDataWriter w, EnemyInfo e)
    {
        w.Put(e.Id);
        w.Put(e.Type);
        w.Put(e.X);
        w.Put(e.Y);
    }

    internal static void WritePlayerStats(this NetDataWriter w, PlayerStats s)
    {
        w.Put(s.Health);
        w.Put(s.MaxHealth);
        w.Put(s.Attack);
        w.Put(s.Defence);
        w.Put(s.Stamina);
        w.Put(s.MaxStamina);
        w.Put(s.MagicPower);
        w.Put(s.MaxMagicPower);
    }
}
