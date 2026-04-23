using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Multiplay.Client.UI;

/// <summary>A single-line text input field driven by raw keyboard events.</summary>
public sealed class TextInput
{
    public Rectangle Bounds  { get; set; }
    public string    Text    { get; private set; } = "";
    public bool      IsPassword { get; init; }
    public bool      IsFocused  { get; set; }
    public int       MaxLength  { get; init; } = 32;

    private static readonly Color BgNormal  = new(40,  40,  55);
    private static readonly Color BgFocused = new(55,  55,  75);
    private static readonly Color Border    = new(120, 120, 160);
    private static readonly Color TextColor = Color.White;
    private static readonly Color PlaceholderColor = new(100, 100, 130);

    public string Placeholder { get; init; } = "";

    private KeyboardState _prev;
    private float _cursorBlink;
    private readonly Texture2D _pixel;

    public TextInput(Texture2D pixel) => _pixel = pixel;

    public void SetText(string text) =>
        Text = text.Length > MaxLength ? text[..MaxLength] : text;

    public void Update(float dt)
    {
        _cursorBlink = (_cursorBlink + dt) % 1f;

        if (!IsFocused) return;

        var curr = Keyboard.GetState();

        // Backspace
        if (IsNewPress(curr, _prev, Keys.Back) && Text.Length > 0)
            Text = Text[..^1];

        // Printable ASCII
        foreach (var key in curr.GetPressedKeys())
        {
            if (_prev.IsKeyDown(key)) continue;
            var ch = KeyToChar(key, curr.IsKeyDown(Keys.LeftShift) || curr.IsKeyDown(Keys.RightShift));
            if (ch != '\0' && Text.Length < MaxLength)
                Text += ch;
        }

        _prev = curr;
    }

    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        // Background
        sb.Draw(_pixel, Bounds, IsFocused ? BgFocused : BgNormal);

        // Border (1 px inset)
        DrawBorder(sb, Bounds, Border, 2);

        // Text / placeholder
        var display = Text.Length == 0
            ? Placeholder
            : (IsPassword ? new string('•', Text.Length) : Text);

        var color = Text.Length == 0 ? PlaceholderColor : TextColor;
        var textPos = new Vector2(Bounds.X + 8, Bounds.Y + (Bounds.Height - font.LineSpacing) / 2f);

        sb.DrawString(font, display, textPos, color);

        // Cursor
        if (IsFocused && _cursorBlink < 0.5f)
        {
            var measured = font.MeasureString(IsPassword ? new string('•', Text.Length) : Text);
            var cx = (int)(textPos.X + measured.X + 1);
            var cy = (int)textPos.Y;
            sb.Draw(_pixel, new Rectangle(cx, cy, 1, font.LineSpacing), TextColor);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void DrawBorder(SpriteBatch sb, Rectangle r, Color c, int thickness)
    {
        sb.Draw(_pixel, new Rectangle(r.X, r.Y,            r.Width, thickness), c);
        sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), c);
        sb.Draw(_pixel, new Rectangle(r.X, r.Y,            thickness, r.Height), c);
        sb.Draw(_pixel, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), c);
    }

    private static bool IsNewPress(KeyboardState curr, KeyboardState prev, Keys key)
        => curr.IsKeyDown(key) && !prev.IsKeyDown(key);

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));

        if (key >= Keys.D0 && key <= Keys.D9)
        {
            if (!shift) return (char)('0' + (key - Keys.D0));
            return key switch
            {
                Keys.D1 => '!', Keys.D2 => '@', Keys.D3 => '#', Keys.D4 => '$',
                Keys.D5 => '%', Keys.D6 => '^', Keys.D7 => '&', Keys.D8 => '*',
                Keys.D9 => '(', Keys.D0 => ')',
                _ => '\0'
            };
        }

        return key switch
        {
            Keys.Space          => ' ',
            Keys.OemPeriod      => shift ? '>' : '.',
            Keys.OemComma       => shift ? '<' : ',',
            Keys.OemMinus       => shift ? '_' : '-',
            Keys.OemPlus        => shift ? '+' : '=',
            Keys.OemQuestion    => shift ? '?' : '/',
            Keys.OemSemicolon   => shift ? ':' : ';',
            Keys.OemQuotes      => shift ? '"' : '\'',
            Keys.OemOpenBrackets  => shift ? '{' : '[',
            Keys.OemCloseBrackets => shift ? '}' : ']',
            Keys.OemBackslash   => shift ? '|' : '\\',
            Keys.OemTilde       => shift ? '~' : '`',
            _ => '\0'
        };
    }
}
