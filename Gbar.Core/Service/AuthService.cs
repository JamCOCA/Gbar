using System.Security.Cryptography;
using Gbar.Core.Persistence.Users;

namespace Gbar.Core.Service;

/// <summary>认证结果。</summary>
public enum AuthResult
{
    Success = 0,
    InvalidCredentials = 1,
    AccountExists = 2,
    InvalidInput = 3,
}

/// <summary>
/// 认证服务：注册与登录的业务逻辑，依赖 <see cref="UserRepository"/> 访问数据库。
/// 不依赖 ECS，不属于游戏系统；由 App 装配，NetServerSystem 在处理 Auth 包时调用。
/// 方法为 async，便于将来把 DB I/O 移出主循环线程（当前调用方同步等待）。
/// </summary>
public sealed class AuthService
{
    private readonly UserRepository _users;

    public AuthService(UserRepository users) => _users = users;

    /// <summary>注册：账号不存在则建号，签发并落库新 token。返回结果、用户与 token。</summary>
    public async Task<(AuthResult Result, User? User, string? Token)> RegisterAsync(string account, string password)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            return (AuthResult.InvalidInput, null, null);

        if (await _users.GetByAccountAsync(account) is not null)
            return (AuthResult.AccountExists, null, null);

        var user = await _users.CreateAsync(account, password);
        if (user is null)
            return (AuthResult.AccountExists, null, null);   // 并发下唯一约束兜底

        var token = IssueToken();
        await _users.UpdateTokenAsync(user.Id, token);
        return (AuthResult.Success, user, token);
    }

    /// <summary>登录：校验账号密码，通过则签发并落库新 token。</summary>
    public async Task<(AuthResult Result, User? User, string? Token)> LoginAsync(string account, string password)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            return (AuthResult.InvalidInput, null, null);

        var user = await _users.VerifyAsync(account, password);
        if (user is null)
            return (AuthResult.InvalidCredentials, null, null);

        var token = IssueToken();
        await _users.UpdateTokenAsync(user.Id, token);
        return (AuthResult.Success, user, token);
    }

    /// <summary>生成 32 字节加密随机数的 hex 令牌。</summary>
    private static string IssueToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
