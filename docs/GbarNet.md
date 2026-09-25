# GbarNet 网络同步

服务器权威状态同步：新客户端**关键帧对齐一次**，之后每 tick 收**增量（Delta）**。

## 同步模型

- **Delta（常态）**：每 tick 只发变化（Spawned / Updated / Despawned），相对该连接**上次已 ACK 的快照**生成。Updated 是**字段级增量**（按 `EntityFields` 脏位掩码只发变化的坐标/速度字段，见下）。走 Sequenced 通道，旧状态不重传，丢包只是多发数据，不错位。超单包上限的大包由传输层自动分片重组（Keyframe/Delta 统一），业务协议无感知。
- **Keyframe（全量快照）**：两种情况发——(1) **新连接对齐**：客户端首次 Ack 之前每 tick 重发，直到确认已应用；(2) **周期纠偏**：每 `KeyframeIntervalTicks`（默认 10s）按 `(tick + ClientId) % interval == 0` 错峰发一次，避免发送尖峰。
- **Ack**：客户端回执"已应用到第 T tick"，服务器以此确定 Delta 基线（`session.BaselineTick`），并把该连接切到"已对齐"。客户端校验 `Delta.BaseTick`，不符则丢弃等 Keyframe。
- **AOI 可见集过滤**：`AoiSystem` 每 tick 把带 `Position` 的实体写入 `AoiCell`（整数网格 cell），`NetSyncSystem` 只同步与玩家同一 cell 或相邻 cell（`VisibilityRadiusCells = 1`，即 3×3×3 = 27 格）的实体。

## 协议

封包格式：`[PacketType: 1 byte][MemoryPack 序列化包体]`，服务器与客户端共享 `Gbar.Net.Protocol` 里的定义。握手/可靠通道/心跳由 LiteNetLib 负责。

传输层统一封包管线（Keyframe/Delta 共用，业务协议不感知分包/压缩）：

```
普通帧:  [PacketType: 1][MemoryPack body]
压缩帧:  [0xFE: 1][PacketType: 1][compressed body]
分片包:  [0xFF: 1][MessageId: 2][FragIndex: 2][FragCount: 2][frame fragment]   # 7 字节头 + 帧片段
```

- **压缩**：应用消息超过 `MinCompressBytes`（默认 256）且压缩有收益时出现压缩帧。以整个帧为单位（包级），不是单个 Entity——批量数据才有跨实体冗余可压。默认不压缩（`NullPacketCompressor`），留 `IPacketCompressor` 钩子，后续换 LZ4/Zstd 只改 `PacketCodec.Compressor`。
- **分片**：帧超过该通道 `GetMaxSinglePacketSize()` 时拆成多个分片包；接收端按 `MessageId + FragIndex` 顺序重组后，再解压分发。丢片/乱序丢弃整个消息，靠 Keyframe 兜底。收发双向对称：`NetServer` 与 `NetClient` 各持一个 `FragmentAssembler` 重组入站分片、一个 `_nextMessageId` 分配出站分片序号。
- ReliableOrdered（Keyframe 等）由 LiteNetLib 底层分片，单包上限足够大，不会走到应用层分片；仅 Sequenced（Delta）会触发。

| 包 | 方向 | 说明 |
|---|---|---|
| `Hello` / `Welcome` | 握手 | 版本协商、分配 ClientId 与玩家本体 NetId；Welcome 携带 ClientId/TickRate/ServerTick/PlayerNetId 供两端 tick 对齐 |
| `Keyframe` | S→C | 全量快照（ReliableOrdered，LiteNetLib 底层分片） |
| `Delta` | S→C | 常态增量：Spawned 全量 / Updated 字段级（Sequenced；超限由传输层自动分片重组） |
| `Ack` | C→S | 回执（Unreliable，可降频） |
| `Bye` | 双向 | 优雅断开，携带原因 |

> 原 `Galaxy` 星图表包（天体轨道根数）已随天体 on-rails 方案的搁置而移除——当前代码无天体实体，`OnHello` 不再下发星图表（见"与既有设计的差异"）。

