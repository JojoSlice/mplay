using Microsoft.EntityFrameworkCore;
using Multiplay.Server.Models;

namespace Multiplay.Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<User>   Users   => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(32).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.SessionToken).HasMaxLength(64);
            e.Property(u => u.DisplayName).HasMaxLength(32);
            e.Property(u => u.CharacterType).HasMaxLength(32);
            e.Property(u => u.WeaponType).HasMaxLength(32);
            e.Property(u => u.SlimeQuestDone).IsRequired();
        });
    }
}
