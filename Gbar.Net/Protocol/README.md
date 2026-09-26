# Gbar 网络协议说明

本文档说明 `Gbar.Net` 协议层的封包格式、目录结构、收发流程，以及**如何新增一个数据包**。

## 目录结构

```
Gbar.Net/Protocol/
├── PacketAttribute.cs    # [Packet] 装饰器 + PacketDirection（包元数据唯一事实源）
├── PacketType.cs         # 包类型枚举（占封包首字节）
├── ProtocolInfo.cs       # 协议常量（版本号、共享密钥）
├── Packets.cs            # EntityDelta 数组的自定义 MemoryPack formatter
├── Entities/             # 实体同步数据定义
│   ├── EntityType.cs     # 实体类型枚举
│   ├── EntityState.cs    # 实体全量状态（Keyframe/Spawned 用）
│   └── EntityDelta.cs    # 字段级增量 + 脏位掩码 EntityFields
├── Packets/              # 各包体结构体（MemoryPack 可序列化）
└── Tools/                # 功能/管线类
    ├── PacketCodec.cs        # 序列化 + 压缩 + 分片 + 解压入口
    ├── PacketDispatcher.cs   # 接收侧分发（字典查表，无 switch）
    ├── PacketMeta.cs         # 从 [Packet] 读取元数据的静态泛型缓存
    ├── PacketCompressor.cs   # 压缩器接口 + 默认不压缩实现
    └── FragmentAssembler.cs  # 分片重组器
```

> 服务端传输封装（`NetServer`/`NetEvent`/`ClientRegistry`）位于 `Gbar.Core/Net`，
> 依赖 ECS，故保留在 Core，不进入协议层。

## 封包格式

### 应用消息（未压缩）

```
[ PacketType: 1 byte ][ MemoryPack 序列化的包体 ]
```

首字节是 `PacketType` 枚举值，其余是 `MemoryPackSerializer` 序列化出的包体。

### 传输帧

应用消息超过 `PacketCodec.MinCompressBytes`（默认 256）且压缩有收益时，加压缩标志：

```
[ CompressFlag 0xFE: 1 byte ][ 压缩后的应用消息 ]
```

否则原样发送应用消息（首字节即 `PacketType`）。

### 分片包

仅 `Sequenced` 通道需要应用层分片（`ReliableOrdered` 由 LiteNetLib 底层自动分片）：

```
[ FragmentFlag 0xFF ][ MessageId: 2 ][ FragIndex: 2 ][ FragCount: 2 ][ 帧片段 ]
```

分片头共 7 字节（`PacketCodec.FragmentHeaderSize`）。

## 包类型一览

| PacketType | 方向 | 通道 | 说明 |
|---|---|---|---|
| Hello | C→S | ReliableOrdered | 版本协商 |
| Welcome | S→C | ReliableOrdered | 分配身份（ClientId/PlayerNetId/ServerTick） |
| Keyframe | S→C | ReliableOrdered | 全量快照 |
| Delta | S→C | Sequenced | 常态增量 |
| Ack | C→S | Unreliable | 回执 |
| Bye | 双向 | ReliableOrdered | 优雅断开 |
| Auth | C→S | ReliableOrdered | 注册/登录 |
| AuthFail | S→C | ReliableOrdered | 认证失败 |

方向与通道都在各包结构体的 `[Packet]` 特性上声明，是唯一事实源。

## 核心组件

### `PacketAttribute` / `PacketMeta<T>`

每个包结构体标注 `[Packet(类型, 通道, 方向)]`，作为该包所有协议信息的唯一来源：

```csharp
[Packet(PacketType.Hello, DeliveryMethod.ReliableOrdered, PacketDirection.ClientToServer)]
[MemoryPackable]
public partial struct HelloPacket
{
    public int ProtocolVersion;
}
```

`PacketMeta<T>` 用静态泛型字段缓存该特性（每类型仅反射一次，热路径零反射），供发送端读通道/类型。

### `PacketCodec`（发送侧 + 解压入口）

- `Encode<T>(in T body)`：序列化 + 拼类型字节 → 应用消息。
- `EncodeFrame<T>(in T body)`：应用消息 → 传输帧（按需压缩）。
- `SplitFrame(...)`：传输帧 → 分片序列。
- `TryDispatch(frame, clientId, dispatcher)`：解压后交给分发器。

### `PacketDispatcher`（接收侧分发）

按首字节查字典路由，**无 switch**：

- `Register<T>(Action<int, T>)`：注册处理器，校验方向、防重复注册。
- `Dispatch(clientId, message)`：查表 → 反序列化 → 回调，未知类型/反序列化失败返回 `false`。

泛型持有者 `PacketHandler<T>` 保证结构体 `T` 不装箱。

## 收发流程

### 发送

```
net.Send<T>(clientId, body)
  → PacketMeta<T>.Channel 取通道
  → PacketCodec.EncodeFrame 序列化 + 压缩
  → 超上限则 SplitFrame 分片
  → peer.Send
```

### 接收

```
LiteNetLib 收到原始字节
  → FragmentAssembler.Accept（分片重组）
  → 事件队列 NetEvent.Packet
  → NetServerSystem.Update 取出
  → PacketCodec.TryDispatch（解压）
  → NetServer.Dispatcher.Dispatch（按首字节查表）
  → 命中处理器 OnXxx(clientId, packet)
```

## 如何新增一个数据包

以新增 C→S 的 `Chat` 包为例：

**1. 加枚举成员**（`PacketType.cs`）

```csharp
Chat,
```

**2. 建包体结构体**（`Packets/ChatPacket.cs`）

```csharp
[Packet(PacketType.Chat, DeliveryMethod.ReliableOrdered, PacketDirection.ClientToServer)]
[MemoryPackable]
public partial struct ChatPacket
{
    public string Text;
}
```

**3. 注册处理器**（`NetServerSystem.Initialize`）

```csharp
net.RegisterHandler<ChatPacket>(OnChat);
```

**4. 写处理逻辑**

```csharp
private void OnChat(int clientId, ChatPacket packet)
{
    Log.Info($"客户端 {clientId} 说：{packet.Text}");
}
```

其余（分发、通道选择、接口）全部由 `[Packet]` 特性与分发器自动完成，无需改动。

> 方向不符会抛异常：服务器只注册 C→S（含 Both）包，注册 S→C 包会直接失败。

## 实体字段级增量

`DeltaPacket.Updated` 使用字段级增量，只序列化变化的字段：

- `EntityFields`（byte 掩码）：`PosX/PosY/PosZ/VelX/VelY/VelZ` 六个位。
- `EntityDelta`：`NetId + Mask + 各字段值`。
- `EntityDeltaArrayFormatter`（`Packets.cs`）：手写序列化，按 `Mask` 只写变化字段，避免 unmanaged struct 默认整块序列化（含 padding）。

`EntityState`（全量）用于 `Keyframe.Entities` 与 `Delta.Spawned`；`EntityDelta`（增量）用于 `Delta.Updated`；`Delta.Despawned` 为待销毁的 NetId。

## 协议常量（`ProtocolInfo`）

- `Version`：协议版本，`Hello` 校验不符则断开。
- `PacketLayerKey`：数据包层 XOR 混淆密钥（非强加密），两端必须一致。
