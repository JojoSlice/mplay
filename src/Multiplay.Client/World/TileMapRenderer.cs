using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using TiledCS;

namespace Multiplay.Client.World;

/// <summary>
/// Loads a Tiled .tmx map and renders all visible tile layers each frame.
/// Also exposes objects parsed from the object layer (playArea, colliders,
/// interactables, startPoint).
/// </summary>
public sealed class TileMapRenderer
{
    public const int TileSize = 16;

    private readonly string _tmxPath;

    private TiledMap _map = null!;
    private Dictionary<int, TiledTileset>  _tilesets     = [];
    private Dictionary<int, Texture2D>     _tileTextures = [];

    // ── Parsed objects ─────────────────────────────────────────────────────────

    /// <summary>Movement boundary for the player. Defaults to the full map.</summary>
    public Rectangle PlayArea { get; private set; }

    /// <summary>Solid rectangles the player cannot walk through.</summary>
    public IReadOnlyList<Rectangle> Colliders { get; private set; } = [];

    /// <summary>Interactive zones (name + bounds). Behaviour implemented later.</summary>
    public IReadOnlyList<(string Name, Rectangle Bounds)> Interactables { get; private set; } = [];

    /// <summary>Center of the startPoint object, or null if none exists in the map.</summary>
    public Vector2? StartPoint { get; private set; }

    // ── Dimensions ─────────────────────────────────────────────────────────────

    public int MapWidthPixels  => _map.Width  * TileSize;
    public int MapHeightPixels => _map.Height * TileSize;

    public TileMapRenderer(string tmxPath) => _tmxPath = tmxPath;

    // ── Loading ────────────────────────────────────────────────────────────────

    public void LoadContent(ContentManager content)
    {
        _map    = new TiledMap(_tmxPath);
        var dir = Path.GetDirectoryName(_tmxPath) ?? ".";

        // TiledCS concatenates the folder with the source path verbatim, which
        // breaks when Tiled saves an absolute Windows path (e.g. C:/Users/...).
        // Strip to filename only and resolve relative to the TMX directory instead.
        _tilesets = [];
        foreach (var mapTs in _map.Tilesets)
        {
            var tsxPath = Path.Combine(dir, Path.GetFileName(mapTs.source));
            _tilesets[mapTs.firstgid] = new TiledTileset(tsxPath);
        }

        foreach (var (firstgid, tileset) in _tilesets)
        {
            var imgSource  = tileset.Image?.source ?? string.Empty;
            var stem       = Path.GetFileNameWithoutExtension(imgSource);
            var contentKey = $"Tiles/{stem}";
            _tileTextures[firstgid] = content.Load<Texture2D>(contentKey);
        }

        ParseObjects();
    }

    private void ParseObjects()
    {
        // Default: full map bounds
        PlayArea = new Rectangle(0, 0, MapWidthPixels, MapHeightPixels);

        var colliders     = new List<Rectangle>();
        var interactables = new List<(string, Rectangle)>();

        foreach (var layer in _map.Layers)
        {
            if (layer.type != TiledLayerType.ObjectLayer) continue;
            if (layer.objects is null) continue;

            foreach (var obj in layer.objects)
            {
                var rect = new Rectangle((int)obj.x, (int)obj.y, (int)obj.width, (int)obj.height);

                switch (obj.type)
                {
                    case "playArea":
                        PlayArea = rect;
                        break;
                    case "collider":
                        colliders.Add(rect);
                        break;
                    case "interactable":
                        interactables.Add((obj.name ?? string.Empty, rect));
                        break;
                    case "startPoint":
                        StartPoint = new Vector2(obj.x + obj.width / 2f, obj.y + obj.height / 2f);
                        break;
                }
            }
        }

        Colliders     = colliders;
        Interactables = interactables;
    }

    // ── Drawing ────────────────────────────────────────────────────────────────

    public void Draw(SpriteBatch sb, Vector2 cameraOffset = default)
    {
        foreach (var layer in _map.Layers)
        {
            if (!layer.visible) continue;
            if (layer.type != TiledLayerType.TileLayer) continue;

            for (int i = 0; i < layer.data.Length; i++)
            {
                int gid = layer.data[i];
                if (gid == 0) continue;

                var mapTileset = _map.GetTiledMapTileset(gid);
                if (mapTileset is null) continue;

                if (!_tilesets.TryGetValue(mapTileset.firstgid, out var tileset)) continue;
                if (!_tileTextures.TryGetValue(mapTileset.firstgid, out var tex)) continue;

                var rect = _map.GetSourceRect(mapTileset, tileset, gid);
                var src  = new Rectangle(rect.x, rect.y, rect.width, rect.height);

                int col  = i % _map.Width;
                int row  = i / _map.Width;
                var dest = new Vector2(
                    col * TileSize - cameraOffset.X,
                    row * TileSize - cameraOffset.Y);

                sb.Draw(tex, dest, src, Color.White);
            }
        }
    }
}
