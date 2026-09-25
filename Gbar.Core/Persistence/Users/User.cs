namespace Gbar.Core.Persistence.Users;

/// <summary>
/// 用户账号实体，对应数据库 users 表（"user" 是 PostgreSQL 保留字，故用复数表名）。
/// 密码只存哈希（见 <see cref="PasswordHasher"/>），token 为登录会话令牌。
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Account { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}
