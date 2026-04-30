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
    private const int StatsPerLevel = 1;

    public static int XpForNextLevel(int level) => (level + 1) * 100;

    /// <summary>
    /// Add <paramref name="xp"/> to <paramref name="stats"/>, levelling up as needed.
    /// Each level gained adds +1 to MaxHealth, MaxStamina, and MaxMagicPower,
    /// and restores all three to their new maximums.
    /// </summary>
    public static PlayerStats AwardXp(PlayerStats stats, int xp)
    {
        int newXp      = stats.Xp + xp;
        int newLevel   = stats.Level;
        int maxHealth  = stats.MaxHealth;
        int maxStamina = stats.MaxStamina;
        int maxMagic   = stats.MaxMagicPower;

        while (newXp >= XpForNextLevel(newLevel))
        {
            int threshold = XpForNextLevel(newLevel);
            newXp     -= threshold;
            newLevel++;
            maxHealth  += StatsPerLevel;
            maxStamina += StatsPerLevel;
            maxMagic   += StatsPerLevel;
        }

        bool leveledUp = newLevel > stats.Level;
        return stats with
        {
            Level         = newLevel,
            Xp            = newXp,
            MaxHealth     = maxHealth,
            MaxStamina    = maxStamina,
            MaxMagicPower = maxMagic,
            Health        = leveledUp ? maxHealth  : stats.Health,
            Stamina       = leveledUp ? maxStamina : stats.Stamina,
            MagicPower    = leveledUp ? maxMagic   : stats.MagicPower,
        };
    }
}

/// <summary>Base stats per class and enemy type.</summary>
public static class DefaultStats
{
    /// <summary>
    /// Returns base stats for a weapon class.
    /// Null or unknown weapon type returns Beginner stats.
    /// </summary>
    public static PlayerStats ForClass(string? weaponType) => weaponType switch
    {
        "Sword" => new PlayerStats(120, 120, 14,  6,  80,  80, 10, 10, 0, 0),
        "Bow"   => new PlayerStats( 90,  90, 10,  3, 120, 120, 20, 20, 0, 0),
        "Wand"  => new PlayerStats( 70,  70,  6,  2,  80,  80, 80, 80, 0, 0),
        _       => new PlayerStats( 80,  80,  8,  3,  80,  80, 10, 10, 0, 0), // Beginner
    };

    /// <summary>
    /// Reconstructs full stats for a class at a saved level/xp.
    /// Max stats are base + 1 per level; current HP/Stamina/MP start at their maximums.
    /// </summary>
    public static PlayerStats ForClassAtLevel(string? weaponType, int level, int xp)
    {
        var s     = ForClass(weaponType);
        int maxHp = s.MaxHealth     + level;
        int maxSt = s.MaxStamina    + level;
        int maxMp = s.MaxMagicPower + level;
        return s with
        {
            Level         = level,
            Xp            = xp,
            MaxHealth     = maxHp,  Health     = maxHp,
            MaxStamina    = maxSt,  Stamina    = maxSt,
            MaxMagicPower = maxMp,  MagicPower = maxMp,
        };
    }

    public static EnemyStats ForEnemy(string? enemyType) => new EnemyStats(40, 40, 22, 2); // Slime — 22 atk kills Zink in 6 hits
}
