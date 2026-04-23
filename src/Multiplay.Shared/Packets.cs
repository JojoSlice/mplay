using LiteNetLib.Utils;

namespace Multiplay.Shared;

public enum PacketType : byte
{
    // Server → Client
    WorldSnapshot = 0,
    PlayerJoined  = 1,
    PlayerLeft    = 2,
    PlayerMoved   = 3,
    // Client → Server
    Move    = 10,
    SetName = 11,
}

/// <summary>Valid character type identifiers.</summary>
public static class CharacterType
{
    public const string Zink         = "Zink";
    public const string ShieldKnight = "ShieldKnight";
    public const string SwordKnight  = "SwordKnight";
}

/// <summary>Snapshot of a single player's state.</summary>
public record struct PlayerInfo(int Id, string Name, float X, float Y, string CharacterType = CharacterType.Zink);

/// <summary>Extension methods for reading/writing PlayerInfo and packet headers.</summary>
public static class Packets
{
    public static void WritePlayerInfo(this NetDataWriter w, PlayerInfo p)
    {
        w.Put(p.Id);
        w.Put(p.Name ?? string.Empty);
        w.Put(p.X);
        w.Put(p.Y);
        w.Put(p.CharacterType ?? CharacterType.Zink);
    }

    public static PlayerInfo ReadPlayerInfo(this NetDataReader r) =>
        new(r.GetInt(), r.GetString(), r.GetFloat(), r.GetFloat(), r.GetString());
}
