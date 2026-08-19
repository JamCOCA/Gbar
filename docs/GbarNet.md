# GbarNet 网络同步

服务器权威状态同步：新客户端**关键帧对齐一次**，之后每 tick 收**增量（Delta）**。

## 同步模型

- **Delta（常态）**：每 tick 只发变化实体（Spawned / Updated / Despawned），相对该连接**上次已 ACK 的快照**生成。走 Sequenced 通道，旧状态不重传，丢包只是多发数据，不错位。
- **Keyframe（全量快照）**：三种情况发——新连接对齐、周期纠偏、连续 N tick 未 ACK 回退。按 `ClientId % K` 分组错峰，避免发送尖峰。
- **Ack**：客户端回执"已应用到第 T tick"，服务器以此确定 Delta 基线。客户端校验 `Delta.BaseTick`，不符则丢弃等 Keyframe。

## 协议

封包格式：`[PacketType: 1 byte][MemoryPack 序列化包体]`，服务器与客户端共享 `Gbar.Net.Protocol` 里的定义。握手/可靠通道/心跳由 LiteNetLib 负责。

| 包 | 方向 | 说明 |
|---|---|---|
| `Hello` / `Welcome` | 握手 | 版本协商、分配 ClientId 与玩家本体 NetId；Welcome 携带 ServerTick 供两端 tick 对齐 |
| `Galaxy` | S→C | 星图表（天体轨道根数），Welcome 后一次性下发，之后天体位置两端各自按 tick 解析 |
| `Keyframe` | S→C | 全量快照（ReliableOrdered，需分片） |
| `Delta` | S→C | 常态增量（Sequenced） |
| `Ack` | C→S | 回执（Unreliable，可降频） |
| `Bye` | 双向 | 优雅断开 |

```csharp
[MemoryPackable]
public partial struct EntityState
{
    public int NetId;               // 4 B
    public int SpaceNetId;          // 4 B  所属空间 NetId；0 = 全局域
    public long PosX, PosY, PosZ;   // 24 B 定点（1:100）
    public long VelX, VelY, VelZ;   // 24 B 定点
}
// 合计 56 B/实体（MemoryPack 头部按 ~2 B 摊销，按 58 B 估算）
// 坐标必须 long 定点：星系级坐标超出 float 精度，且定点保证两端逐 bit 一致
```

## 天体同步：不同步位置

天体 on-rails，位置是 tick 的纯函数，**永不进 Keyframe/Delta**：

- 入网时 `Galaxy` 包一次性下发全部天体的 μ、半径、SOI、轨道根数、自转参数
- 两端用同一份根数 + 对齐的 tick 各自解析求值，结果逐 bit 一致
- 自转角同理：`Rotation = InitialRotation + AngularVelocity × tick`
- 确定性纪律：开普勒求解只用 +−×÷（牛顿迭代），不用 `Math.Sin/Cos` 等超越函数（.NET 不保证跨平台逐 bit 一致）
- 轨道变更（剧情事件）极少发生，发生时重发该天体的根数即可

连接时序：

```
C ── Hello ──→ S    校验版本，创建玩家本体（PlayerTag + NetworkId）
C ←─ Welcome ─ S    分配身份 + tick 对齐（ServerTick/TickRate）
C ←─ Galaxy ───S    星图表一次性下发（天体轨道根数）
C ←─ Keyframe ─S    帧边界采集的全量对齐（不含天体）
C ── Ack(T) ─→ S    基线建立
C ←─ Delta ─── S    常态循环（Delta 相对已 ACK 基线生成，丢包不累积错误）
C ── Ack ────→ S
C ── Bye ────→ S    按 PlayerTag 清理归属实体
```

连接成功时服务器创建玩家本体（`PlayerTag + NetworkId`），断开时按 `PlayerTag` 批量清理。

## 实现流程（当前代码）

网络已接入 ECS 管线，由两个系统 + 两处基础设施构成：

| 组件 | 位置 | 职责 |
|---|---|---|
| `NetServer` | `Gbar.Net/Server/NetServer.cs` | LiteNetLib 封装，后台轮询线程收发，事件入 `ConcurrentQueue` |
| `ClientRegistry` / `ClientSession` | `Gbar.Core/Net/ClientRegistry.cs` | 连接会话 + NetId 分配，仅主循环线程访问 |
| `NetServerSystem` | `Gbar.Core/Systems/NetServerSystem.cs` | 变步长（每帧），消费事件、握手、玩家实体生命周期 |
| `NetSyncSystem` | `Gbar.Core/Systems/NetSyncSystem.cs` | 定步长 Order 120（每 tick 末），生成 Keyframe/Delta |
| `GameServer.StartNet(port)` | `Gbar.Core/GameServer.cs` | 创建并启动 NetServer + 注册表 |

