# Gbar 系统清单

系统分两组：**变步长组**（`UpdateSystem`，每帧一次，IO/连接管理）与**定步长管线**（`FixedSystem`，20 tick/s，确定性模拟）。

- 网络协议、收包/同步时序见 [GbarNet.md](GbarNet.md)，线格式细节见 `Gbar.Net/Protocol/README.md`
- 项目结构、主循环与组件约定见 [GbarProject.md](GbarProject.md)

约定：

- Order 间隔 10 为插入位（如 `CollisionSystem` 可插 55）
- 定步长纯计算查询用 Arch 源码生成器（`[Query(Parallel = true)]` + `[All<...>]`），查询内只做纯计算
- 结构变更（Create/Destroy/Add/Remove）一律写 `Commands`（CommandBufferHub），组更新结束后统一回放
- 变步长系统在主循环线程串行执行，可直接写 `World`（不在并行查询内，无需 CommandBuffer）
- 当前为**单一扁平坐标域**：`Position`（double，米）/ `Velocity`（float，米每秒），无引力/质量/冲量/双域切换，移动为运动学积分

## 总览

### 变步长组（UpdateSystem，每帧一次）

| Order | 系统 | 职责一句话 | 状态 |
|---|---|---|---|
| 0 | `NetServerSystem` | 收包/握手/玩家生命周期 | **已实现** |

### 定步长管线（FixedSystem，Order 50–120）

| Order | 系统 | 职责一句话 | 状态 |
|---|---|---|---|
| 50 | `MoveInputSystem` | 玩家输入意图 → 速度（奔跑/蹲伏倍率） | **已实现** |
| 60 | `MovementSystem` | 运动学积分，唯一位置写入点 | **已实现** |
| 70 | `PhysicsSystem` | Bepu 物理步进 + 位姿/速度回写 | **已实现** |
| 100 | `AoiSystem` | 按坐标写 AOI cell 标签 | **已实现** |
| 120 | `NetSyncSystem` | 快照/增量同步（按 AOI 可见集剪枝） | **已实现** |

---

## 定步长系统

### 50 MoveInputSystem（已实现）

**职责**：把玩家实体的 `MoveInput` 意图换算为 `Velocity`，并应用奔跑/蹲伏速度倍率。跑在 `MovementSystem`（60）之前，保证本 tick 积分用到最新速度。`Systems/MoveInputSystem.cs:15`。

**实现**：

```csharp
[SystemOrder(50)]
public sealed partial class MoveInputSystem : FixedSystem
{
    [Query(Parallel = true)]
    [All<PlayerTag, Velocity, MoveInput>]
    private static void ApplyMoveInput(ref Velocity velocity, in MoveInput input);
}
```

**关键点**：

- 基准行走速度 `WalkSpeed = 5 m/s`；`Running` × 1.8、`Crouch` × 0.4（`MoveFlags` 位标志）
- 输入方向已由 `NetServerSystem` 归一化（模长 ≤ 1），这里直接缩放为速度：`velocity.X = input.X * speed`、`velocity.Z = input.Y * speed`
- `velocity.Y` 留给跳越/重力处理（当前无垂直运动，保持 0）；跳越属边沿触发且依赖地面状态，待重力/着地系统就绪后再接入
- 只查 `PlayerTag` 实体，怪物/静态物不受影响

### 60 MovementSystem（已实现）

**职责**：运动学积分，唯一的位置写入点。`位置 = 位置 + 速度 × Δt`（Δt 为固定步长）。`Systems/MovementSystem.cs:11`。

**实现**：

```csharp
[SystemOrder(60)]
public sealed partial class MovementSystem : FixedSystem
{
    [Query(Parallel = true)]
    [All<Position, Velocity>]
    private static void Move(ref Position position, in Velocity velocity);
}
```

**关键点**：

- 单一扁平坐标域，不再区分坐标域，也不涉及冲量/质量/引力
- `_delta` 为固定步长（1/TickRate），保证模拟确定性
- 并行安全：每个实体只写自身的 `Position`

### 70 PhysicsSystem（已实现）

**职责**：把带 `PhysicsBody`/`PhysicsStatic` 的实体挂接到 Bepu 刚体，单线程推进物理模拟，再把 Bepu 的位姿/线速度回写到 `Position`/`Rotation`/`Velocity`。跑在 `MovementSystem`（60）之后、`AoiSystem`（100）之前。`Systems/PhysicsSystem.cs:16`。

**实现**：

