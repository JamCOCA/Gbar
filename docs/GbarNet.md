# GbarNet 网络协议与数据包处理

服务器权威：客户端与服务器共享 `Gbar.Net.Protocol` 里的线上字节标准，两端用同一套 `PacketType` 枚举与 MemoryPack 结构体。

> 封包字节格式、压缩/分片细节、新增包与新增可复制组件的步骤见 `Gbar.Net/Protocol/README.md`；本文只讲服务器侧的同步模型与收发流程。

## 包类型

| 包 | 方向 | 说明 |
|---|---|---|
| `Hello` | C→S | 版本协商 |
| `Auth` | C→S | 注册/登录（`AuthMode` + `Account` + `Password`） |
| `Welcome` | S→C | 认证通过后分配身份（`ClientId`/`PlayerNetId`/`ServerTick`） |
| `AuthFail` | S→C | 认证失败（`AuthFailReason`） |
| `MoveInput` | C→S | 移动输入（方向轴 + `MoveFlags`：running/jumping/crouch） |
| `Keyframe` | S→C | 全量快照（新连接对齐 / 周期纠偏） |
| `Delta` | S→C | 常态增量（相对已 ACK 基线） |
| `Ack` | C→S | 回执，建立同步基线 |
| `Bye` | 双向 | 优雅断开 |

方向与通道都在各包结构体的 `[Packet]` 特性上声明，是唯一事实源；接收侧由 `PacketDispatcher` 校验方向。

## 处理流程

### 1. 网络线程收包（`NetServer`）

后台轮询线程驱动 LiteNetLib，收到数据先分片重组，再把完整帧塞进事件队列交给主循环：

```csharp
// NetServer.cs  NetworkReceiveEvent
var data = new byte[reader.AvailableBytes];
reader.GetBytes(data, data.Length);
var frame = _assembler.Accept(data);   // 分片重组，未收齐返回 null
if (frame is not null)
    _events.Enqueue(NetEvent.Packet(peer.Id, frame));
```

### 2. 主循环取包（`NetServerSystem.Update`）

变步长系统每帧从队列取出事件，按来源 ClientId 分发：

```csharp
// NetServerSystem.cs  Update (Order 0)
case NetEventKind.Packet:
    if (ev.Data is null || !PacketCodec.TryDispatch(ev.Data, ev.ClientId, net.Dispatcher))
        net.Disconnect(ev.ClientId, "非法封包");
    break;
```

### 3. 解压与分发（`PacketCodec.TryDispatch` → `PacketDispatcher.Dispatch`）

`TryDispatch` 处理压缩标志后交给分发器；后者读首字节当 `PacketType`，按类型查字典路由到处理器，clientId 通过参数传入：

```csharp
// PacketDispatcher.cs  Dispatch
var type = message[0];                 // 首字节 = PacketType
if (!_handlers.TryGetValue(type, out var handler)) return false;
handler.Handle(clientId, message[1..]); // 反序列化 → 回调 OnXxx(clientId, packet)
return true;
```

### 4. 注册处理器（`NetServerSystem.Initialize`）

服务器在 `Initialize` 阶段把 C→S 包注册到 `NetServer.Dispatcher`，clientId 通过回调参数传入，无需再手动追踪来源连接：

```csharp
net.RegisterHandler<HelloPacket>(OnHello);
net.RegisterHandler<AuthPacket>(OnAuth);
net.RegisterHandler<AckPacket>(OnAck);
net.RegisterHandler<ByePacket>(OnBye);
net.RegisterHandler<MoveInputPacket>(OnMoveInput);
```

以登录为例：客户端发 `PacketType.Auth` + `AuthPacket` 序列化字节 → `PacketDispatcher.Dispatch` 命中对应处理器 → 调 `NetServerSystem.OnAuth(clientId, packet)`，clientId 即来源连接。

## 握手时序

```
C ── Hello ──→ S    校验版本，标记待认证
C ── Auth ───→ S    注册/登录 → 成功分配 NetId、创建玩家实体
C ←─ AuthFail─ S    认证失败，可重试
C ←─ Welcome ─ S    认证成功，分配身份
C ←─ Keyframe ─S    全量快照（未对齐期间每 tick 重发）
C ── Ack ────→ S    建立基线（BaselineTick）
C ←─ Delta ─── S    常态增量循环
```

## 同步模型

- **关键帧对齐一次，之后常态 Delta**：新连接连上先收 Keyframe 全量快照，客户端应用后回 `Ack`（携带 `LastAppliedTick`），服务器据此把该连接的 `BaselineTick` 推进到该 tick，此后每 tick 只发相对基线的增量。
- **组件级增量**：实体状态是带 `ReplicatedComponent` 标签的 `ComponentEntry[]` 标签联合，每个实体只携带它实际持有的组件；`Delta.Updated` 只发变化的组件（`EntitySpawn` 全量 / `EntityUpdate` 增量），`Delta.Despawned` 为待销毁的 NetId。
- **AOI 可见集剪枝**：`NetSyncSystem` 按玩家 `AoiCell` 只同步同一 cell 或相邻 cell（半径 10 格）内的实体，避免全量广播。
- **周期纠偏**：每 `KeyframeIntervalTicks`（默认 10 秒）重发一次 Keyframe，各连接按 `(tick + ClientId) % interval` 错峰，防止长期增量累积漂移。
- **对齐只由 Ack 驱动**：Keyframe 发送本身不置位 `Synced`，避免 Sequenced Delta 抢在 Keyframe 之前造成"基线空洞"。

## 目录对应

```
Gbar.Net/Protocol/
  PacketType.cs        包类型枚举（首字节）
  PacketAttribute.cs   [Packet] 特性（类型/通道/方向唯一事实源）
  PacketCodec.cs       编解码 + 压缩 + 分片（Tools/）
  PacketDispatcher.cs  接收侧分发（字典查表，无 switch）（Tools/）
  Packets/             各包结构体定义（Hello/Welcome/Auth/AuthFail/MoveInput/Keyframe/Delta/Ack/Bye）
  Components/          可同步组件（Position/Velocity/Rotation/Health/Cpc）
  Entities/            实体同步数据定义（EntityType + State/EntitySpawn + State/EntityUpdate）
  README.md            封包格式 / 新增包 / 新增可复制组件

Gbar.Core/
  Net/NetServer.cs           LiteNetLib 封装 + 事件队列 + 分片发送/重组
  Net/NetEvent.cs            网络事件（Connected/Packet/Disconnected）
  Net/ClientRegistry.cs      连接会话 + NetId + 握手状态
  Service/AuthService.cs     注册/登录/签发 token
  Systems/NetServerSystem.cs 握手/认证/生命周期（UpdateSystem, Order 0）
  Systems/NetSyncSystem.cs   快照/增量生成（FixedSystem, Order 120）
```
