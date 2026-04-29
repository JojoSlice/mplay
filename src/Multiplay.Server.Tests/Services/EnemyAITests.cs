using Multiplay.Server.Domain;
using Multiplay.Server.Services;
using Multiplay.Shared;

namespace Multiplay.Server.Tests.Services;

public class EnemyAITests
{
    private readonly EnemyAI _sut = new();

    private static Enemy MakeEnemy(float x = 100, float y = 100, float dirX = 1f) => new()
    {
        Index = 0,
        DirX  = dirX,
        Info  = new EnemyInfo(1, EnemyType.Slime, x, y),
        Stats = DefaultStats.ForEnemy(EnemyType.Slime),
        WanderTimer = 2f,
    };

    // ── NextState ─────────────────────────────────────────────────────────────

    [Fact]
    public void NextState_Wander_EntersChaseWhenPlayerClose()
    {
        var result = _sut.NextState(EnemyAIState.Wander, nearestDist: 50f,
            detectionRadius: 200f, chaseRadius: 300f);
        Assert.Equal(EnemyAIState.Chase, result);
    }

    [Fact]
    public void NextState_Wander_StaysWanderWhenPlayerFar()
    {
        var result = _sut.NextState(EnemyAIState.Wander, nearestDist: 250f,
            detectionRadius: 200f, chaseRadius: 300f);
        Assert.Equal(EnemyAIState.Wander, result);
    }

    [Fact]
    public void NextState_Chase_EntersWanderWhenPlayerTooFar()
    {
        var result = _sut.NextState(EnemyAIState.Chase, nearestDist: 350f,
            detectionRadius: 200f, chaseRadius: 300f);
        Assert.Equal(EnemyAIState.Wander, result);
    }

    [Fact]
    public void NextState_Chase_StaysChaseWhenPlayerClose()
    {
        var result = _sut.NextState(EnemyAIState.Chase, nearestDist: 100f,
            detectionRadius: 200f, chaseRadius: 300f);
        Assert.Equal(EnemyAIState.Chase, result);
    }

    // ── ApplyWander ───────────────────────────────────────────────────────────

    [Fact]
    public void ApplyWander_MovesEnemyHorizontally()
    {
        var enemy = MakeEnemy(x: 200, dirX: 1f);
        _sut.ApplyWander(enemy, dt: 0.1f,
            minX: 0, maxX: 800, minSpeed: 10, maxSpeed: 70,
            hopCycle: 1.26f, wanderDirInterval: 2f);
        Assert.NotEqual(200f, enemy.Info.X);
    }

    [Fact]
    public void ApplyWander_FlipsDirectionAtWallBoundary()
    {
        var enemy = MakeEnemy(x: 799, dirX: 1f);
        _sut.ApplyWander(enemy, dt: 0.5f,
            minX: 0, maxX: 800, minSpeed: 70, maxSpeed: 70,
            hopCycle: 1.26f, wanderDirInterval: 2f);
        Assert.Equal(-1f, enemy.DirX);
    }

    [Fact]
    public void ApplyWander_ReturnsIsAttackingBasedOnHopPhase()
    {
        var enemy = MakeEnemy();
        enemy.HopTime = 0f; // start of cycle → isAttacking = true (hop phase)
        bool attacking = _sut.ApplyWander(enemy, dt: 0.01f,
            minX: 0, maxX: 800, minSpeed: 10, maxSpeed: 70,
            hopCycle: 1.26f, wanderDirInterval: 2f);
        Assert.True(attacking);
    }

    // ── ApplyChase ────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyChase_MovesEnemyTowardTarget()
    {
        var enemy  = MakeEnemy(x: 0, y: 0);
        var target = new PlayerInfo(1, "Bob", 100f, 0f, CharacterType.Zink);
        _sut.ApplyChase(enemy, target, dt: 0.1f,
            speed: 70f, minX: -500, maxX: 500, minY: -500, maxY: 500);
        Assert.True(enemy.Info.X > 0);
    }

    [Fact]
    public void ApplyChase_AlwaysReturnsIsAttacking()
    {
        var enemy  = MakeEnemy();
        bool result = _sut.ApplyChase(enemy, null, dt: 0.1f,
            speed: 70f, minX: 0, maxX: 800, minY: 0, maxY: 800);
        Assert.True(result);
    }
}