```csharp
[SystemOrder(70)]
public sealed class PhysicsSystem : FixedSystem
{
    public override void Initialize();                 // 创建世界地面（静态大盒，顶面 y=0）
    public override void Update(in float delta)
    {
        SyncBodies(physics);      // 新增实体建体、消失实体删体（惰性 + diff）
        SyncStatics(physics);     // 静态碰撞体同理
        physics.Step(delta);      // 单线程推进 Bepu
        WriteBackBodies(physics); // Bepu → Position/Rotation/Velocity
    }
}
```

**关键点**：

- **惰性建体**：`PhysicsBody.Handle` 为 `Null`（-1）时按 `Shape`/`Mass`/`Type` 建体，初始位姿取自 `Position`/`Rotation`（double 位姿 → float 传给 Bepu）
- **删除回收**：内部 `_trackedBodies`/`_trackedStatics` 记录上一 tick 存活句柄，与当前查询结果集 diff，消失者调用 `RemoveBody`/`RemoveStatic` 回收（不直接监听 Destroy）
- **回写**：`GetPosition`/`GetOrientation`/`GetLinearVelocity` 读 Bepu → 写回同步组件（`Position` 做 float→double 加宽）；全零 `Rotation` 视为恒等朝向
- **线程/确定性**：Bepu 单线程内联推进（`ThreadDispatcher(0)`），dt 恒用固定步长
- `MovementSystem` 已加 `[None<PhysicsBody>]`，物理体不再被运动学积分二次位移

### 100 AoiSystem（已实现）

**职责**：把带 `Position` 的实体按当前坐标写入其 `AoiCell` 组件（3D 网格 cell）。`Systems/AoiSystem.cs:13`。

**实现**：

```csharp
[SystemOrder(100)]
public sealed partial class AoiSystem : FixedSystem
{
    public const double CellSize = 100.0;   // 网格边长（米）

    [Query(Parallel = true)]
    [All<Position, AoiCell>]
    private static void Tag(ref AoiCell cell, in Position position);
}
```

**关键点**：

- cell 坐标 = `floor(position / CellSize)`，每 tick 现算，不维护全局索引
- 并行安全：每个实体只写自身的 `AoiCell`
- 可见集过滤由 `NetSyncSystem` 读取 `AoiCell` 完成（见下）

### 120 NetSyncSystem（已实现）

**职责**：每 tick 末按 AOI 可见集向每个连接发送状态。对未对齐的连接（尚未收到 Ack）每 tick 重发 Keyframe，对已对齐的连接按相对基线 diff 发 Delta。`Systems/NetSyncSystem.cs:23`。

**实现**：

```csharp
[SystemOrder(120)]
public sealed class NetSyncSystem : FixedSystem
{
    private static readonly QueryDescription _query =
        new QueryDescription().WithAll<NetworkId, Position, AoiCell>();

    public override void Update(in float delta)
    {
        Collect();                 // 收集所有同步实体（含组件列表）
        foreach (var session in registry.Sessions.Values)
        {
            if (session.PlayerEntity == Entity.Null) continue;
            BuildVisible(session); // 按玩家 AoiCell 过滤可见集
            if (!session.Synced || ShouldSendKeyframe(session, tick))
                SendKeyframe(net, session, tick);
            else
                SendDelta(net, session, tick);
        }
    }
}
```

**关键点**：

- 可见半径 `VisibilityRadiusCells = 10`（格）：只同步与玩家同一 cell 或相邻 cell 的实体
- 组件采集由源生成器产出的 `SyncReader` 完成，本系统不感知具体组件
- 组件级增量：`SendDelta` 用 `DiffComponents` 按组件 ID 比对（新增或变化进 `Updated`）；`Spawned`/`Despawned` 由可见集与 `LastSent` 的差集得出
- 周期纠偏：每 `KeyframeIntervalTicks`（默认 10 秒）重发一次 Keyframe，各连接按 `(tick + ClientId) % interval` 错峰
- 对齐状态（`Synced`/`BaselineTick`）只由客户端 Ack 驱动（`NetServerSystem.OnAck`），Keyframe 发送本身不置位

---

## 变步长组（UpdateSystem，每帧一次）

### 0 NetServerSystem（已实现）

**职责**：**收包系统**。每帧从 `NetServer.Events` 队列取出连接/数据/断开事件，在主循环线程完成握手（Hello → Welcome）、认证、玩家实体创建/销毁、Ack 基线更新、移动输入落组件。`Systems/NetServerSystem.cs:19`。

