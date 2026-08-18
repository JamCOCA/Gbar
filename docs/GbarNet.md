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
| `Hello` / `Welcome` | 握手 | 版本协商、分配 ClientId 与玩家本体 NetId |
| `Keyframe` | S→C | 全量快照（ReliableOrdered，需分片） |
| `Delta` | S→C | 常态增量（Sequenced） |
| `Ack` | C→S | 回执（Unreliable，可降频） |
| `Bye` | 双向 | 优雅断开 |

```csharp
[MemoryPackable]
public partial struct EntityState
{
    public int NetId;               // 4 B
    public int SpaceNetId;          // 4 B  所属空间（GbarMotion.md 坐标框架）
    public float PosX, PosY, PosZ;  // 12 B
    public float VelX, VelY, VelZ;  // 12 B
}
// 合计 32 B/实体（MemoryPack 头部按 ~2 B 摊销，按 34 B 估算）
```

连接时序：

```
C ── Hello ──→ S    校验版本，创建玩家本体（PlayerTag + NetworkId）
C ←─ Welcome ─ S
C ←─ Keyframe ─S    帧边界采集的全量对齐
C ── Ack(T) ─→ S    基线建立
C ←─ Delta ─── S    常态循环（Delta 相对已 ACK 基线生成，丢包不累积错误）
C ── Ack ────→ S
C ── Bye ────→ S    按 PlayerTag 清理归属实体
```

连接成功时服务器创建玩家本体（`PlayerTag + NetworkId`），断开时按 `PlayerTag` 批量清理。

## 数据流

- `SnapshotSystem`（每帧最后执行）：主循环线程内在 `Commands.Playback` **之后**收集同步实体，发布到 `ConcurrentQueue`；网络线程永不碰 World
- 发送侧（网络线程）：`Diff(该连接基线, 最新快照)` 生成 Delta；基线初始为空时首包自动退化为 Keyframe，逻辑统一
- 客户端镜像：`NetId → 状态` 表，Keyframe 全量替换、Delta 局部应用
- NetId 由 `NetIdRegistry` 分配回收；参与同步的组件后续用 `[SyncComponent]` 标注

## 带宽结论（1000 实体 + 1000 玩家，20 tick/s）

- 无 AOI 全量广播：服务器下行 5.4 Gbps 级，**不可行，AOI 是硬需求**
- 有 AOI（每玩家可见 ~100 实体、10% 变化率）：服务器下行 ~55–90 Mbps，单玩家下行 ~7 KB/s，**可行**

AOI 按 [GbarMotion.md](GbarMotion.md) 的分域索引实现，应提前到 MVP。

## 后续扩展（先不实现）

- AOI 可见集过滤：Enter/Leave 复用 Spawned/Despawned
- 大包 LZ4 压缩 + 分片重组
- 输入上行与客户端预测回滚（协议预留）
