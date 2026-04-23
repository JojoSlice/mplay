using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Multiplay.Client.Graphics;
using Multiplay.Client.Services;
using Multiplay.Client.UI;
using Multiplay.Shared;

namespace Multiplay.Client.Screens;

public sealed class CharacterSelectScreen : Screen
{
    private readonly AuthService _auth;

    private SpriteFont _font  = null!;
    private Texture2D  _pixel = null!;

    private TextInput _nameInput   = null!;
    private Button    _confirmBtn  = null!;

    private string? _errorMessage;
    private bool    _loading;
    private volatile string? _pendingResult;
    private KeyboardState _prevKb;
    private MouseState    _prevMouse;
    private int _viewportWidth;

    // ── Character options ──────────────────────────────────────────────────────

    private static readonly (string Type, string Label)[] Options =
    [
        (CharacterType.Zink,         "Zink"),
        (CharacterType.ShieldKnight, "Shield Knight"),
        (CharacterType.SwordKnight,  "Sword Knight"),
    ];

    private readonly CharacterAnimator[] _previews = new CharacterAnimator[3];
    private int _selectedIndex = 0;

    // Card layout
    private const int CardW     = 160;
    private const int CardH     = 200;
    private const int CardGap   = 24;
    private const int PreviewY  = 260; // center Y of the animated sprite in the card
    private const int PreviewScale = 4;

    public CharacterSelectScreen(AuthService auth) => _auth = auth;

    public override void LoadContent(ContentManager content, GraphicsDevice gd)
    {
        _font         = content.Load<SpriteFont>("Fonts/Default");
        _pixel        = new Texture2D(gd, 1, 1);
        _pixel.SetData([Color.White]);
        _viewportWidth = gd.Viewport.Width;

        // Pre-fill display name with username if available
        var nameHint = _auth.Username ?? "";

        int cx = gd.Viewport.Width / 2;

        _nameInput = new TextInput(_pixel)
        {
            Bounds      = new Rectangle(cx - 150, 155, 300, 40),
            Placeholder = "Choose a display name",
            IsFocused   = true,
            MaxLength   = 32,
        };
        // Pre-fill
        if (!string.IsNullOrEmpty(nameHint))
            SetInputText(_nameInput, nameHint);

        _confirmBtn = new Button(_pixel)
        {
            Label  = "Confirm",
            Bounds = new Rectangle(cx - 100, 430, 200, 44),
        };
        _confirmBtn.Clicked += StartSetup;

        // Load preview animators — all face South (idle walk frame)
        for (int i = 0; i < Options.Length; i++)
        {
            _previews[i] = CharacterAnimator.Create(Options[i].Type);
            _previews[i].LoadContent(content);
            _previews[i].SetDirection(Direction.S);
            _previews[i].SetAction(PlayerAction.Walk);
        }
    }

    public override void Update(GameTime gameTime)
    {
        if (_pendingResult is not null)
        {
            if (_pendingResult == string.Empty)
            {
                Manager.Navigate(new GameScreen(_auth));
                return;
            }
            _errorMessage      = _pendingResult;
            _loading           = false;
            _confirmBtn.Enabled = true;
            _pendingResult     = null;
        }

        if (_loading) return;

        var kb    = Keyboard.GetState();
        float dt  = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var mouse = Mouse.GetState();

        // Click on name input / card selection
        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
        {
            _nameInput.IsFocused = _nameInput.Bounds.Contains(mouse.Position);

            // Card click-to-select
            int totalW = Options.Length * CardW + (Options.Length - 1) * CardGap;
            int startX = _viewportWidth / 2 - totalW / 2;
            for (int i = 0; i < Options.Length; i++)
            {
                var cardRect = new Rectangle(startX + i * (CardW + CardGap), 200, CardW, CardH);
                if (cardRect.Contains(mouse.Position))
                    _selectedIndex = i;
            }
        }

        // Left / Right arrows to cycle character
        if (kb.IsKeyDown(Keys.Left)  && !_prevKb.IsKeyDown(Keys.Left))
            _selectedIndex = (_selectedIndex - 1 + Options.Length) % Options.Length;
        if (kb.IsKeyDown(Keys.Right) && !_prevKb.IsKeyDown(Keys.Right))
            _selectedIndex = (_selectedIndex + 1) % Options.Length;

        // Enter confirms
        if (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter))
            StartSetup();

