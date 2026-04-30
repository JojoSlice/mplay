namespace Multiplay.Shared;

/// <summary>Collision radii (world units) for each entity type.</summary>
public static class ColliderRadius
{
    public static float ForCharacter(string? type) => type switch
    {
        CharacterType.ShieldKnight => 10f,  // 32×32 sprite
        CharacterType.SwordKnight  => 10f,  // 32×32 sprite
        _                          => 7f,   // Zink 48×48 sprite
    };

    public static float ForEnemy(string? type) => 6f;  // Slime 16×16 sprite
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

    /// <summary>
    /// Pushes a circle at (cx, cy) out of an axis-aligned rectangle.
    /// Returns the adjusted position.
    /// </summary>
    public static (float cx, float cy) ResolveRect(
        float cx, float cy, float radius,
        float left, float top, float right, float bottom)
    {
        float ox = Math.Clamp(cx, left, right);
        float oy = Math.Clamp(cy, top,  bottom);
        float dx = cx - ox;
        float dy = cy - oy;
        float distSq = dx * dx + dy * dy;
        if (distSq >= radius * radius) return (cx, cy); // no overlap

        if (distSq < 0.0001f) // centre is inside the rect
        {
            float oL = cx - left   + radius;
            float oR = right  - cx + radius;
            float oT = cy - top    + radius;
            float oB = bottom - cy + radius;
            float min = Math.Min(Math.Min(oL, oR), Math.Min(oT, oB));
            if      (min == oL) return (left   - radius, cy);
            else if (min == oR) return (right  + radius, cy);
            else if (min == oT) return (cx, top    - radius);
            else                return (cx, bottom + radius);
        }

        float dist    = MathF.Sqrt(distSq);
        float overlap = radius - dist;
        return (cx + dx / dist * overlap, cy + dy / dist * overlap);
    }

    /// <summary>
    /// Pushes a circle at (cx, cy) out of a polygon (edge-by-edge push-out).
    /// Polygon vertices are supplied as flat (x, y) pairs.
    /// </summary>
    public static (float cx, float cy) ResolvePoly(
        float cx, float cy, float radius,
        ReadOnlySpan<(float x, float y)> polygon)
    {
        for (int i = 0; i < polygon.Length; i++)
        {
            var (ax, ay) = polygon[i];
            var (bx, by) = polygon[(i + 1) % polygon.Length];
            float abx = bx - ax, aby = by - ay;
            float dSq  = abx * abx + aby * aby;
            float t    = dSq < 0.0001f ? 0f
                : Math.Clamp(((cx - ax) * abx + (cy - ay) * aby) / dSq, 0f, 1f);
            float ox   = ax + t * abx;
            float oy   = ay + t * aby;
            float diffX = cx - ox;
            float diffY = cy - oy;
            float dist  = MathF.Sqrt(diffX * diffX + diffY * diffY);
            if (dist < radius && dist > 0.0001f)
            {
                cx += diffX / dist * (radius - dist);
                cy += diffY / dist * (radius - dist);
            }
        }
        return (cx, cy);
    }
}
