using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using TiledCS;

namespace Multiplay.Client.World;

/// <summary>
/// Loads a Tiled .tmx map and renders all visible tile layers each frame.
///
/// Workflow:
///   1. Create your map in Tiled using tileBaseTileset.png (16×16 tiles).
///   2. Save as .tmx (XML format) into Content/Maps/.
///   3. Pass the full path to the constructor, call <see cref="LoadContent"/>.
///   4. Call <see cref="Draw"/> in Game1.Draw().
///
/// The tileset PNG must be loaded via Content Pipeline before calling LoadContent.
/// Tileset textures are looked up by filename stem, e.g. "tileBaseTileset" →
/// Content.Load&lt;Texture2D&gt;("Tiles/tileBaseTileset").
/// </summary>
public sealed class TileMapRenderer
{
    public const int TileSize = 16;

    private readonly string _tmxPath;

    private TiledMap _map = null!;
    private Dictionary<int, TiledTileset>  _tilesets    = [];
    private Dictionary<int, Texture2D>     _tileTextures = []; // firstgid → texture

    public int MapWidthPixels  => _map.Width  * TileSize;
    public int MapHeightPixels => _map.Height * TileSize;

    public TileMapRenderer(string tmxPath) => _tmxPath = tmxPath;

    // ── Loading ────────────────────────────────────────────────────────────────

    public void LoadContent(ContentManager content)
    {
        _map      = new TiledMap(_tmxPath);
        var dir   = Path.GetDirectoryName(_tmxPath) ?? ".";
        _tilesets = _map.GetTiledTilesets(dir);

        // Load a texture for each referenced tileset
        foreach (var (firstgid, tileset) in _tilesets)
        {
            // tileset.Image.source is something like "../Tiles/tileBaseTileset.png"
            // Strip the extension and build the Content key
            var imgSource = tileset.Image?.source ?? string.Empty;
            var stem      = Path.GetFileNameWithoutExtension(imgSource);
            var contentKey = $"Tiles/{stem}";

            _tileTextures[firstgid] = content.Load<Texture2D>(contentKey);
        }
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
                if (gid == 0) continue; // empty cell

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
