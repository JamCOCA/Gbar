# GbarNet 网络同步架构

基于 [开发路线](../.kilo/plans/1786541457997-network-sync-roadmap.md)。服务器权威状态同步：新客户端**关键帧对齐一次**，之后每 tick 收**增量（Delta）**。

## 一、同步模型

```
tick:  0    1    2    3    4    5    6 ...
       K    D    D    D    D    D    D     ← 连接 A
            K    D    D    D    D    D     ← 连接 B（错峰收关键帧）

K = Keyframe 全量快照   D = Delta 增量
```

- **Delta（常态）**：每 tick 只发变化实体（Spawned / Updated / Despawned），相对该连接**上次已 ACK 的快照**生成；
- **Keyframe**：三种情况发——新连接对齐、周期纠偏、连续 N tick 未 ACK 回退重建；
- **关键帧错峰**：连接按 `ClientId % K` 分组，各组在不同 tick 收周期 Keyframe，避免全量发送尖峰。

## 二、协议包

传输层握手/可靠通道/心跳由 LiteNetLib 自动完成，以下是应用层协议（我们自己定义）。

### 封包格式

```
[ PacketType: 1 byte ][ MemoryPack 序列化的包体 ]
```

包体为 `[MemoryPackable]` struct，服务器与客户端共享同一份定义（`Gbar.Net.Protocol`）。编码解码 = 首字节 + MemoryPack + switch 分发，几十行胶水代码。

### 包清单

| PacketType | 方向 | 通道 | 包体 | 说明 |
|-----------|------|------|------|------|
| `Hello` | C→S | ReliableOrdered | `ProtocolVersion` | 版本协商，不符直接断开 |
| `Welcome` | S→C | ReliableOrdered | `ClientId, TickRate, ServerTick, PlayerNetId` | 分配身份；`PlayerNetId` 为玩家本体实体 |
| `Keyframe` | S→C | ReliableOrdered | `Tick, Entities: EntityState[]` | 全量快照：加入对齐 / 纠偏 / 回退 |
| `Delta` | S→C | Sequenced | `BaseTick, CurrTick, Spawned[], Updated[], Despawned[]` | 常态增量；`BaseTick` = 该连接上次 ACK 的 tick |
| `Ack` | C→S | Unreliable | `LastAppliedTick` | 回执，丢一两个无妨，可降频 |
| `Bye` | 双向 | ReliableOrdered | `Reason` | 优雅断开；崩溃靠 LiteNetLib 超时检测 |

```csharp
[MemoryPackable]
public partial struct EntityState
{
    public int NetId;            // 4 B
    public int SpaceNetId;       // 4 B  所属空间（GbarGame.md 坐标框架）
    public float PosX, PosY, PosZ;  // 12 B
    public float VelX, VelY, VelZ;  // 12 B
}
// 合计 32 B/实体（MemoryPack 头部按 ~2 B 摊销，按 34 B 估算）
```

### 连接时序

```
C ── Hello ──→ S    校验版本，创建玩家本体（PlayerTag + NetworkId）
C ←─ Welcome ─ S
C ←─ Keyframe ─S    帧边界采集的全量对齐
C ── Ack(T) ─→ S    基线建立
C ←─ Delta ─── S    常态循环（Delta 相对已 ACK 基线生成，丢包不累积错误）
C ── Ack ────→ S
C ── Bye ────→ S    按 PlayerTag 清理归属实体
```

### ACK 的作用

Delta 永远相对"客户端已 ACK 的 tick"生成：丢包只是多发数据，不会错位；客户端校验 `Delta.BaseTick == LastAppliedTick`，不符则丢弃等 Keyframe。不用 LiteNetLib 可靠通道承载 Delta，是为了避免队头阻塞——旧状态没有重传价值。

## 三、组件规范

| 组件 | 状态 | 职责 |
|------|------|------|
| `NetworkId` | 已存在 | 同步主键；无此组件的实体不参与同步 |
| `PlayerTag` | 已存在 | 玩家归属标记：断开批量清理、AOI 观察锚点 |
| `[SyncComponent]` | 待新增 | 标注参与同步的组件，替代硬编码列表 |

- 连接成功时服务器创建玩家本体实体（`PlayerTag + NetworkId`），连接表记 `ClientId → Entity`；
- 断开时一个 `[All<PlayerTag>]` query 批量 `Destroy`（走 CommandBuffer）。

## 四、数据获取：SnapshotSystem

独立 `SnapshotSystem : UpdateSystem`（`[SystemOrder(int.MaxValue)]` 最后执行），**主循环线程内** query 收集同步实体写入快照缓存；网络线程永不碰 World。

```csharp
curr = Collect(world);   // [Query][All<NetworkId, Position, Velocity>] 本 tick 收集
prev = lastPublished;    // 上一次发布，供 Diff
```

- 采集时机必须在组内 `Commands.Playback` **之后**，否则含未回放变更；
- NetId 分配/回收（`NetIdRegistry`）在同一系统内经 CommandBuffer 处理；
- 每 tick 将 `curr` 发布到 `ConcurrentQueue`，发送侧消费。

### Delta 生成（发送侧，可在网络线程）

