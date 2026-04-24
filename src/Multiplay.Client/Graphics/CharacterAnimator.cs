using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Multiplay.Shared;

namespace Multiplay.Client.Graphics;

public enum Direction  { S, N, E, W }
public enum PlayerAction { Idle, Walk, SwordAttack, ClassicSwordAttack, BowAttack, WandAttack, Jump, Death }

/// <summary>Common interface for all character animators.</summary>
public abstract class CharacterAnimator
{
    public abstract void LoadContent(ContentManager content);
    public abstract void SetDirection(Direction dir);
    public abstract void SetAction(PlayerAction action);
    public abstract void Update(float deltaSeconds);
    public abstract void Draw(SpriteBatch sb, Vector2 position, Color color, float scale = 1f);

    /// <summary>Factory: creates the right animator for the given character type string.</summary>
    public static CharacterAnimator Create(string characterType)
    {
        return characterType switch
        {
            CharacterType.ShieldKnight => new KnightAnimator("ShieldKnight"),
            CharacterType.SwordKnight  => new KnightAnimator("SwordKnight"),
            _                          => new ZinkAnimator(),
        };
    }
}
