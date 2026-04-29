using Multiplay.Server.Services;

namespace Multiplay.Server.Tests.Services;

public class CombatServiceTests
{
    private readonly CombatService _sut = new();

    // ── CalculateDamage ────────────────────────────────────────────────────────

    [Fact]
    public void CalculateDamage_SubtractsDefence()
    {
        Assert.Equal(5, _sut.CalculateDamage(10, 5));
    }

    [Fact]
    public void CalculateDamage_ReturnsOneWhenDefenceEqualsAttack()
    {
        Assert.Equal(1, _sut.CalculateDamage(5, 5));
    }

    [Fact]
    public void CalculateDamage_ReturnsOneWhenDefenceExceedsAttack()
    {
        Assert.Equal(1, _sut.CalculateDamage(3, 10));
    }

    // ── Knockback ─────────────────────────────────────────────────────────────

    [Fact]
    public void Knockback_MovesTargetAwayFromSource()
    {
        var (x, y) = _sut.Knockback(0, 0, 10, 0, 20, -100, 100, -100, 100);
        Assert.Equal(30, x, 3);
        Assert.Equal(0,  y, 3);
    }

    [Fact]
    public void Knockback_ClampsToMapBounds()
    {
        var (x, y) = _sut.Knockback(0, 0, 90, 0, 50, -100, 100, -100, 100);
        Assert.Equal(100, x);
    }

    [Fact]
    public void Knockback_ZeroVectorDefaultsToRight()
    {
        var (x, y) = _sut.Knockback(0, 0, 0, 0, 10, -100, 100, -100, 100);
        Assert.Equal(10, x, 3);
        Assert.Equal(0,  y, 3);
    }
}
