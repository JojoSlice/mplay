using Multiplay.Server.Domain;
using Multiplay.Shared;

namespace Multiplay.Server.Services;

public sealed class EnemyAI : IEnemyAI
{
    public EnemyAIState NextState(EnemyAIState current, float nearestDist,
        float detectionRadius, float chaseRadius)
    {
        if (current == EnemyAIState.Wander && nearestDist < detectionRadius) return EnemyAIState.Chase;
        if (current == EnemyAIState.Chase  && nearestDist > chaseRadius)     return EnemyAIState.Wander;
        return current;
    }

    public bool ApplyWander(Enemy enemy, float dt,
        float minX, float maxX,
        float minSpeed, float maxSpeed,
        float hopCycle, float wanderDirInterval)
    {
        enemy.HopTime = (enemy.HopTime + dt) % hopCycle;
        bool isAttacking = enemy.HopTime < hopCycle / 2f;
        float speed = isAttacking ? minSpeed : maxSpeed;

        enemy.WanderTimer -= dt;
        if (enemy.WanderTimer <= 0f)
        {
            enemy.DirX        = -enemy.DirX;
            enemy.WanderTimer = wanderDirInterval;
        }

        float newX = enemy.Info.X + speed * enemy.DirX * dt;
        if (newX <= minX || newX >= maxX)
        {
            enemy.DirX = -enemy.DirX;
            newX = Math.Clamp(newX, minX, maxX);
        }
        enemy.Info = enemy.Info with { X = newX };
        return isAttacking;
    }

    public bool ApplyChase(Enemy enemy, PlayerInfo? target, float dt,
        float speed, float minX, float maxX, float minY, float maxY)
    {
        enemy.HopTime = (enemy.HopTime + dt) % (7 * 0.18f); // HopCycle constant

        if (target.HasValue)
        {
            float dx  = target.Value.X - enemy.Info.X;
            float dy  = target.Value.Y - enemy.Info.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 0.001f)
            {
                dx /= len;
                dy /= len;
                if (MathF.Abs(dx) > 0.001f)
                    enemy.DirX = MathF.Sign(dx);
                enemy.Info = enemy.Info with
                {
                    X = Math.Clamp(enemy.Info.X + dx * speed * dt, minX, maxX),
                    Y = Math.Clamp(enemy.Info.Y + dy * speed * dt, minY, maxY),
                };
            }
        }
        return true; // always attacking when chasing
    }
}
