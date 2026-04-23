using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Multiplay.Client.Screens;
using Multiplay.Client.Services;

namespace Multiplay.Client;

public class Game1 : Game
{
    private const string ServerBaseUrl = "http://localhost:5000/";

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;

    private readonly AuthService _auth;
    private ScreenManager _screens = null!;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth  = 800;
        _graphics.PreferredBackBufferHeight = 600;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        _auth = new AuthService(ServerBaseUrl);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _screens = new ScreenManager(Content, GraphicsDevice, new StartScreen(_auth));
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed
            || Keyboard.GetState().IsKeyDown(Keys.Escape))
        {
            Exit();
            return;
        }

        _screens.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        _screens.Draw(_spriteBatch);
        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _auth.Dispose();
        base.Dispose(disposing);
    }
}