```csharp
[MemoryPackable]
public partial struct EntityState
{
    public int NetId;                   // 4 B
    public EntityType Type;             // 1 B 实体类型（出生时确定、终生不变，仅 Spawned/Keyframe 全量下发）
    public double PosX, PosY, PosZ;     // 24 B 位置（米）
    public double VelX, VelY, VelZ;     // 24 B 速度（米/秒）
}
// 合计 53 B/实体（数组有集合头，按 ~55 B/实体摊销估算）
// 坐标为双精度浮点（米），服务器权威；客户端做插值/预测即可，无需逐 bit 一致。
// 单一世界坐标系，不再区分全局域/局部域（SpaceNetId 已移除）。
// Type 是粗粒度分类标签、终生不变，不进 EntityFields 增量掩码（见下），
// 细粒度数据（怪物层级/物品种类/资源熟成度等）走组件或按 ArchetypeId 查静态表，不进本结构。

// 字段级增量（Delta.Updated 的元素）：Mask 指示哪些字段在本 tick 变化，
// 自定义 formatter 只序列化被标记的字段，未变化的字段不占字节。
[Flags]
public enum EntityFields : byte
{
    None = 0,
    PosX = 1 << 0, PosY = 1 << 1, PosZ = 1 << 2,
    VelX = 1 << 3, VelY = 1 << 4, VelZ = 1 << 5,
}

public struct EntityDelta
{
    public int NetId;               // 4 B  标识符，恒定发送
    public byte Mask;               // 1 B  EntityFields 脏位掩码
    public double PosX, PosY, PosZ; // 8 B/字段，仅当对应位被标记
    public double VelX, VelY, VelZ; // 8 B/字段，仅当对应位被标记
}
// 例：仅 PosX 变化 → 4+1+8 = 13 B（实体级 53 B 的约 25%）；三个位置都变 → 4+1+24 = 29 B（约 55%）
// EntityDelta 是 unmanaged struct，MemoryPack 默认会按内存布局整块序列化（含 padding），
// 故由 EntityDeltaArrayFormatter 手写序列化覆盖（见 Protocol/Packets.cs）。
```

## 连接时序

```
C ── Hello ──→ S    校验版本，分配 NetId，创建玩家本体（PlayerTag + EntityTypeTag + NetworkId + Position + Velocity + AoiCell）
C ←─ Welcome ─ S    分配身份 + tick 对齐（ClientId/TickRate/ServerTick/PlayerNetId）
C ←─ Keyframe ─S    未对齐期间每 tick 重发全量快照（按可见集过滤）
C ── Ack(T) ─→ S    基线建立（BaselineTick = T，Synced = true）
C ←─ Delta ─── S    常态循环（Delta 相对已 ACK 基线生成，丢包不累积错误）
C ── Ack ────→ S
C ── Bye ────→ S    销毁该玩家的 PlayerEntity
```

连接成功时服务器创建玩家本体（`PlayerTag + EntityTypeTag + NetworkId`），断开时按会话里的 `PlayerEntity` 引用销毁。

## 实现流程（当前代码）

网络已接入 ECS 管线，由三个定步长系统（运动 → AOI → 同步）+ 一个变步长系统 + 两处基础设施构成：

| 组件 | 位置 | 职责 |
|---|---|---|
| `NetServer` | `Gbar.Core/Net/NetServer.cs` | LiteNetLib 封装，后台轮询线程收发，事件入 `ConcurrentQueue`；出站分片 + 入站 `FragmentAssembler` 重组 |
| `NetEvent` | `Gbar.Core/Net/NetEvent.cs` | 网络事件（Connected/Packet/Disconnected） |
| `ClientRegistry` / `ClientSession` | `Gbar.Core/Net/ClientRegistry.cs` | 连接会话 + NetId 分配，仅主循环线程访问 |
| `NetServerSystem` | `Gbar.Core/Systems/NetServerSystem.cs` | 变步长 Order 0（每帧），消费事件、握手、玩家实体生命周期 |
| `MovementSystem` | `Gbar.Core/Systems/MovementSystem.cs` | 定步长 Order 60，运动学积分（pos += vel·Δt），唯一位置写入点 |
| `AoiSystem` | `Gbar.Core/Systems/AoiSystem.cs` | 定步长 Order 100，按 `Position` 现算 `AoiCell` cell 坐标 |
| `NetSyncSystem` | `Gbar.Core/Systems/NetSyncSystem.cs` | 定步长 Order 120（每 tick 末），按 AOI 可见集生成 Keyframe/Delta |
| `GameServer.StartNet(port)` | `Gbar.Core/GameServer.cs` | 创建并启动 NetServer + 注册表 |

### 线程模型

```
LiteNetLib 后台线程 ──PollEvents──> NetServer.Events (ConcurrentQueue<NetEvent>)
                                          │
主循环线程（GameServer.Loop）               │
  ├─ 变步长组：NetServerSystem.Update ─────┘ 逐条 TryDequeue 处理（握手/建实体/Ack/销毁）
  └─ 定步长组：MovementSystem → AoiSystem → NetSyncSystem（Order 120）直接 net.Send 生成 Keyframe/Delta
```

- **网络线程永不碰 World**：它只做字节级的分片重组（`FragmentAssembler`），把完整帧丢进 `Events` 队列
- **主循环线程独占 World 与注册表**：NetServerSystem 直接 `World.Create/Destroy` 是安全的（不在并行查询内，无需 CommandBuffer）
- **发送跨线程安全**：`NetSyncSystem` 在主线程调 `net.Send`，LiteNetLib 的 `NetPeer.Send` 本身线程安全

### 每帧流程

