namespace Multiplay.Shared;

public enum PacketType : byte
{
    // Server → Client
    WorldSnapshot = 0,
    PlayerJoined  = 1,
    PlayerLeft    = 2,
    PlayerMoved   = 3,
    EnemySnapshot = 4,
    EnemyMoved    = 5,
    // Client → Server
    Move = 10,
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
