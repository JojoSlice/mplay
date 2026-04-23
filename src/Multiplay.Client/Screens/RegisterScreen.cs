using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Multiplay.Client.Services;
using Multiplay.Client.UI;

namespace Multiplay.Client.Screens;

public sealed class RegisterScreen : Screen
{
    private readonly IAuthService _auth;
    private SpriteFont _font  = null!;
    private Texture2D  _pixel = null!;

    private TextInput _usernameInput = null!;
    private TextInput _passwordInput = null!;
    private TextInput _confirmInput  = null!;
    private Button    _registerBtn   = null!;
    private Button    _backBtn       = null!;

    private string? _errorMessage;
    private bool    _loading;
    private volatile string? _pendingResult;
    private KeyboardState _prevKb;

    public RegisterScreen(IAuthService auth) => _auth = auth;

    public override void LoadContent(ContentManager content, GraphicsDevice gd)
    {
        _font  = content.Load<SpriteFont>("Fonts/Default");
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData([Color.White]);

        int cx = gd.Viewport.Width / 2;

        _usernameInput = new TextInput(_pixel)
        {
            Bounds      = new Rectangle(cx - 150, 220, 300, 40),
            Placeholder = "Username  (max 32 chars)",
            IsFocused   = true,
            MaxLength   = 32,
        };
        _passwordInput = new TextInput(_pixel)
        {
            Bounds      = new Rectangle(cx - 150, 290, 300, 40),
            Placeholder = "Password  (min 6 chars)",
            IsPassword  = true,
        };
        _confirmInput = new TextInput(_pixel)
        {
            Bounds      = new Rectangle(cx - 150, 360, 300, 40),
            Placeholder = "Confirm password",
            IsPassword  = true,
        };
        _registerBtn = new Button(_pixel)
        {
            Label  = "Create account",
            Bounds = new Rectangle(cx - 150, 420, 300, 44),
        };
        _backBtn = new Button(_pixel)
        {
            Label  = "Back",
            Bounds = new Rectangle(cx - 150, 480, 140, 40),
        };

        _registerBtn.Clicked += StartRegister;
        _backBtn.Clicked     += () => Manager.Navigate(new StartScreen(_auth));
    }

    public override void Update(GameTime gameTime)
    {
        if (_pendingResult is not null)
        {
            if (_pendingResult == string.Empty)
            {
                Manager.Navigate(_auth.IsSetupDone
                    ? new GameScreen(_auth)
                    : new CharacterSelectScreen(_auth));
                return;
            }
            _errorMessage        = _pendingResult;
            _loading             = false;
            _registerBtn.Enabled = true;
            _pendingResult       = null;
        }

        if (_loading) return;

        var kb    = Keyboard.GetState();
        float dt  = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var mouse = Mouse.GetState();

        if (mouse.LeftButton == ButtonState.Pressed)
        {
            _usernameInput.IsFocused = _usernameInput.Bounds.Contains(mouse.Position);
            _passwordInput.IsFocused = _passwordInput.Bounds.Contains(mouse.Position);
            _confirmInput.IsFocused  = _confirmInput.Bounds.Contains(mouse.Position);
        }

        if (kb.IsKeyDown(Keys.Tab) && !_prevKb.IsKeyDown(Keys.Tab))
            CycleFocus();

        if (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter))
            StartRegister();

        _usernameInput.Update(dt);
        _passwordInput.Update(dt);
        _confirmInput.Update(dt);
        _registerBtn.Update();
        _backBtn.Update();

        _prevKb = kb;
    }

    public override void Draw(SpriteBatch sb, GraphicsDevice gd)
    {
        gd.Clear(new Color(20, 20, 35));
        sb.Begin();

        int cx = gd.Viewport.Width / 2;

        DrawCenteredText(sb, "Create account", cx, 150, 2f, new Color(200, 180, 100));
        DrawLeftText(sb, "Username", _usernameInput.Bounds.X, _usernameInput.Bounds.Y - 22);
        DrawLeftText(sb, "Password", _passwordInput.Bounds.X, _passwordInput.Bounds.Y - 22);
        DrawLeftText(sb, "Confirm password", _confirmInput.Bounds.X, _confirmInput.Bounds.Y - 22);

        _usernameInput.Draw(sb, _font);
        _passwordInput.Draw(sb, _font);
        _confirmInput.Draw(sb, _font);
        _registerBtn.Draw(sb, _font);
        _backBtn.Draw(sb, _font);

        if (_loading)
            DrawCenteredText(sb, "Creating account...", cx, 540, 1f, Color.Gray);
        else if (_errorMessage is not null)
            DrawCenteredText(sb, _errorMessage, cx, 540, 1f, new Color(220, 80, 80));

        sb.End();
    }

    private void StartRegister()
    {
        if (_loading) return;
        _errorMessage = null;

        var username = _usernameInput.Text.Trim();
        var password = _passwordInput.Text;
        var confirm  = _confirmInput.Text;

        if (username.Length == 0 || password.Length == 0 || confirm.Length == 0)
        {
            _errorMessage = "Please fill in all fields.";
            return;
        }

        if (password != confirm)
        {
            _errorMessage = "Passwords do not match.";
            return;
        }

        _loading             = true;
        _registerBtn.Enabled = false;

        Task.Run(async () =>
        {
            var error      = await _auth.RegisterAsync(username, password);
            _pendingResult = error ?? string.Empty;
        });
    }

    private void CycleFocus()
    {
        if (_usernameInput.IsFocused)      { _usernameInput.IsFocused = false; _passwordInput.IsFocused = true; }
        else if (_passwordInput.IsFocused) { _passwordInput.IsFocused = false; _confirmInput.IsFocused  = true; }
        else                               { _confirmInput.IsFocused  = false; _usernameInput.IsFocused = true; }
    }

    private void DrawCenteredText(SpriteBatch sb, string text, float cx, float y, float scale, Color color)
    {
        var size = _font.MeasureString(text) * scale;
        sb.DrawString(_font, text, new Vector2(cx - size.X / 2f, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void DrawLeftText(SpriteBatch sb, string text, float x, float y)
        => sb.DrawString(_font, text, new Vector2(x, y), new Color(160, 160, 200));
}
