namespace Gbar.Core.Systems;

/// <summary>
/// 指定系统的执行顺序（仅反射自动注册时生效）。
/// 数值越小越先执行；未标注的系统按 0 处理，同级按类型名字典序排列。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SystemOrderAttribute : Attribute
{
    public int Order { get; }

    public SystemOrderAttribute(int order) => Order = order;
}
