namespace Multiplay.Server.Models;

public class User
{
    public int    Id           { get; set; }
    public string Username     { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? SessionToken { get; set; }
    /// <summary>Null until the player completes character setup.</summary>
    public string? DisplayName   { get; set; }
    public string? CharacterType { get; set; }
    /// <summary>Weapon the player chose from the Old Man ("Sword"/"Bow"/"Wand"). Null until chosen.</summary>
    public string? WeaponType    { get; set; }
    /// <summary>True after the player has collected the slime-kill quest reward once.</summary>
    public bool    SlimeQuestDone { get; set; }
    public int     Level          { get; set; }
    public int     Xp             { get; set; }
    public DateTime CreatedAt    { get; set; }
}
