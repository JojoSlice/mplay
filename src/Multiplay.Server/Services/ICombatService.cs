namespace Multiplay.Server.Services;

public interface ICombatService
{
    int CalculateDamage(int attack, int defence);

    (float x, float y) Knockback(
        float fromX, float fromY,
        float targetX, float targetY,
        float distance,
        float minX, float maxX, float minY, float maxY);
}
