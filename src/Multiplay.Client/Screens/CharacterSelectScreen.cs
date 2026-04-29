using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Multiplay.Client.Services;
using Multiplay.Client.UI;
using Multiplay.Shared;

namespace Multiplay.Client.Screens;

public sealed class CharacterSelectScreen : Screen
{
    private readonly IAuthService _auth;

    private SpriteFont _font  = null!;
    private Texture2D  _pixel = null!;

    private TextInput _nameInput   = null!;
    private Button    _confirmBtn  = null!;

    private string? _errorMessage;
    private bool    _loading;
    private volatile string? _pendingResult;
    private KeyboardState _prevKb;
    private MouseState    _prevMouse;

    public CharacterSelectScreen(IAuthService auth) => _auth = auth;

    public override void LoadContent(ContentManager content, GraphicsDevice gd)
    {
        _font  = content.Load<SpriteFont>("Fonts/Default");
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData([Color.White]);

        int cx = gd.Viewport.Width / 2;

        _nameInput = new TextInput(_pixel)
        {
            Bounds      = new Rectangle(cx - 150, 155, 300, 40),
            Placeholder = "Choose a display name",
            IsFocused   = true,
            MaxLength   = 32,
        };

        var nameHint = _auth.Username ?? "";
        if (!string.IsNullOrEmpty(nameHint))
            _nameInput.SetText(nameHint);

        _confirmBtn = new Button(_pixel)
        {
            Label  = "Confirm",
            Bounds = new Rectangle(cx - 100, 230, 200, 44),
        };
        _confirmBtn.Clicked += StartSetup;
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
            _errorMessage       = _pendingResult;
            _loading            = false;
            _confirmBtn.Enabled = true;
            _pendingResult      = null;
        }

        if (_loading) return;

        var kb    = Keyboard.GetState();
        float dt  = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var mouse = Mouse.GetState();

        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
            _nameInput.IsFocused = _nameInput.Bounds.Contains(mouse.Position);

        if (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter))
            StartSetup();

        _nameInput.Update(dt);
        _confirmBtn.Update();

        _prevKb    = kb;
        _prevMouse = mouse;
    }

    public override void Draw(SpriteBatch sb, GraphicsDevice gd)
    {
        gd.Clear(new Color(20, 20, 35));
        sb.Begin(samplerState: SamplerState.PointClamp);

        int cx = gd.Viewport.Width / 2;

        DrawCenteredText(sb, "Enter your name", cx, 60, 2f, new Color(200, 180, 100));
        DrawLeftText(sb, "Display name", _nameInput.Bounds.X, _nameInput.Bounds.Y - 22);

        _nameInput.Draw(sb, _font);
        _confirmBtn.Draw(sb, _font);

        if (_loading)
            DrawCenteredText(sb, "Saving...", cx, 290, 1f, Color.Gray);
        else if (_errorMessage is not null)
            DrawCenteredText(sb, _errorMessage, cx, 290, 1f, new Color(220, 80, 80));

        sb.End();
    }

    private void StartSetup()
    {
        if (_loading) return;
        _errorMessage = null;

        var displayName = _nameInput.Text.Trim();

        if (displayName.Length == 0)
        {
            _errorMessage = "Please enter a display name.";
            return;
        }

        _loading            = true;
        _confirmBtn.Enabled = false;

        Task.Run(async () =>
        {
            var error      = await _auth.SetupAsync(displayName, CharacterType.Zink);
            _pendingResult = error ?? string.Empty;
        });
    }

    private void DrawCenteredText(SpriteBatch sb, string text, float cx, float y, float scale, Color color)
    {
        var size = _font.MeasureString(text) * scale;
        sb.DrawString(_font, text, new Vector2(cx - size.X / 2f, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void DrawLeftText(SpriteBatch sb, string text, float x, float y)
        => sb.DrawString(_font, text, new Vector2(x, y), new Color(160, 160, 200));
}