### 线程模型

```
LiteNetLib 后台线程 ──PollEvents──> NetServer.Events (ConcurrentQueue)
                                          │
主循环线程（GameServer.Loop）               │
  ├─ 变步长组：NetServerSystem.Update ─────┘ 逐条 TryDequeue 处理（握手/建实体/Ack/销毁）
  └─ 定步长组：... → NetSyncSystem.Update（Order 120）直接 net.Send 生成 Keyframe/Delta
```

- **网络线程永不碰 World**：它只把原始字节丢进 `Events` 队列
- **主循环线程独占 World 与注册表**：NetServerSystem 直接 `World.Create/Destroy` 是安全的（不在并行查询内，无需 CommandBuffer）
- **发送跨线程安全**：`NetSyncSystem` 在主线程调 `net.Send`，LiteNetLib 的 `NetPeer.Send` 本身线程安全

### 每帧流程

```
变步长组（每帧一次）
  NetServerSystem.Update:
    Connected      → registry.Add(clientId)
    Packet(Hello)  → 校验版本 → 分配 NetId → World.Create(PlayerTag+NetworkId+坐标)
                     → 发 Welcome（ServerTick/TickRate/PlayerNetId）
                     → 发 Galaxy（收集 Celestial+Orbit 构建星图表）
    Packet(Ack)    → session.BaselineTick = Ack.LastAppliedTick
    Packet(Bye)    → net.Disconnect
    Disconnected   → World.Destroy(玩家实体) + registry.Remove

定步长组（每 tick，含补帧）
  NetSyncSystem.Update (Order 120):
    收集同步实体（NetworkId+Global/LocalPosition）→ Dictionary<NetId, EntityState>
    对每个连接：
      未对齐  → 发 Keyframe（全量），标记 Synced，记录 LastSent 基线
      已对齐  → 与 LastSent diff → Spawned/Updated/Despawned → 发 Delta，更新 LastSent
```

### 与既有设计的差异（取舍说明）

1. **发送并入主线程**，没有再用 `SnapshotSystem + ConcurrentQueue` 的"发布/订阅"中转：因为 `NetPeer.Send` 线程安全，主线程直接发送少一次拷贝、少一次队列，时序也更直白。原方案留作网络线程需要独立节流/压缩时的演进方向
2. **Delta 未做 AOI 与空间剪枝**：当前对每个连接发全量 diff（`Spawned/Updated/Despawned`），正确但未按可见集过滤——AOI 落地后由 `SpaceAoiSystem/LocalAoiSystem` 提供可见集，本系统按集过滤即可
3. **NetId 由 `ClientRegistry` 单计数器分配**：尚未做回收复用（`NetIdRegistry` 回收在实体销毁时接入）

### 目录对应

```
Gbar.Net/           传输层（LiteNetLib + MemoryPack，不依赖 Gbar.Core）
  Server/NetServer.cs        服务器封装 + 事件队列
  Protocol/Packets.cs        包定义（EntityState/CelestialState/GalaxyPacket/...）
  Protocol/PacketCodec.cs    编解码 + 分发 + 通道选择
Gbar.Core/          ECS + 系统（依赖 Gbar.Net）
  Net/ClientRegistry.cs      会话 + NetId
  Systems/NetServerSystem.cs 握手/生命周期（UpdateSystem）
  Systems/NetSyncSystem.cs   快照/增量（FixedSystem, Order 120）
```

## 带宽结论（1000 实体 + 1000 玩家，20 tick/s）

- 无 AOI 全量广播：服务器下行 5.4 Gbps 级，**不可行，AOI 是硬需求**
- 有 AOI（每玩家可见 ~100 实体、10% 变化率）：服务器下行 ~95–155 Mbps，单玩家下行 ~12 KB/s，**可行**
- 天体流量为零（根数一次性下发），实体越大越密时这是关键减负

AOI 按 [GbarMotion.md](GbarMotion.md) 的分域索引实现，应提前到 MVP。

## 后续扩展（先不实现）

- AOI 可见集过滤：Enter/Leave 复用 Spawned/Despawned
- 大包 LZ4 压缩 + 分片重组
- 输入上行与客户端预测回滚（协议预留）