```csharp
[SystemOrder(0)]
public sealed class NetServerSystem : UpdateSystem
{
    public override void Initialize()   // 把本系统处理的 C→S 包注册到分发器
    {
        net.RegisterHandler<HelloPacket>(OnHello);
        net.RegisterHandler<AuthPacket>(OnAuth);
        net.RegisterHandler<AckPacket>(OnAck);
        net.RegisterHandler<ByePacket>(OnBye);
        net.RegisterHandler<MoveInputPacket>(OnMoveInput);
    }

    public override void Update(in float delta)
    {
        while (net.Events.TryDequeue(out var ev))
        {
            switch (ev.Kind)
            {
                case NetEventKind.Connected:    registry.Add(ev.ClientId); break;
                case NetEventKind.Packet:       PacketCodec.TryDispatch(ev.Data, ev.ClientId, net.Dispatcher); break;
                case NetEventKind.Disconnected: OnClientDisconnected(ev.ClientId); break;
            }
        }
    }
}
```

**处理细节**：

- `OnHello`：校验 `ProtocolVersion` → 标记 `Helloed`，等待认证
- `OnAuth`：注册/登录（`AuthService`）→ 成功则 `AllocNetId` + `EntityFactory.CreatePlayer(netId)` → 发 `Welcome`（ClientId/TickRate/ServerTick/PlayerNetId）
- `OnAck`：`session.BaselineTick = packet.LastAppliedTick`、`session.Synced = true`，供 `NetSyncSystem` 生成 Delta 基线
- `OnMoveInput`：服务器权威——归一化输入方向、钳制模长 ≤ 1（杜绝斜向加速），落为 `MoveInput` 组件（latest-wins），由 `MoveInputSystem` 消费
- `OnClientDisconnected`：`World.Destroy(玩家实体)` + `registry.Remove`

**备注**：直接写 `World` 安全——主循环线程独占，不在并行查询内。线程模型与收发时序见 [GbarNet.md](GbarNet.md)。

---

## 组件清单

实现系统前先确认组件就位：

| 组件 | 程序集 | 说明 |
|---|---|---|
| `Position` / `Velocity` / `Rotation` | `Gbar.Net.Protocol.Components` | 可同步组件（float，米 / 米每秒 / 弧度） |
| `Health` / `Cpc` | `Gbar.Net.Protocol.Components` | 可同步组件（血条 / 蓝条，int） |
| `NetworkId` / `PlayerTag` / `EntityTypeTag` | `Gbar.Core.Components.Identity` | 标识：NetId / 玩家标记 / 类型标签 |
| `MoveInput` | `Gbar.Core.Components.Input` | 移动意图（服务器本地，非同步；latest-wins） |
| `AoiCell` | `Gbar.Core.Components.Spatial` | AOI 网格 cell 坐标（int 格） |
| `PhysicsBody` | `Gbar.Core.Components.Physics` | 动态/运动学刚体（类型/质量/形状/锁旋转/Bepu 句柄，服务器本地） |
| `PhysicsStatic` | `Gbar.Core.Components.Physics` | 静态碰撞体（形状/句柄，服务器本地） |

> 可同步组件（`Gbar.Net/Protocol/Components`）带 `[SyncComponent]`，由 `Gbar.Sync.Generator` 源生成器自动产出 ID、`ComponentEntry` 联合、codec 与 `SyncReader` 读取分支；服务器本地组件（Identity/Input/Spatial）不同步。

## 实体装配（`EntityFactory`）

| 工厂方法 | 组件 | 说明 |
|---|---|---|
| `CreatePlayer(netId)` | NetworkId + PlayerTag + EntityTypeTag + Position + Velocity + Rotation + MoveInput + Health + Cpc + AoiCell | 玩家本体，NetId 由会话分配传入 |
| `CreateSurfaceWanderer(pos, vel)` | NetworkId + EntityTypeTag + Position + Velocity + Health + Cpc + AoiCell | 地表游荡敏化人（测试运动学移动与增量同步），自动分配 NetId |
| `CreateStaticEntity(type, pos)` | NetworkId + EntityTypeTag + Position + AoiCell | 静态实体（资源点/建筑，无速度），自动分配 NetId |
| `CreatePhysicsBody(type, pos, shape, mass)` | NetworkId + EntityTypeTag + Position + Velocity + Rotation + PhysicsBody + AoiCell | 动态刚体（受重力/碰撞，位姿由 PhysicsSystem 回写），自动分配 NetId |
| `CreateStaticCollider(type, pos, shape)` | NetworkId + EntityTypeTag + Position + PhysicsStatic + AoiCell | 静态碰撞体（仅碰撞阻挡，不参与动力学），自动分配 NetId |
