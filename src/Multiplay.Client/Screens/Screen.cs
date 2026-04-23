using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Multiplay.Client.Screens;

public abstract class Screen
{
    protected ScreenManager Manager { get; private set; } = null!;

    internal void Attach(ScreenManager manager) => Manager = manager;

    public virtual void LoadContent(ContentManager content, GraphicsDevice gd) { }
    public abstract void Update(GameTime gameTime);
    public abstract void Draw(SpriteBatch sb, GraphicsDevice gd);
}
