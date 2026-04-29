using Multiplay.Shared;

namespace Multiplay.Server.Domain;

public enum EnemyAIState { Wander, Chase }

public sealed class Enemy
{
    public required EnemyInfo   Info          { get; set; }
    public required EnemyStats  Stats         { get; set; }
    public int                  Index         { get; init; } // index into SpawnPoints
    public float                DirX          { get; set; }
    public float                HopTime       { get; set; }
    public float                RespawnTimer  { get; set; } // > 0 → dead/respawning
    public EnemyAIState         AIState       { get; set; }
    public float                WanderTimer   { get; set; }
    public bool                 IsDead        => RespawnTimer > 0f;
}
