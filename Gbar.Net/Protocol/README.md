# Gbar 网络协议说明

本文档说明 `Gbar.Net` 协议层的封包格式、目录结构、收发流程，以及**如何新增一个数据包**。

## 目录结构

```
Gbar.Net/Protocol/
├── PacketAttribute.cs    # [Packet] 装饰器 + PacketDirection（包元数据唯一事实源）
├── PacketType.cs         # 包类型枚举（占封包首字节）
├── ProtocolInfo.cs       # 协议常量（版本号、共享密钥）
├── SyncComponentAttribute.cs  # [SyncComponent] 标记可同步组件（ID 可省，被源生成器消费）
├── Packets.cs            # 组件数组的自定义 MemoryPack formatter
├── Components/           # 可同步组件 struct（线格式本体，Arch 直接用作 ECS 组件）
│   ├── Position.cs       # [SyncComponent]（ID 由生成器自动分配）
│   ├── Velocity.cs       # [SyncComponent]
│   ├── Rotation.cs       # [SyncComponent]
│   ├── Health.cs         # [SyncComponent]
│   └── Cpc.cs            # [SyncComponent]
├── Entities/             # 实体同步数据定义
│   ├── EntityType.cs             # 实体类型枚举
│   ├── IComponentCodec.cs        # 组件编解码器接口（实现由生成器产出）
│   └── State/                    # 实体同步状态：全量 + 组件级增量
│       ├── EntitySpawn.cs            # 新实体全量状态（Keyframe/Spawned 用）
│       └── EntityUpdate.cs           # 组件级增量（Delta.Updated 用）
├── Packets/              # 各包体结构体（MemoryPack 可序列化）
└── Tools/                # 功能/管线类
    ├── PacketCodec.cs        # 序列化 + 压缩 + 分片 + 解压入口
    ├── PacketDispatcher.cs   # 接收侧分发（字典查表，无 switch）
    ├── PacketMeta.cs         # 从 [Packet] 读取元数据的静态泛型缓存
    ├── PacketCompressor.cs   # 压缩器接口 + 默认不压缩实现
    └── FragmentAssembler.cs  # 分片重组器
```

> 组件 ID 枚举、标签联合 `ComponentEntry`、各组件 codec、`ComponentCodecRegistry` 由
> `Gbar.Sync.Generator` 源生成器扫描 `[SyncComponent]` 后自动生成到 `Gbar.Net.Protocol.Entities`
> 命名空间，不落盘为源文件。

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

## 客户端接入

客户端是独立项目（Godot C# 或普通 .NET，目标框架 `net8.0`），只引用共享层 `Gbar.Net.dll`，
**不引用**服务器侧的 `Gbar.Core` / `Gbar.App` / `Gbar.Sync.Generator`。

### 引用与依赖

**开发期**（项目引用，改动自动同步）：

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="..\Gbar\Gbar.Net\Gbar.Net.csproj" />
  <PackageReference Include="MemoryPack" Version="1.21.4" />
  <PackageReference Include="LiteNetLib" Version="2.1.4" />
</ItemGroup>
```

**发布期**（DLL 引用）：把 `Gbar.Net.dll`、`LiteNetLib.dll`、`MemoryPack.Core.dll` 三个文件
（取自 `Gbar.Net/bin/Debug/net8.0/`）拷入客户端，用 `<Reference Include="...">` 引用。

### 连接与传输层初始化

客户端与服务器对称，必须使用相同的连接密钥与 XOR 混淆层（见 `NetServer.Start`）：

```csharp
using LiteNetLib;
using LiteNetLib.Layers;

var listener = new EventBasedNetListener();
var manager = new NetManager(listener, new XorEncryptLayer(ProtocolInfo.PacketLayerKey));

listener.NetworkReceiveEvent += (peer, reader, channel, method) => { /* 见下 */ };

manager.Start();
manager.Connect("服务器地址", 9050, "gbar");   // 密钥 "gbar" 与服务器 AcceptIfKey 一致
```

### 连接时序

```
客户端                             服务器
  │── Hello{ProtocolVersion} ──────▶│  校验版本，不符断开
  │── Auth{Mode,Account,Password}──▶│  校验账号 → 创建玩家实体
  │◀─────── Welcome{ClientId,        │
  │         TickRate,ServerTick,     │
  │         PlayerNetId} ────────────│
  │◀─────── Keyframe{Tick,Entities}──│  未对齐：每 tick 重发全量
  │── Ack{LastAppliedTick} ────────▶│  首次 Ack 后切入 Delta
  │◀─────── Delta{BaseTick,CurrTick, │  常态：组件级增量
  │         Spawned,Updated,         │
  │         Despawned} ──────────────│
```

### 收包处理（复用协议层管线，无需自己写 switch 分发）

`FragmentAssembler` / `PacketCodec` / `PacketDispatcher` 都在共享层 `Gbar.Net`，客户端直接复用：

```csharp
using Gbar.Net.Protocol;
using Gbar.Net.Protocol.Tools;
using Gbar.Net.Protocol.Entities;
using Gbar.Net.Protocol.Packets;

