using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Multiplay.Client.Services;
using Multiplay.Client.UI;

namespace Multiplay.Client.Screens;

public sealed class LoginScreen : Screen
{
    private readonly AuthService _auth;
    private SpriteFont _font  = null!;
    private Texture2D  _pixel = null!;

    private TextInput _usernameInput = null!;
    private TextInput _passwordInput = null!;
    private Button    _loginBtn      = null!;
    private Button    _backBtn       = null!;

    private string? _errorMessage;
    private bool    _loading;

    // Result from async login task, consumed on the game thread next Update
    private volatile string? _pendingResult; // null = not done, "" = success, else error msg

    private KeyboardState _prevKb;

    public LoginScreen(AuthService auth) => _auth = auth;

    public override void LoadContent(ContentManager content, GraphicsDevice gd)
    {
        _font  = content.Load<SpriteFont>("Fonts/Default");
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData([Color.White]);

        int cx = gd.Viewport.Width / 2;

        _usernameInput = new TextInput(_pixel)
        {
            Bounds      = new Rectangle(cx - 150, 240, 300, 40),
            Placeholder = "Username",
            IsFocused   = true,
        };
        _passwordInput = new TextInput(_pixel)
        {
            Bounds      = new Rectangle(cx - 150, 300, 300, 40),
            Placeholder = "Password",
            IsPassword  = true,
        };
        _loginBtn = new Button(_pixel)
        {
            Label  = "Login",
            Bounds = new Rectangle(cx - 150, 360, 300, 44),
        };
        _backBtn = new Button(_pixel)
        {
            Label  = "Back",
            Bounds = new Rectangle(cx - 150, 420, 140, 40),
        };

        _loginBtn.Clicked += StartLogin;
        _backBtn.Clicked  += () => Manager.Navigate(new StartScreen(_auth));
    }

    public override void Update(GameTime gameTime)
    {
        // Consume async result if ready
        if (_pendingResult is not null)
        {
            if (_pendingResult == string.Empty)
            {
                Manager.Navigate(_auth.IsSetupDone
                    ? new GameScreen(_auth)
                    : new CharacterSelectScreen(_auth));
                return;
            }
            _errorMessage     = _pendingResult;
            _loading          = false;
            _loginBtn.Enabled = true;
            _pendingResult    = null;
        }

        if (_loading) return;

        var kb    = Keyboard.GetState();
        float dt  = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var mouse = Mouse.GetState();

        // Click-to-focus
        if (mouse.LeftButton == ButtonState.Pressed)
        {
            _usernameInput.IsFocused = _usernameInput.Bounds.Contains(mouse.Position);
            _passwordInput.IsFocused = _passwordInput.Bounds.Contains(mouse.Position);
        }

        // Tab switches focus
        if (kb.IsKeyDown(Keys.Tab) && !_prevKb.IsKeyDown(Keys.Tab))
        {
            _usernameInput.IsFocused = !_usernameInput.IsFocused;
            _passwordInput.IsFocused = !_passwordInput.IsFocused;
        }

        // Enter submits
        if (kb.IsKeyDown(Keys.Enter) && !_prevKb.IsKeyDown(Keys.Enter))
            StartLogin();

        _usernameInput.Update(dt);
        _passwordInput.Update(dt);
        _loginBtn.Update();
        _backBtn.Update();

        _prevKb = kb;
    }

    public override void Draw(SpriteBatch sb, GraphicsDevice gd)
    {
        gd.Clear(new Color(20, 20, 35));
        sb.Begin();

        int cx = gd.Viewport.Width / 2;

        DrawCenteredText(sb, "Login", cx, 170, 2f, new Color(200, 180, 100));
        DrawLeftText(sb, "Username", _usernameInput.Bounds.X, _usernameInput.Bounds.Y - 22);
        DrawLeftText(sb, "Password", _passwordInput.Bounds.X, _passwordInput.Bounds.Y - 22);

        _usernameInput.Draw(sb, _font);
        _passwordInput.Draw(sb, _font);
        _loginBtn.Draw(sb, _font);
        _backBtn.Draw(sb, _font);

        if (_loading)
            DrawCenteredText(sb, "Logging in...", cx, 476, 1f, Color.Gray);
        else if (_errorMessage is not null)
            DrawCenteredText(sb, _errorMessage, cx, 476, 1f, new Color(220, 80, 80));

        sb.End();
    }

    private void StartLogin()
    {
        if (_loading) return;
        _errorMessage = null;

        var username = _usernameInput.Text.Trim();
        var password = _passwordInput.Text;

        if (username.Length == 0 || password.Length == 0)
        {
            _errorMessage = "Please fill in all fields.";
            return;
        }

        _loading          = true;
        _loginBtn.Enabled = false;

        Task.Run(async () =>
        {
            var error    = await _auth.LoginAsync(username, password);
            _pendingResult = error ?? string.Empty; // "" = success
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
