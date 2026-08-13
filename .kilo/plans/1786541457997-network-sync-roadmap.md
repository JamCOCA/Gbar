# Gbar 网络同步开发路线

## 目标

为现有 Arch ECS 游戏服务器（20 tick/s，`GameServer` 主循环）增加网络同步能力：**服务器权威状态同步**，客户端加入时通过一次全量快照完成对齐，之后每 tick 接收全量状态广播。

## 已确认决策

| 决策点 | 结论 |
|--------|------|
| 同步模型 | 状态同步（服务器权威）；玩家加入时全量快照对齐一次 |
| 传输协议 | UDP + LiteNetLib（可靠有序通道用于快照对齐， unreliable/sequenced 用于周期快照） |
| 第一阶段范围 | 纯状态广播；输入上行、客户端预测回滚**不做**（留作后续阶段） |
| 客户端形态 | C# 控制台测试客户端（新 exe 项目） |
| 序列化 | MemoryPack（源生成器，两端共享 DTO 定义） |
| 快照策略 | 每 tick 全量快照（现阶段 100 实体 Position/Velocity 量级完全可承受） |

## 项目结构（新增）

```
Gbar/
├── Gbar.Core/                          不动：纯模拟核心，保持无网络依赖
├── Gbar.Net/                           新增：共享网络层（服务器与客户端共用）
│   ├── Gbar.Net.csproj                 依赖：LiteNetLib、MemoryPack
│   ├── Protocol/
│   │   ├── Packets.cs                  [MemoryPackable] DTO：Hello/Welcome/FullSnapshot/Snapshot
│   │   └── PacketType.cs               包类型枚举（首字节包头）
│   ├── NetServer.cs                    LiteNetLib 服务器封装：监听、连接管理、广播
│   ├── NetClient.cs                    LiteNetLib 客户端封装：连接、收包回调
│   └── SnapshotBuilder.cs              World → Snapshot DTO（依赖 Arch，仅服务器侧使用）
├── Gbar.App/                           服务器宿主：挂接 NetServer 与快照系统
└── Gbar.Client/                        新增：C# 控制台测试客户端
    └── Program.cs                      连接服务器、收快照、打印实体状态（验证插值前先做原始渲染）
```

## 协议设计（v1）

所有包首字节为 `PacketType`，正文 MemoryPack 序列化。

| 包 | 方向 | 通道 | 内容 |
|----|------|------|------|
| `Hello` | C→S | ReliableOrdered | 协议版本号 |
| `Welcome` | S→C | ReliableOrdered | 分配的 `ClientId`、当前 `Tick`、`TickRate` |
| `FullSnapshot` | S→C | ReliableOrdered | `Tick` + 全部实体 `(NetId, Position, Velocity)`（加入对齐用） |
| `Snapshot` | S→C | Unreliable（或 Sequenced） | `Tick` + 全量实体状态（每 tick 广播） |
| `Pong/Bye` | 双向 | — | 断开与心跳（LiteNetLib 自带心跳，只做断开通知） |

**网络 ID**：服务器侧维护 `Entity.Id → NetId(int)` 映射表（`Dictionary` + 自增计数器），实体创建时分配、销毁时回收。快照内一律用 `NetId`，客户端按 `NetId` 建本地镜像表。

## 集成点（关键设计）

- **不侵入主循环**：`NetServer` 在自己的线程跑 LiteNetLib 轮询；与 ECS 主循环只通过以下两点交互：
  1. **快照采集**：新增一个 `UpdateSystem` 子类 `SnapshotSystem`（`[SystemOrder]` 排在最后），每 tick 在主循环线程内读 World 构建 `Snapshot` DTO，序列化后丢进并发队列，`NetServer` 线程取走广播。**禁止**在网络线程直接遍历 World。
  2. **加入对齐**：客户端连入事件入队 → 主循环线程在帧边界消费：`FullSnapshot` 必须在某个 tick 的 `Commands.Playback` 之后采集发送，保证客户端对齐到干净帧边界（用户要求的"进入时对齐"）。
- **实体变更**：当前实体仅启动时创建一次，`SnapshotSystem` 遍历即可；实体销毁（越界 `Destroy`）通过快照中消失的 `NetId` 让客户端移除本地镜像。

## 开发路线（按序执行）

### 阶段 1：协议与骨架
1. 新建 `Gbar.Net` 项目，引用 LiteNetLib + MemoryPack，加入 `Gbar.slnx`。
2. 定义 `PacketType` 与各 DTO（`[MemoryPackable]`），写序列化/反序列化往返自测。
3. 实现 `NetServer`：启动监听、连接/断开事件（线程安全队列外抛）、按通道广播字节包。
4. 实现 `NetClient`：连接、发 `Hello`、收包分发回调。

### 阶段 2：服务器接入
5. 实现 `SnapshotBuilder`：World → `(NetId, Position, Velocity)[]`，含 NetId 分配/回收表。
6. 新增 `SnapshotSystem : UpdateSystem`（Gbar.App 或 Gbar.Net 中，`[SystemOrder(int.MaxValue)]` 保证最后执行）：每 tick 构建快照、序列化、入发送队列。
7. `Program.cs` 接线：启动 `NetServer` → `server.Start()`；订阅连接事件，在帧边界向新客户端发 `Welcome` + `FullSnapshot`。
8. 关停顺序：`StopAsync()` 停主循环后再关 `NetServer`。

### 阶段 3：测试客户端
9. 新建 `Gbar.Client` 控制台项目：连接 → 收 `FullSnapshot` 建镜像 → 每 `Snapshot` 更新镜像 → 定时打印实体数与抽样坐标，验证与服务器日志一致。
10. 验证边界：运行中途新客户端连入能正确全量对齐；实体越界销毁后客户端镜像同步移除。

### 阶段 4：硬化（可延后，但列入路线）
11. 快照包过大时分片/降频；实体量增长后评估增量同步。
12. 丢包容忍：Snapshot 用 Sequenced 通道丢旧包。
13. 预留输入上行包结构（`InputPacket` 占位，不实现），为后续预测回滚留口。

## 风险与注意

- **线程安全**：World 只允许主循环线程访问；网络线程与主循环间一律走队列（`ConcurrentQueue` / `Channel`），与现有 CommandBuffer 哲学一致。
- **JobScheduler 约束不变**：网络代码不触碰 `Schedule`，不受影响。
- **MemoryPack 版本兼容**：两端同版本部署；`Hello` 带协议版本号便于将来拒绝旧客户端。
- **快照采集时机**：必须在组内 `Commands.Playback` 之后，否则客户端会拿到含待回放变更的脏状态。

## 验证

- `dotnet build` 全解决方案通过。
- 启动 `Gbar.App` + 两个 `Gbar.Client` 实例：两端实体数一致，抽样坐标误差为 0（原始快照无插值）。
- 中途连入第三个客户端：收到 `FullSnapshot` 后与其他客户端状态一致。
- 服务器日志确认 tick 速率不受网络线程影响（维持 ~20 tick/s）。

## 明确不做（本期）

- 玩家输入上行、客户端预测、服务器和解（reconciliation）
- 增量/差量快照、AOI（兴趣区域）裁剪
- 真游戏引擎客户端对接、跨语言协议
- 断线重连状态恢复
