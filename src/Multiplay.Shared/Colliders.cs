namespace Multiplay.Shared;

/// <summary>Collision radii (world units) for each entity type.</summary>
public static class ColliderRadius
{
    public static float ForCharacter(string? type) => 14f;

    public static float ForEnemy(string? type) => 14f;
}

/// <summary>Circle-vs-circle overlap detection and minimum separation.</summary>
public static class Collision
{
    public static bool Overlaps(
        float x1, float y1, float r1,
        float x2, float y2, float r2)
    {
        float dx = x2 - x1, dy = y2 - y1;
        float minDist = r1 + r2;
        return dx * dx + dy * dy < minDist * minDist;
    }

    /// <summary>
    /// Returns the vector to add to (x1, y1) to push it fully outside (x2, y2).
    /// </summary>
    public static (float X, float Y) Resolve(
        float x1, float y1, float r1,
        float x2, float y2, float r2)
    {
        float dx   = x1 - x2;
        float dy   = y1 - y2;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 0.0001f) return (r1 + r2, 0f); // degenerate: perfectly overlapping
        float push = r1 + r2 - dist;
        return (dx / dist * push, dy / dist * push);
    }
}
