using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Multiplay.Client.UI;

/// <summary>
/// Reusable in-game dialog box rendered in the HUD (unzoomed) pass.
/// The caller drives input: call <see cref="SelectPrev"/>/<see cref="SelectNext"/>
/// to move the cursor and read <see cref="SelectedIndex"/> on confirm.
/// </summary>
public sealed class DialogBox
{
    private const int BoxW        = 500;
    private const int BoxH        = 160;
    private const int Padding     = 12;
    private const int TabH        = 22;
    private const int PortraitSize = 120;  // square drawn size
    private const int PortraitPad  = 8;

    public string?   SpeakerName   { get; set; }
    public string    BodyText      { get; set; } = "";
    public string[]  Options       { get; set; } = [];
    public int       SelectedIndex { get; private set; }
    /// <summary>Optional portrait drawn on the left of the dialog box.</summary>
    public Texture2D? Portrait     { get; set; }

    public void SelectPrev() => SelectedIndex = Math.Max(0, SelectedIndex - 1);
    public void SelectNext() => SelectedIndex = Math.Min(Options.Length - 1, SelectedIndex + 1);

    /// <summary>Resets cursor to first option (call when opening).</summary>
    public void Reset() => SelectedIndex = 0;

    public void Draw(SpriteBatch sb, SpriteFont font, Texture2D pixel, int vw, int vh)
    {
        int boxX = (vw - BoxW) / 2;
        int boxY = vh - BoxH - 10;

        var bg     = new Color(20, 20, 40);
        var border = new Color(120, 120, 180);

        // Background
        sb.Draw(pixel, new Rectangle(boxX, boxY, BoxW, BoxH), bg);
        DrawBorder(sb, pixel, new Rectangle(boxX, boxY, BoxW, BoxH), border, 2);

        // Portrait (drawn flush against the left edge, vertically centred in the box)
        int contentX = boxX + Padding;
        if (Portrait is not null)
        {
            int px = boxX + BoxW + PortraitPad;
            int py = boxY + (BoxH - PortraitSize) / 2;
            sb.Draw(pixel, new Rectangle(px - 2, py - 2, PortraitSize + 4, PortraitSize + 4), border);
            sb.Draw(Portrait, new Rectangle(px, py, PortraitSize, PortraitSize), Color.White);
        }

        int y = boxY + Padding;

        // Speaker name tab
        if (!string.IsNullOrEmpty(SpeakerName))
        {
            var nameSize = font.MeasureString(SpeakerName);
            int tabW     = (int)nameSize.X + Padding * 2;
            sb.Draw(pixel, new Rectangle(boxX, boxY - TabH, tabW, TabH), new Color(60, 60, 120));
            DrawBorder(sb, pixel, new Rectangle(boxX, boxY - TabH, tabW, TabH), border, 2);
            sb.DrawString(font, SpeakerName,
                new Vector2(boxX + Padding, boxY - TabH + (TabH - nameSize.Y) / 2f),
                new Color(220, 200, 120));
        }

        // Body text (supports \n)
        if (!string.IsNullOrEmpty(BodyText))
        {
            sb.DrawString(font, BodyText, new Vector2(contentX, y), new Color(200, 200, 220));
            y += (int)(font.LineSpacing * (BodyText.Count(c => c == '\n') + 1)) + 8;
        }

        // Divider
        sb.Draw(pixel, new Rectangle(boxX + Padding, y, BoxW - Padding * 2, 1), border);
        y += 8;

        if (Options.Length == 0)
        {
            // Pure message — show a dismiss hint in the bottom-right corner
            const string hint = "[E]";
            var hintSize = font.MeasureString(hint);
            sb.DrawString(font, hint,
                new Vector2(boxX + BoxW - Padding - hintSize.X, boxY + BoxH - Padding - hintSize.Y),
                new Color(140, 140, 180));
        }
        else
        {
            foreach (var (opt, i) in Options.Select((o, i) => (o, i)))
            {
                bool selected = i == SelectedIndex;
                string line   = selected ? $"  > {opt}" : $"    {opt}";
                sb.DrawString(font, line, new Vector2(contentX, y),
                    selected ? Color.Yellow : Color.LightGray);
                y += font.LineSpacing + 2;
            }
        }
    }

    private static void DrawBorder(SpriteBatch sb, Texture2D pixel, Rectangle r, Color c, int t)
    {
        sb.Draw(pixel, new Rectangle(r.X,          r.Y,          r.Width, t), c);
        sb.Draw(pixel, new Rectangle(r.X,          r.Bottom - t, r.Width, t), c);
        sb.Draw(pixel, new Rectangle(r.X,          r.Y,          t, r.Height), c);
        sb.Draw(pixel, new Rectangle(r.Right - t,  r.Y,          t, r.Height), c);
    }
}
