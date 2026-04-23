using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Multiplay.Client.UI;

public sealed class Button
{
    public Rectangle Bounds  { get; set; }
    public string    Label   { get; set; } = "";
    public bool      Enabled { get; set; } = true;

    public event Action? Clicked;

    private static readonly Color BgNormal   = new(70,  70, 110);
    private static readonly Color BgHover    = new(90,  90, 140);
    private static readonly Color BgDisabled = new(50,  50,  70);
    private static readonly Color TextNormal  = Color.White;
    private static readonly Color TextDisabled = new(120, 120, 140);

    private readonly Texture2D _pixel;
    private MouseState _prev;
    private bool _hover;

    public Button(Texture2D pixel) => _pixel = pixel;

    public void Update()
    {
        var mouse = Mouse.GetState();
        _hover = Bounds.Contains(mouse.Position);

        if (Enabled && _hover
            && mouse.LeftButton    == ButtonState.Released
            && _prev.LeftButton    == ButtonState.Pressed)
        {
            Clicked?.Invoke();
        }

        _prev = mouse;
    }

    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        var bg = !Enabled ? BgDisabled : _hover ? BgHover : BgNormal;
        sb.Draw(_pixel, Bounds, bg);

        // 2 px border
        var border = !Enabled ? new Color(60, 60, 80) : new Color(120, 120, 180);
        DrawBorder(sb, Bounds, border, 2);

        var textColor = Enabled ? TextNormal : TextDisabled;
        var size = font.MeasureString(Label);
        var pos  = new Vector2(
            Bounds.X + (Bounds.Width  - size.X) / 2f,
            Bounds.Y + (Bounds.Height - size.Y) / 2f);

        sb.DrawString(font, Label, pos, textColor);
    }

    private void DrawBorder(SpriteBatch sb, Rectangle r, Color c, int t)
    {
        sb.Draw(_pixel, new Rectangle(r.X, r.Y,              r.Width, t), c);
        sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - t,     r.Width, t), c);
        sb.Draw(_pixel, new Rectangle(r.X, r.Y,              t, r.Height), c);
        sb.Draw(_pixel, new Rectangle(r.Right - t, r.Y,      t, r.Height), c);
    }
}
