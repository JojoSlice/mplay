using Multiplay.Server.Domain;
using Multiplay.Shared;

namespace Multiplay.Server.Services;

public interface IEnemyAI
{
    EnemyAIState NextState(EnemyAIState current, float nearestDist,
        float detectionRadius, float chaseRadius);

    /// <summary>Advances hop animation and applies wander movement. Returns isAttacking.</summary>
    bool ApplyWander(Enemy enemy, float dt,
        float minX, float maxX,
        float minSpeed, float maxSpeed,
        float hopCycle, float wanderDirInterval);

    /// <summary>Advances hop animation and moves toward <paramref name="target"/>. Returns isAttacking (always true).</summary>
    bool ApplyChase(Enemy enemy, PlayerInfo? target, float dt,
        float speed, float minX, float maxX, float minY, float maxY);
}
