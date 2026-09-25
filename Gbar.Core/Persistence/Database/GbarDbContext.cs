using Gbar.Core.Persistence.Users;
using Microsoft.EntityFrameworkCore;

namespace Gbar.Core.Persistence.Database;

/// <summary>
/// PostgreSQL 数据库上下文。用户表映射见 <see cref="OnModelCreating"/>。
/// </summary>
public class GbarDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();

    public GbarDbContext(DbContextOptions<GbarDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).ValueGeneratedOnAdd();
            entity.Property(u => u.Account).IsRequired().HasMaxLength(64);
            entity.HasIndex(u => u.Account).IsUnique();          // 账号唯一，数据库层兜底
            entity.Property(u => u.PasswordHash).IsRequired().HasMaxLength(128);
            entity.Property(u => u.Token).HasMaxLength(128);
        });
    }
}
