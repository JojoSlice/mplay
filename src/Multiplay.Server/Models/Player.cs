namespace Multiplay.Server.Models;

public class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public DateTime LastSeen { get; set; }
}
