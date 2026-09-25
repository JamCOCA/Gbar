using Gbar.Core.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Gbar.Core.Persistence.Users;

/// <summary>
/// 用户账号仓储：封装 users 表的增删改查与认证辅助。
/// 每个方法独立创建短生命周期 DbContext，用完即释放；只读查询用 AsNoTracking，写操作用跟踪实体。
/// </summary>
public sealed class UserRepository
{
    private readonly GbarDbContextFactory _factory;

    public UserRepository(GbarDbContextFactory factory) => _factory = factory;

    // ---- 增 ----

    /// <summary>创建账号。账号已存在时返回 null（唯一约束由数据库兜底）。</summary>
    public async Task<User?> CreateAsync(string account, string password)
    {
        await using var db = _factory.CreateDbContext();
        if (await db.Users.AnyAsync(u => u.Account == account)) return null;

        var user = new User
        {
            Account = account,
            PasswordHash = PasswordHasher.Hash(password),
            Token = string.Empty,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    // ---- 查 ----

    public async Task<User?> GetByIdAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<User?> GetByAccountAsync(string account)
    {
        await using var db = _factory.CreateDbContext();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Account == account);
    }

    public async Task<User?> GetByTokenAsync(string token)
    {
        await using var db = _factory.CreateDbContext();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Token == token);
    }

    // ---- 改 ----

    public async Task<bool> UpdatePasswordAsync(int id, string newPassword)
    {
        await using var db = _factory.CreateDbContext();
        var user = await db.Users.FindAsync(id);
        if (user is null) return false;
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateTokenAsync(int id, string token)
    {
        await using var db = _factory.CreateDbContext();
        var user = await db.Users.FindAsync(id);
        if (user is null) return false;
        user.Token = token;
        await db.SaveChangesAsync();
        return true;
    }

    // ---- 删 ----

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = _factory.CreateDbContext();
        var user = await db.Users.FindAsync(id);
        if (user is null) return false;
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return true;
    }

    // ---- 认证辅助 ----

    /// <summary>校验账号密码，通过则返回用户实体，否则返回 null。</summary>
    public async Task<User?> VerifyAsync(string account, string password)
    {
        await using var db = _factory.CreateDbContext();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Account == account);
        if (user is null) return null;
        return PasswordHasher.Verify(password, user.PasswordHash) ? user : null;
    }
}
