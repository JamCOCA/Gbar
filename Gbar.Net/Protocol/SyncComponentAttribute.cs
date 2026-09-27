namespace Gbar.Net.Protocol;

/// <summary>
/// 标记一个 struct 为「可同步组件」。组件 struct 即线格式本体，两端共用；
/// 源生成器据此自动生成组件 ID 枚举、标签联合、编解码器、注册表与服务器侧的读取器。
/// <see cref="Id"/> 可省略：省略时由生成器按类型全名字母序自动分配稳定编号；
/// 仅在需要「跨版本固定线序」（如老客户端对接新服务器）时显式指定。
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class SyncComponentAttribute : Attribute
{
    /// <summary>组件 ID（可空）。空 = 由生成器自动分配。</summary>
    public int? Id { get; }

    public SyncComponentAttribute()
    {
        Id = null;
    }

    public SyncComponentAttribute(int id)
    {
        Id = id;
    }
}
