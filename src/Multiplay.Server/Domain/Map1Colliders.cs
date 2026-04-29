using Multiplay.Shared;

namespace Multiplay.Server.Domain;

/// <summary>
/// World-space collision geometry for map1.tmx, extracted from the Tiled object layer.
/// Mirrors the colliders that <see cref="Multiplay.Client.World.TileMapRenderer"/> exposes
/// to the client so that enemy movement is blocked by the same obstacles.
/// </summary>
internal static class Map1Colliders
{
    // AABB colliders: (left, top, right, bottom)
    private static readonly (float L, float T, float R, float B)[] Rects =
    [
        // leftGarden  — x=1.33  y=1904.67 w=318.67 h=334
        (1.33f,   1904.67f, 320.00f, 2238.67f),
        // rightGarden — x=479.33 y=1906    w=319.33 h=334
        (479.33f, 1906.00f, 798.67f, 2240.00f),
        // hub portal  — x=304.67 y=2239.33 w=189.33 h=84
        (304.67f, 2239.33f, 494.00f, 2323.33f),
    ];

    // Polygon colliders: world-space vertices (origin + points from TMX)
    private static readonly (float x, float y)[][] Polygons =
    [
        // trees1 — origin (48.67, 1337.67)
        [
            (48.67f,  1337.67f), (57.00f,  1328.67f), (167.33f, 1328.33f),
            (175.33f, 1336.33f), (176.33f, 1375.00f), (201.33f, 1376.33f),
            (207.33f, 1384.33f), (207.67f, 1439.00f), (176.67f, 1440.00f),
            (176.67f, 1472.33f), (111.33f, 1472.33f), (111.00f, 1440.67f),
            (47.67f,  1441.00f), (47.33f,  1408.67f), (15.67f,  1407.67f),
            (15.67f,  1367.33f), (24.33f,  1360.33f), (47.67f,  1360.00f),
        ],
        // trees2 — origin (592, 1689.09)
        [
            (592.00f, 1689.09f), (599.82f, 1680.18f), (623.09f, 1680.18f),
            (623.45f, 1671.82f), (635.45f, 1663.27f), (647.45f, 1664.18f),
            (656.36f, 1672.00f), (656.18f, 1678.55f), (669.09f, 1680.00f),
            (671.64f, 1688.36f), (672.55f, 1695.27f), (695.27f, 1695.82f),
            (703.45f, 1703.45f), (704.55f, 1743.45f), (688.18f, 1744.18f),
            (688.00f, 1760.36f), (655.64f, 1760.18f), (655.27f, 1744.18f),
            (622.91f, 1744.27f), (623.09f, 1727.91f), (591.45f, 1727.91f),
        ],
    ];

    /// <summary>
    /// Resolves a circle at (<paramref name="x"/>, <paramref name="y"/>) against all
    /// map1 AABB and polygon colliders and returns the adjusted position.
    /// </summary>
    public static (float x, float y) Resolve(float x, float y, float radius)
    {
        foreach (var (l, t, r, b) in Rects)
            (x, y) = Collision.ResolveRect(x, y, radius, l, t, r, b);

        foreach (var poly in Polygons)
            (x, y) = Collision.ResolvePoly(x, y, radius, poly);

        return (x, y);
    }
}
