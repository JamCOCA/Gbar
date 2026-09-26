using MemoryPack;

namespace Gbar.Net.Protocol.Tools;

/// <summary>
/// 接收侧分发器：按包首字节查字典路由到对应处理器，无 switch。
/// 构造时声明本端方向，Register 时校验包方向，方向不符直接抛错。
/// </summary>
public sealed class PacketDispatcher
{
    private readonly PacketDirection _direction;
    private readonly Dictionary<byte, IPacketHandler> _handlers = new();

    public PacketDispatcher(PacketDirection direction) => _direction = direction;

    /// <summary>注册一个包处理器，类型字节与方向来自 <see cref="PacketAttribute"/>。</summary>
    public void Register<T>(Action<int, T> callback) where T : struct
    {
        if (callback is null) throw new ArgumentNullException(nameof(callback));

        var meta = PacketMeta<T>.Attr;
        if (_direction != PacketDirection.Both
            && meta.Direction != PacketDirection.Both
            && meta.Direction != _direction)
            throw new InvalidOperationException(
                $"包方向不符：{typeof(T)} 是 {meta.Direction}，本分发器只处理 {_direction}");

        var type = (byte)meta.Type;
        if (_handlers.ContainsKey(type))
            throw new InvalidOperationException($"重复注册包类型 {meta.Type}");
        _handlers[type] = new PacketHandler<T>(callback);
    }

    /// <summary>分发一条未压缩应用消息 [type][body]，字典查表。返回 false 表示未知类型或反序列化失败。</summary>
    public bool Dispatch(int clientId, ReadOnlySpan<byte> message)
    {
        if (message.Length < 1) return false;
        if (!_handlers.TryGetValue(message[0], out var handler)) return false;
        try
        {
            handler.Handle(clientId, message[1..]);
            return true;
        }
        catch
        {
            return false;   // MemoryPack 反序列化失败视为非法包
        }
    }

    private interface IPacketHandler
    {
        void Handle(int clientId, ReadOnlySpan<byte> body);
    }

    /// <summary>泛型持有者：反序列化与回调都在泛型类内部完成，结构体 T 不装箱。</summary>
    private sealed class PacketHandler<T> : IPacketHandler where T : struct
    {
        private readonly Action<int, T> _callback;
        public PacketHandler(Action<int, T> callback) => _callback = callback;
        public void Handle(int clientId, ReadOnlySpan<byte> body)
            => _callback(clientId, MemoryPackSerializer.Deserialize<T>(body));
    }
}
