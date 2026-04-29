namespace Multiplay.Shared;

public enum PacketType : byte
{
    // Server → Client
    WorldSnapshot = 0,
    PlayerJoined  = 1,
    PlayerLeft    = 2,
    PlayerMoved   = 3,
    EnemySnapshot  = 4,
    EnemyMoved     = 5,
    PlayerDamaged  = 6,
    EnemyDamaged   = 7,
    PlayerStats    = 8,
    // Client → Server
    Move        = 10,
    Attack      = 11,
    ZoneChanged = 12,  // player entered a different zone/map
}

/// <summary>Zone identifiers shared between client and server.</summary>
public static class Zone
{
    public const string Hub  = "hub";
    public const string Map1 = "map1";
}

/// <summary>Valid character type identifiers.</summary>
public static class CharacterType
{
    public const string Zink         = "Zink";
    public const string ShieldKnight = "ShieldKnight";
    public const string SwordKnight  = "SwordKnight";

    public static bool IsValid(string? type) =>
        type is Zink or ShieldKnight or SwordKnight;
}

/// <summary>Valid enemy type identifiers.</summary>
public static class EnemyType
{
    public const string Slime = "Slime";
}

/// <summary>Snapshot of a single player's state.</summary>
public record struct PlayerInfo(int Id, string Name, float X, float Y, string CharacterType = CharacterType.Zink);

/// <summary>State of a single enemy.</summary>
public record struct EnemyInfo(int Id, string Type, float X, float Y);
