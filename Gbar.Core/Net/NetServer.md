# Gbar.Net — 协议层说明

> `NetServer` 已迁至 `Gbar.Core/Net/NetServer.cs`（命名空间 `Gbar.Core.Net`），本文件描述其运行机制。

`NetServer`（位于 `Gbar.Core/Net/NetServer.cs`）是基于 LiteNetLib 的 UDP 游戏服务器封装，负责客户端连接管理与数据包收发。

## 整体架构

- **后台轮询线程**：LiteNetLib 的网络事件由名为 `Gbar.NetServer` 的后台线程驱动（`PollLoop`），每 1ms 调用一次 `PollEvents()`。
- **事件队列**：网络回调（连接、收包、断开）不直接交给游戏逻辑，而是封装为 `NetEvent` 放入线程安全的 `ConcurrentQueue<NetEvent>`（`Events` 属性），由游戏主线程自行取出处理。这样避免了多线程访问游戏状态的问题。

## 运行流程

### 1. 启动 — `Start(port, maxConnections)`

1. 创建 `EventBasedNetListener` 和 `NetManager`（开启 `AutoRecycle` 自动回收读取器）。
2. 注册四个网络回调：
   - **ConnectionRequestEvent**：校验连接密钥，仅接受 key 为 `"gbar"` 的连接。
   - **PeerConnectedEvent**：将 `NetPeer` 存入 `_peers` 字典（键为 `peer.Id`），并投递 `Connected` 事件。
   - **PeerDisconnectedEvent**：从 `_peers` 移除，投递 `Disconnected` 事件（附带断开原因）。
   - **NetworkReceiveEvent**：读取全部可用字节，投递 `Packet` 事件。
3. 调用 `_manager.Start(port)` 绑定端口，失败则抛出 `InvalidOperationException`。
4. 置 `_running = true`，启动后台轮询线程。

### 2. 事件消费（游戏主线程）

游戏主循环中轮询 `Events` 队列：

```csharp
while (server.Events.TryDequeue(out var e))
{
    switch (e.Kind)
    {
        case NetEventKind.Connected:    /* 新客户端加入 */ break;
        case NetEventKind.Packet:       /* 解码 e.Data 并处理 */ break;
        case NetEventKind.Disconnected: /* 清理该客户端 */ break;
    }
}
```

### 3. 发送 — `Send<T>(clientId, type, body)`

通过 `PacketCodec.Encode` 将结构体序列化为字节数组，并按 `PacketCodec.ChannelFor(type)` 选择通道与投递方式发送。客户端不存在时静默忽略。

### 4. 主动断开 — `Disconnect(clientId, reason)`

先以 `ReliableOrdered` 方式发送 `Bye` 包告知客户端断开原因，再调用 `peer.Disconnect()`。

### 5. 停止 — `Stop()` / `Dispose()`

1. 置 `_running = false` 并 `Join` 等待轮询线程退出。
2. 停止 `NetManager`，清空 `_peers`。
3. `Dispose()` 等价于 `Stop()`，支持 `using` 语义。

## 关键设计点

| 点 | 说明 |
|---|---|
| 线程模型 | 网络在后台线程，事件经队列转交主线程，游戏逻辑单线程化 |
| 连接鉴权 | 固定密钥 `"gbar"`（`AcceptIfKey`），协议版本见 `Gbar.Net.Protocol.ProtocolInfo.Version` |
| 可靠性 | 通道与投递方式由 `PacketCodec.ChannelFor` 按包类型决定 |
| 关闭安全 | `Stop` 幂等，先停线程再停 NetManager，避免回调访问已释放对象 |
