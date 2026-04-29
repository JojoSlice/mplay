namespace Multiplay.Shared;

/// <summary>Mutable stats for a player character.</summary>
public record struct PlayerStats(
    int Health,    int MaxHealth,
    int Attack,    int Defence,
    int Stamina,   int MaxStamina,
    int MagicPower, int MaxMagicPower,
    int Level,     int Xp);

/// <summary>Mutable stats for an enemy.</summary>
public record struct EnemyStats(
    int Health, int MaxHealth,
    int Attack, int Defence);

/// <summary>XP thresholds: XpForNextLevel(n) = (n+1)*100.</summary>
public static class XpSystem
{
    public static int XpForNextLevel(int level) => (level + 1) * 100;

    /// <summary>Add <paramref name="xp"/> to <paramref name="stats"/>, levelling up as needed.</summary>
    public static PlayerStats AwardXp(PlayerStats stats, int xp)
    {
        int newXp    = stats.Xp + xp;
        int newLevel = stats.Level;
        while (newXp >= XpForNextLevel(newLevel))
        {
            newXp -= XpForNextLevel(newLevel);
            newLevel++;
        }
        return stats with { Level = newLevel, Xp = newXp };
    }
}

/// <summary>Base stats per character / enemy type.</summary>
public static class DefaultStats
{
    public static PlayerStats ForCharacter(string? characterType) => characterType switch
    {
        CharacterType.ShieldKnight => new PlayerStats(150, 150,  7, 15,  80,  80, 15, 15, 0, 0),
        CharacterType.SwordKnight  => new PlayerStats( 90,  90, 15,  4,  90,  90, 10, 10, 0, 0),
        _                          => new PlayerStats(100, 100, 10,  5, 100, 100, 50, 50, 0, 0), // Zink
    };

    public static EnemyStats ForEnemy(string? enemyType) => new EnemyStats(40, 40, 22, 2); // Slime — 22 atk kills Zink in 6 hits
}
