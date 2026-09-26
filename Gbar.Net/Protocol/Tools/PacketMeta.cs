using System.Reflection;
using LiteNetLib;

namespace Gbar.Net.Protocol.Tools;

/// <summary>
/// 从 <see cref="PacketAttribute"/> 读取包元数据的静态泛型缓存。
/// 静态泛型字段每个 T 只初始化一次，GetCustomAttribute 仅在首次访问执行，之后为纯字段读；
/// 发送热路径（Delta/Keyframe）无反射开销。
/// </summary>
public static class PacketMeta<T> where T : struct
{
    public static readonly PacketAttribute Attr =
        typeof(T).GetCustomAttribute<PacketAttribute>()
        ?? throw new InvalidOperationException($"结构体 {typeof(T)} 缺少 [Packet] 特性");

    public static readonly byte Type = (byte)Attr.Type;
    public static readonly DeliveryMethod Channel = Attr.Channel;
    public static readonly PacketDirection Direction = Attr.Direction;
}