var dispatcher = new PacketDispatcher(PacketDirection.ServerToClient);
dispatcher.Register<WelcomePacket>((_, w) => OnWelcome(w));
dispatcher.Register<KeyframePacket>((_, k) => OnKeyframe(k));
dispatcher.Register<DeltaPacket>((_, d) => OnDelta(d));

var assembler = new FragmentAssembler();

// LiteNetLib NetworkReceiveEvent 里：
var data = new byte[reader.AvailableBytes];
reader.GetBytes(data, data.Length);
var frame = assembler.Accept(data);              // 分片则重组，未收齐返回 null
if (frame is not null)
    PacketCodec.TryDispatch(frame, 0, dispatcher);  // 解压 + 按首字节查表分发
```

### 应用组件到本地实体

`ComponentEntry[]` 是标签联合，按 `Id`（`ReplicatedComponent` 枚举）分发：

```csharp
void OnKeyframe(KeyframePacket k) {
    foreach (var e in k.Entities) Spawn(e.NetId, e.Type, e.Components);
}
void OnDelta(DeltaPacket d) {
    foreach (var s in d.Spawned)  Spawn(s.NetId, s.Type, s.Components);
    foreach (var u in d.Updated)  Apply(u.NetId, u.Components);
    foreach (var id in d.Despawned) Despawn(id);
}

void Apply(int netId, ComponentEntry[] comps) {
    foreach (var c in comps) {
        switch (c.Id) {
            case ReplicatedComponent.Position: SetPosition(netId, c.Position); break;
            case ReplicatedComponent.Velocity: SetVelocity(netId, c.Velocity); break;
            case ReplicatedComponent.Rotation: SetRotation(netId, c.Rotation); break;
            case ReplicatedComponent.Health:   SetHealth(netId, c.Health);     break;
            case ReplicatedComponent.Cpc:      SetCpc(netId, c.Cpc);           break;
        }
    }
}
```

### 客户端发包（Hello / Auth / Ack）

与服务器发送对称：`PacketCodec.EncodeFrame` 序列化 + 拼类型字节，按 `[Packet]` 声明的通道发送：

```csharp
manager.Send(peer, PacketCodec.EncodeFrame(new HelloPacket { ProtocolVersion = ProtocolInfo.Version }),
             PacketMeta<HelloPacket>.Channel);
manager.Send(peer, PacketCodec.EncodeFrame(new AckPacket { LastAppliedTick = lastTick }),
             PacketMeta<AckPacket>.Channel);
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

## 实体组件级增量

实体同步状态采用**组件标签化负载**（component-tagged payload）：实体状态不再是一个固定结构体，而是一组带 `ReplicatedComponent` 标签的小块，每个实体只携带它实际持有的组件（投射物只有 Position/Velocity，战斗单位另有 Health/Cpc）。

- `ReplicatedComponent`（byte）：组件 ID，由 `[SyncComponent]` 自动编号（字母序）或显式 `id` 决定，只增不删、终生不复用。
- `ComponentEntry`：标签联合（`Id` + 对应组件值），由源生成器产出。
- `ComponentEntryArrayFormatter`（`Packets.cs`）：手写序列化，按 `Id` 查表转发到生成器产出的 `IComponentCodec`，只写有效组件，避免联合结构体整块序列化（含空槽 padding）。

`EntitySpawn`（全量，含 `NetId + Type + 组件列表`）用于 `Keyframe.Entities` 与 `Delta.Spawned`；`EntityUpdate`（组件级增量，只带变化组件）用于 `Delta.Updated`；`Delta.Despawned` 为待销毁的 NetId。组件集合在实体生命周期内保持稳定（出生时由 `EntityType` 决定装配），故无需"组件移除"标记。

### 新增一个可复制组件

只需 **1 个 struct + 1 行特性**，其余由 `Gbar.Sync.Generator` 自动生成：

```csharp
// Gbar.Net/Protocol/Components/Stamina.cs
[SyncComponent]
public struct Stamina { public int Current; public int Max; }
```

`[SyncComponent]` 的 ID 可省略：生成器按类型全名字母序自动编号。仅当需要「跨版本固定线序」（老客户端对接新服务器）时，才显式写 `[SyncComponent(6)]`。

源生成器会自动产出：组件 ID 枚举成员、`ComponentEntry` 联合字段与 `From*` 工厂、`StaminaCodec`、注册表条目，以及服务器侧 `SyncReader` 的读取分支。序列化层（`Packets.cs`）与 `NetSyncSystem` 均无需改动。

## 协议常量（`ProtocolInfo`）

- `Version`：协议版本，`Hello` 校验不符则断开。
- `PacketLayerKey`：数据包层 XOR 混淆密钥（非强加密），两端必须一致。
