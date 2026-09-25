using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gbar.Core.Persistence.Database;

/// <summary>
/// 从连接字符串构建 <see cref="GbarDbContext"/> 的工厂。
/// 每次操作新建短生命周期上下文，用完即释放，不跨操作复用（避免跟踪与并发问题）。
/// </summary>
public sealed class GbarDbContextFactory
{
    private readonly DbContextOptions<GbarDbContext> _options;

    public GbarDbContextFactory(string connectionString)
    {
        _options = new DbContextOptionsBuilder<GbarDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    public GbarDbContext CreateDbContext() => new(_options);

    /// <summary>
    /// 确保数据库与表结构存在：数据库不存在则创建库并建全部表；库已存在但没有表则补建表结构。
    /// 仅做「建库 / 建表」，不做既有结构迁移（增量结构变更应改用 EF Migrations）。
    /// </summary>
    public void EnsureCreated()
    {
        using var db = CreateDbContext();
        var creator = db.GetService<IRelationalDatabaseCreator>();

        if (!creator.Exists())
        {
            creator.Create();        // 创建数据库
            creator.CreateTables();  // 在新建库中建全部表
            return;
        }

        if (!creator.HasTables())
            creator.CreateTables();  // 库已存在但为空 → 补建表结构
    }
}
