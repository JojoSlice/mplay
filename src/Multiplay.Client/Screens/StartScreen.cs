using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Multiplay.Client.Services;
using Multiplay.Client.UI;

namespace Multiplay.Client.Screens;

public sealed class StartScreen : Screen
{
    private readonly IAuthService _auth;
    private SpriteFont _font = null!;
    private Texture2D  _pixel = null!;
    private Button     _loginBtn = null!;
    private Button     _registerBtn = null!;

    public StartScreen(IAuthService auth) => _auth = auth;

    public override void LoadContent(ContentManager content, GraphicsDevice gd)
    {
        _font  = content.Load<SpriteFont>("Fonts/Default");
        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData([Color.White]);

        _loginBtn = new Button(_pixel)
        {
            Label  = "Login",
            Bounds = new Rectangle(300, 300, 200, 48),
        };
        _registerBtn = new Button(_pixel)
        {
            Label  = "Register",
            Bounds = new Rectangle(300, 364, 200, 48),
        };

        _loginBtn.Clicked    += () => Manager.Navigate(new LoginScreen(_auth));
        _registerBtn.Clicked += () => Manager.Navigate(new RegisterScreen(_auth));
    }

    public override void Update(GameTime gameTime)
    {
        _loginBtn.Update();
        _registerBtn.Update();
    }

    public override void Draw(SpriteBatch sb, GraphicsDevice gd)
    {
        gd.Clear(new Color(20, 20, 35));
        sb.Begin();

        // Title
        var title = "Multiplay";
        var titleSize = _font.MeasureString(title) * 3f;
        sb.DrawString(_font, title,
            new Vector2((gd.Viewport.Width - titleSize.X) / 2f, 160),
            new Color(200, 180, 100), 0f, Vector2.Zero, 3f,
            SpriteEffects.None, 0f);

        _loginBtn.Draw(sb, _font);
        _registerBtn.Draw(sb, _font);

        sb.End();
    }
}
