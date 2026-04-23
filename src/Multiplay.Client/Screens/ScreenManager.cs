using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Multiplay.Client.Screens;

public sealed class ScreenManager
{
    private readonly ContentManager _content;
    private readonly GraphicsDevice _gd;
    private Screen _current;

    public ScreenManager(ContentManager content, GraphicsDevice gd, Screen initial)
    {
        _content = content;
        _gd      = gd;
        _current = initial;
        Activate(initial);
    }

    public void Navigate(Screen next)
    {
        Activate(next);
        _current = next;
    }

    private void Activate(Screen screen)
    {
        screen.Attach(this);
        screen.LoadContent(_content, _gd);
    }

    public void Update(GameTime gameTime) => _current.Update(gameTime);
    public void Draw(SpriteBatch sb)      => _current.Draw(sb, _gd);
}