        _nameInput.Update(dt);
        _confirmBtn.Update();

        // Animate all previews
        foreach (var p in _previews) p.Update(dt);

        _prevKb    = kb;
        _prevMouse = mouse;
    }

    public override void Draw(SpriteBatch sb, GraphicsDevice gd)
    {
        gd.Clear(new Color(20, 20, 35));
        sb.Begin(samplerState: SamplerState.PointClamp);

        int vw = gd.Viewport.Width;
        int cx = vw / 2;

        DrawCenteredText(sb, "Choose your character", cx, 60, 2f, new Color(200, 180, 100));
        DrawLeftText(sb, "Display name", _nameInput.Bounds.X, _nameInput.Bounds.Y - 22);

        _nameInput.Draw(sb, _font);

        // Character cards
        int totalW  = Options.Length * CardW + (Options.Length - 1) * CardGap;
        int startX  = cx - totalW / 2;

        for (int i = 0; i < Options.Length; i++)
        {
            var cardX = startX + i * (CardW + CardGap);
            bool selected = i == _selectedIndex;

            DrawCard(sb, i, cardX, selected, gd.Viewport.Width);
        }

        _confirmBtn.Draw(sb, _font);

        if (_loading)
            DrawCenteredText(sb, "Saving...", cx, 492, 1f, Color.Gray);
        else if (_errorMessage is not null)
            DrawCenteredText(sb, _errorMessage, cx, 492, 1f, new Color(220, 80, 80));

        sb.End();
    }

    private void DrawCard(SpriteBatch sb, int index, int x, bool selected, int viewportWidth)
    {
        var cardRect = new Rectangle(x, 200, CardW, CardH);

        // Background
        var bg = selected ? new Color(60, 60, 100) : new Color(35, 35, 55);
        sb.Draw(_pixel, cardRect, bg);

        // Border
        var borderColor = selected ? new Color(180, 160, 80) : new Color(80, 80, 120);
        DrawBorder(sb, cardRect, borderColor, selected ? 3 : 1);

        // Animated sprite, centered in card
        var spriteCenter = new Vector2(x + CardW / 2f, PreviewY);
        _previews[index].Draw(sb, spriteCenter, Color.White, PreviewScale);

        // Label
        var label = Options[index].Label;
        var labelSize = _font.MeasureString(label);
        sb.DrawString(_font, label,
            new Vector2(x + (CardW - labelSize.X) / 2f, cardRect.Bottom - 30),
            selected ? new Color(220, 200, 100) : new Color(180, 180, 220));

    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void StartSetup()
    {
        if (_loading) return;
        _errorMessage = null;

        var displayName   = _nameInput.Text.Trim();
        var characterType = Options[_selectedIndex].Type;

        if (displayName.Length == 0)
        {
            _errorMessage = "Please enter a display name.";
            return;
        }

        _loading            = true;
        _confirmBtn.Enabled = false;

        Task.Run(async () =>
        {
            var error      = await _auth.SetupAsync(displayName, characterType);
            _pendingResult = error ?? string.Empty;
        });
    }

    private void DrawBorder(SpriteBatch sb, Rectangle r, Color c, int t)
    {
        sb.Draw(_pixel, new Rectangle(r.X, r.Y,          r.Width, t), c);
        sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        sb.Draw(_pixel, new Rectangle(r.X, r.Y,          t, r.Height), c);
        sb.Draw(_pixel, new Rectangle(r.Right - t, r.Y,  t, r.Height), c);
    }

    private void DrawCenteredText(SpriteBatch sb, string text, float cx, float y, float scale, Color color)
    {
        var size = _font.MeasureString(text) * scale;
        sb.DrawString(_font, text, new Vector2(cx - size.X / 2f, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void DrawLeftText(SpriteBatch sb, string text, float x, float y)
        => sb.DrawString(_font, text, new Vector2(x, y), new Color(160, 160, 200));

    private static void SetInputText(TextInput input, string text) => input.SetText(text);
}