```
变步长组（每帧一次）
  NetServerSystem.Update (Order 0):
    Connected      → registry.Add(clientId)
    Packet(Hello)  → 校验版本 → 分配 NetId → World.Create(PlayerTag+EntityTypeTag+NetworkId+Position+Velocity+AoiCell)
                     → 发 Welcome（ClientId/TickRate/ServerTick/PlayerNetId）
    Packet(Ack)    → session.BaselineTick = Ack.LastAppliedTick; session.Synced = true
    Packet(Bye)    → net.Disconnect
    Disconnected   → World.Destroy(玩家实体) + registry.Remove

定步长组（每 tick，含补帧）
  MovementSystem.Update (Order 60)   位置 = 位置 + 速度 × Δt
  AoiSystem.Update       (Order 100) 按 Position 现算 AoiCell
  NetSyncSystem.Update   (Order 120):
    收集同步实体（NetworkId + Position(+Velocity) + AoiCell(+EntityTypeTag)）→ 全部状态列表，EntityTypeTag 缺失时 Type 记 None
    对每个连接：
      按玩家 AoiCell 过滤可见集（同一/相邻 cell）
      未对齐  → 发 Keyframe（全量，可见集），记录 LastSent 基线
      已对齐  → 与 LastSent diff → Spawned/Updated/Despawned → 发 Delta（BaseTick/CurrTick），更新 LastSent
```

### 与既有设计的差异（取舍说明）

1. **发送并入主线程**，没有再用 `SnapshotSystem + ConcurrentQueue` 的"发布/订阅"中转：因为 `NetPeer.Send` 线程安全，主线程直接发送少一次拷贝、少一次队列，时序也更直白。原方案留作网络线程需要独立节流/压缩时的演进方向
2. **AOI 已落地但实现简化**：当前用单一 `AoiCell` 整数网格（`CellSize = 100m`，邻域 1 格），未采用 GbarMotion.md 规划的 `SpaceAoiSystem/LocalAoiSystem` 双域分层索引；双域坐标方案已搁置（`Position` 为单一世界坐标系），单层 grid 即可满足当前需求
3. **天体同步（Galaxy/Celestial/Orbit）未落地**：当前代码无天体实体，`Galaxy` 包与"on-rails 天体"整段均未实现，属规划中的 M2 内容；`NetServerSystem.OnHello` 不再发 Galaxy
4. **NetId 由 `ClientRegistry` 单计数器分配**：尚未做回收复用（`NetIdRegistry` 回收在实体销毁时接入）

### 目录对应

```
Gbar.Net/           协议层（服务器/客户端共享的线上字节标准，不依赖任何游戏逻辑）
  Protocol/PacketType.cs        包类型枚举（封包首字节）
  Protocol/ProtocolInfo.cs      协议版本号等标准常量（Version = 3）
  Protocol/Packets.cs           EntityDelta 数组手写 formatter（字段级增量序列化）
  Protocol/Entities/            实体类型与同步负载（EntityType/EntityState/EntityDelta/EntityFields）
  Protocol/Packets/             包定义（Hello/Welcome/Keyframe/Delta/Ack/Bye）
  Protocol/PacketCodec.cs       编解码 + 分发 + 通道选择 + 压缩/分片
  Protocol/PacketCompressor.cs  包级压缩钩子（IPacketCompressor，默认不压缩）
  Protocol/FragmentAssembler.cs 分片重组器（按 MessageId 顺序累积）
  Protocol/IPacketHandler.cs    包分发回调接口（服务器/客户端各自实现自己方向的回调）
  Client/NetClient.cs           客户端传输封装（Python thinclient 经 pythonnet 使用；Godot 客户端生命周期自行实现时可作参考）
Gbar.Core/          ECS + 系统（依赖 Gbar.Net）
  Net/NetServer.cs            LiteNetLib 服务器封装 + 事件队列 + 分片发送/重组
  Net/NetEvent.cs             网络事件结构（Connected/Packet/Disconnected）
  Net/ClientRegistry.cs       会话 + NetId
  Systems/NetServerSystem.cs  握手/生命周期（UpdateSystem, Order 0）
  Systems/MovementSystem.cs   运动学积分（FixedSystem, Order 60）
  Systems/AoiSystem.cs        AOI cell 标签（FixedSystem, Order 100）
  Systems/NetSyncSystem.cs    快照/增量（FixedSystem, Order 120）
```

## 带宽结论（1000 实体 + 1000 玩家，20 tick/s）

- 无 AOI 全量广播：服务器下行 5.4 Gbps 级，**不可行，AOI 是硬需求**
- 有 AOI（每玩家可见 ~100 实体、10% 变化率）：服务器下行 ~95–155 Mbps，单玩家下行 ~12 KB/s，**可行**

AOI 已按单层 `AoiCell` 网格实现（`AoiSystem` 现算 cell + `NetSyncSystem` 可见集过滤）；后续如需双域分层索引再按 GbarMotion.md 演进。

## 后续扩展（先不实现）

- 双域 AOI 分层索引：`SpaceAoiSystem/LocalAoiSystem`（随双域坐标方案恢复时落地）
- LZ4/Zstd 压缩：实现 `IPacketCompressor` 并赋给 `PacketCodec.Compressor`（分片已落地，压缩仅缺算法实现）
- 输入上行与客户端预测回滚（协议预留）
