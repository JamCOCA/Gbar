using Microsoft.EntityFrameworkCore.Design;

namespace Gbar.Core.Persistence.Database;

/// <summary>
/// dotnet ef 设计时工厂：让迁移命令无需启动服务器即可构建 DbContext。
/// 用法：dotnet ef migrations add Init -- "Host=...;Database=gbar;Username=...;Password=..."
/// 未传连接串时用默认本地连接串。
/// </summary>
public sealed class GbarDbContextDesignFactory : IDesignTimeDbContextFactory<GbarDbContext>
{
    public GbarDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.Length > 0
            ? args[0]
            : "Host=localhost;Database=gbar;Username=postgres;Password=postgres";
        return new GbarDbContextFactory(connectionString).CreateDbContext();
    }
}
