namespace Multiplay.Server.Services;

public sealed class CombatService : ICombatService
{
    public int CalculateDamage(int attack, int defence) =>
        Math.Max(1, attack - defence);

    public (float x, float y) Knockback(
        float fromX, float fromY,
        float targetX, float targetY,
        float distance,
        float minX, float maxX, float minY, float maxY)
    {
        float dx  = targetX - fromX;
        float dy  = targetY - fromY;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.0001f) { dx = 1f; dy = 0f; len = 1f; }
        return (
            Math.Clamp(targetX + dx / len * distance, minX, maxX),
            Math.Clamp(targetY + dy / len * distance, minY, maxY)
        );
    }
}