```
delta = Diff(baseline[conn], currSnapshot)
  ├─ Spawned:   curr 有、baseline 无
  ├─ Updated:   都有但值不同（逐字段比较）
  └─ Despawned: baseline 有、curr 无
```

`baseline[conn]` 初始为空 → 首包自动退化为 Keyframe，逻辑统一。

## 五、项目结构与接线

```
Gbar.Core/      不动：纯模拟核心
Gbar.Net/       协议 DTO + NetServer/NetClient（LiteNetLib、MemoryPack）
Gbar.App/       服务器宿主：SnapshotSystem + Program.cs 接线
Gbar.Client/    控制台测试客户端
```

- `Program.cs`：启动 `NetServer` → `server.Start()`；关停先 `StopAsync()` 再关 `NetServer`；
- 主循环与网络线程之间只走队列（`ConcurrentQueue`），与现有 CommandBuffer 哲学一致；
- 客户端镜像：`NetId → 状态` 表，Keyframe 全量替换、Delta 局部应用，定期打印与服务器日志对拍。

## 六、实现清单

- [ ] `Gbar.Net`：PacketType 枚举 + 各包 struct + 编解码分发；NetServer / NetClient 封装
- [ ] `NetIdRegistry`：NetId 分配/回收，空闲池复用
- [ ] `SnapshotSystem`：收集 + 双缓冲缓存 + 发布队列
- [ ] 连接管理：连接表、玩家本体创建/清理、每连接基线与 ACK 状态、Keyframe 分组错峰
- [ ] `Program.cs` 接线与关停顺序
- [ ] `Gbar.Client`：状态机（等 Welcome → 等 Keyframe → InSync）+ 镜像 + Ack 上报
- [ ] 参数：KeyframeInterval（≈100 tick）、分组数 K、未 ACK 回退阈值 N

## 七、带宽估算（按 GbarGame.md 坐标框架）

前提：TickRate = 20 tick/s，`EntityState` = 34 B（含头部摊销）。规模：**1000 实体 + 1000 玩家**。

### 单包大小

| 包 | 组成 | 大小 |
|---|---|---|
| Keyframe（全量 1000 实体） | 1000 × 34 B | **~34 KB**（需分片，走 ReliableOrdered） |
| Delta 最坏（全变） | 1000 × 34 B | ~34 KB/tick |
| Delta 典型（10% 实体变化） | 100 × 34 B | ~3.4 KB/tick |
| Ack | 4 B + 包头 | <50 B |

### 无 AOI（全量广播，不可行，仅作基准）

每个连接收全部 1000 实体：

| 变化率 | 单连接下行 | 1000 玩家服务器总下行 |
|---|---|---|
| 100%（最坏） | 34 KB × 20 = **680 KB/s** | **680 MB/s ≈ 5.4 Gbps** ❌ |
| 10%（典型） | 3.4 KB × 20 = **68 KB/s** | **68 MB/s ≈ 544 Mbps** ❌ |
| 1%（大部分静止） | **6.8 KB/s** | **54 Mbps**（仍含大量无效数据） |

结论：**1000 玩家规模下全量广播不可行，AOI 是硬需求。**

### 有 AOI（每玩家可见集 ≈ 100 实体，空间分格过滤后）

| 变化率 | 单连接下行 | 1000 玩家总下行 |
|---|---|---|
| 100% | 100 × 34 B × 20 = **68 KB/s** | **68 MB/s ≈ 544 Mbps** |
| 10%（典型） | **6.8 KB/s** | **54 Mbps** ✔ |
| 1% | **0.7 KB/s** | **5.4 Mbps** ✔ |

### 关键帧摊销

KeyframeInterval = 100 tick（5 s），错峰 K = 20 组：
- 每 tick 只有 1/20 连接收 Keyframe：50 连接 × 34 KB / tick = **1.7 MB/tick = 34 MB/s**
- 与 Delta 错峰策略：Keyframe 自身已是最新状态，收 K 的 tick 跳过该连接的 Delta，净增量可忽略。

### 上行（1000 玩家）

- Ack：50 B × 20 = 1 KB/s/连接 → 总计 **1 MB/s**
- 输入上行（预留）：~100 B × 20 = 2 KB/s/连接 → 总计 **2 MB/s**

### 汇总（典型工况：AOI 100 实体、10% 变化率）

| 方向 | 带宽 |
|---|---|
| 服务器下行 | **~55–90 Mbps**（Delta 54 + Keyframe 摊销 34 峰值） |
| 服务器上行 | ~3 Mbps |
| 单玩家下行 | ~7–40 KB/s |
| 单玩家上行 | ~3 KB/s |

**结论：AOI + Delta 后 1000 玩家规模在百兆带宽内可行；无 AOI 直接翻 10 倍不可行。AOI 实现（七-扩展第一条）应提前到 MVP。**

## 八、后续扩展（先不实现）

- AOI 兴趣区域：网格空间索引 + 每连接可见集过滤，Enter/Leave 复用 Spawned/Despawned
- 大包分片：LZ4 压缩 + ~1100 字节切片（走 ReliableOrdered），客户端重组
- 输入上行与客户端预测回滚（协议预留，不实现）
