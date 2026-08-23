# Gbar 系统清单

系统分两组：**定步长管线**（`FixedSystem`，20 tick/s，确定性模拟）与**变步长组**（`UpdateSystem`，每帧一次，IO/连接管理）。

- 定步长管线全量系统一览见下；坐标/物理/运动的原理见 [GbarMotion.md](GbarMotion.md)
- 网络协议、收包/同步时序见 [GbarNet.md](GbarNet.md)

约定：

- Order 间隔 10 为插入位（如 `CollisionSystem` 可插 35）
- 定步长纯计算查询用 Arch 源码生成器（`[Query(Parallel = true)]` + `[All<...>]`），查询内只做纯计算
- 结构变更（Create/Destroy/Add/Remove）一律写 `Commands`（CommandBufferHub），组更新结束后统一回放
- 变步长系统在主循环线程串行执行，可直接写 `World`（不在并行查询内，无需 CommandBuffer）
- 标记 ⏳ 的组件尚未实现，实现对应系统前先补组件

## 总览

### 变步长组（UpdateSystem，每帧一次）

| Order | 系统 | 职责一句话 | 状态 |
|---|---|---|---|
| 0 | `NetServerSystem` | 收包/握手/玩家生命周期 | **已实现** |


### 定步长管线（FixedSystem，Order 10–120）

| Order | 系统 | 职责一句话 | 状态 |
|---|---|---|---|
| 10 | `OrbitalSystem` | 推进星球公转自转 | **已实现** |
| 20 | `GravitySystem` | 引力冲量入池 | 待实现 |
| 30 | `ThrustSystem` | 推力冲量入池、扣燃料 | 待实现 |
| 40 | `DragSystem` | 大气阻力冲量入池 | 待实现 |
| 50 | `ImpulseIntegrationSystem` | 冲量兑换速度，残差留池 | 待实现 |
| 60 | `MoveSystem` | 两域 `pos += v` | **已实现** |
| 70 | `LocomotionSystem` | 着地实体球面位移 | 待实现 |
| 80 | `GroundTransitionSystem` | 着地/飞行状态切换 | 待实现 |
| 90 | `SoiTransferSystem` | 双域坐标转换 | 待实现 |
| 100 | `SpaceAoiSystem` | 全局域 cell 索引重挂 | 待实现 |
| 110 | `LocalAoiSystem` | 局部域分层索引重挂 | 待实现 |
| 120 | `NetSyncSystem` | 快照/增量包生成 | **已实现**（未接 AOI） |



---

## 定步长系统

### 10 OrbitalSystem（已实现）

**职责**：按 `Orbit` 的轨道根数**历元解析**推进行星公转位置（`M = 2π·t/T + InitialPhase`，牛顿迭代解开普勒方程，double 算转回 long），并历元解析推进所有天体的自转角（`Rotation = InitialRotation + AngularVelocity × tick`）。位置与自转都是 tick 的纯函数——不累积、零漂移，与客户端按同一公式求值逐 bit 一致（GbarNet.md 口径）。`Systems/OrbitalSystem.cs:33`。

**实现**：

```csharp
[SystemOrder(10)]
public sealed partial class OrbitalSystem : FixedSystem
{
    public override void Update(in float delta)
    {
        OrbitQuery(World);   // 行星/卫星：公转 + 自转
        SpinQuery(World);    // 恒星（None<Orbit>）：只自转
    }

    [Query(Parallel = true)]
    [All<Celestial, Orbit, GlobalPosition>]
    private static void Orbit(ref Celestial body, in Orbit orbit, ref GlobalPosition pos);

    [Query(Parallel = true)]
    [All<Celestial, GlobalPosition>]
    [None<Orbit>]
    private static void Spin(ref Celestial body);
}
```

**关键点**：

- **确定性纪律**：开普勒求解只用 +−×÷——牛顿固定 6 次迭代，三角函数走 `Maths/DetTrig.cs`（对称归约到 [0, π/4] + Taylor/Horner，误差 ~2e-15），不用 `Math.Sin/Cos`（.NET 不保证跨平台逐 bit 一致）；`Math.Sqrt` 是 IEEE 精确舍入运算，可用
- **父链递归**：绝对位置 = 父天体绝对位置 + 本轨道偏移，沿 `Orbit.Parent` 链递归解析（恒星→行星→卫星）；只读根天体（恒星）静止的 `GlobalPosition`，并行只读安全、无调度顺序依赖
- **历元自转角**：`Celestial` 新增 `InitialRotation`（tick=0 相位）；`NetServerSystem` 的 Galaxy 包改发该字段，客户端按同一公式解析
- **轨道面**：无倾角根数，轨道面取 XY 平面（Z = 0）
- 未做内部降频：位置是解析求值，成本随天体数线性增长（每体 ~6 次牛顿迭代），天体量小无需降频

**备注**：天体不挂 `Mass`/`Impulse`，不走力积分；位置由轨道参数直接解析。

### 20 GravitySystem

**职责**：向飞行实体的 `Impulse` 池加入本 tick 引力冲量 `J = μ·m/(d²·R)`，方向朝唯一引力源（局部域 = 行星心；全局域 = 母恒星）。double 算，`Fix.FromDouble` 转回。

**查询**：

```csharp
[Query(Parallel = true)]
[All<LocalPosition, ParentRef, Mass, Impulse>]   // 局部域：引力源 = Parent 行星
private static void GravityLocal(ref Impulse impulse, in LocalPosition pos, in ParentRef parent, in Mass mass);

[Query(Parallel = true)]
[All<GlobalPosition, Mass, Impulse>]             // 全局域：引力源 = 母恒星
private static void GravityGlobal(ref Impulse impulse, in GlobalPosition pos, in Mass mass);
```

**备注**：`μ` 从引力源的 `Celestial` 只读获取（`World.Get<Celestial>(parent.Parent)`，并行只读安全）；着地实体无 `Mass`，天然不匹配。⏳ `Impulse` 待由 `Force` 重命名而来（见 50 节）。

### 30 ThrustSystem

**职责**：读推进组件的推力向量与朝向，每 tick 入池 `F/R`，同时扣燃料；燃料耗尽或玩法限速到达时停止输出。

**查询**：

```csharp
[Query(Parallel = true)]
[All<Thruster, Mass, Impulse>]   // ⏳ Thruster 待设计（推力、朝向、燃料关联）
private static void Thrust(ref Thruster thruster, ref Impulse impulse, in Mass mass);
```

**备注**：限速等玩法规则在本系统实现，物理管线保持纯粹。

### 40 DragSystem

**职责**：大气层内实体，按高度查密度，阻力冲量 `-k·ρ·v·|v|/R` 入池。

**查询**：

```csharp
[Query(Parallel = true)]
[All<LocalPosition, LocalVelocity, Mass, Impulse>]   // 仅局部域有大气
private static void Drag(ref Impulse impulse, in LocalPosition pos, in LocalVelocity vel, in Mass mass);
```

**备注**：高度 = `|LocalPosition| − SurfaceRadius`；高于大气顶直接跳过。⏳ 大气参数（密度剖面）需扩展 `Celestial` 或独立组件。

### 50 ImpulseIntegrationSystem

**职责**：唯一积分点。`Δv = p/m`（`Δv_fix = p_fix×500/m_fix`，Int128，截断），兑换后残差留池不清零。

**查询**：

```csharp
[Query(Parallel = true)]
[All<Impulse, Mass, GlobalVelocity>]
private static void IntegrateGlobal(ref GlobalVelocity vel, ref Impulse impulse, in Mass mass);

[Query(Parallel = true)]
[All<Impulse, Mass, LocalVelocity>]
private static void IntegrateLocal(ref LocalVelocity vel, ref Impulse impulse, in Mass mass);
```

**备注**：实现前先把 `Physics/Force.cs` 的 `Force` 组件重命名为 `Impulse`（语义已按冲量定稿，见 GbarMotion.md）；`Mass` 已实现。

### 60 MoveSystem（已实现）

**职责**：唯一移动点。两域各自纯整数加法 `pos += v`，速度量纲 = 定点单位/tick。`Systems/MoveSystem.cs:16`。

**实现**：

```csharp
[SystemOrder(60)]
public sealed partial class MoveSystem : FixedSystem
{
    public override void Update(in float delta)
    {
        MoveGlobalQuery(World);   // 源码生成器生成入口，手动调用（并行）
        MoveLocalQuery(World);
        Log.Debug($"MoveSystem 全局域 {World.CountEntities(in _globalCountQuery)} 个实体，局部域 {World.CountEntities(in _localCountQuery)} 个实体");
    }

    [Query(Parallel = true)]
    [All<GlobalPosition, GlobalVelocity>]
    private static void MoveGlobal(ref GlobalPosition position, in GlobalVelocity velocity);

    [Query(Parallel = true)]
    [All<LocalPosition, LocalVelocity>]
    private static void MoveLocal(ref LocalPosition position, in LocalVelocity velocity);
}
```

### 70 LocomotionSystem

**职责**：着地实体的球面约束移动：位移 → 绕星心旋转（θ = s/r，Rodrigues，double 算），再径向缩放贴合地形高度。直接改写 `LocalPosition`，不经速度积分。

**查询**：

```csharp
[Query(Parallel = true)]
[All<LocalPosition, GroundedTag, MoveIntent>]   // ⏳ GroundedTag、MoveIntent（移动意图：方向+速度）待建
private static void Walk(ref LocalPosition pos, in MoveIntent intent);
```

**备注**：着地实体不挂 `LocalVelocity`，与本管线其他查询天然不重叠；法线 = `pos` 归一化，射线只查地形起伏。

### 80 GroundTransitionSystem

**职责**：着地 ↔ 飞行切换，迟滞防抖动。离地（跳跃/被炸飞）：摘 `GroundedTag`，挂 `Mass` + `LocalVelocity` + `Impulse`，初速度折冲量入池；着陆（径向命中且速度低于阈值）：反向切换并吸附地表。

**查询**：

```csharp
[Query(Parallel = true)]
[All<LocalPosition, GroundedTag>]              // 离地检测（跳跃意图/脚下悬空）
private static void CheckLiftOff(Entity entity, in LocalPosition pos);

[Query(Parallel = true)]
[All<LocalPosition, LocalVelocity, Mass>]      // 着陆检测（径向距离 + 速度阈值）
private static void CheckTouchDown(Entity entity, in LocalPosition pos, in LocalVelocity vel);
```

**备注**：全部结构变更写 `Commands`，回放时生效；着地判定的理想球面部分用 `|pos|` 径向比较（O(1)），射线只管地形与建筑。

### 90 SoiTransferSystem

**职责**：唯一坐标转换点。穿越 SOI 边界（迟滞：进 0.98 / 出 1.02）时互换组件集：`GlobalPosition/Velocity` ↔ `ParentRef + LocalPosition/Velocity`，含自转项 `ω×r`。只切容器本身，乘员 `ParentRef` 不动。

**查询**：

```csharp
[Query(Parallel = true)]
[All<GlobalPosition>]                          // 全局实体：检测进入某行星 SOI
private static void CheckEnter(Entity entity, in GlobalPosition pos);

[Query(Parallel = true)]
[All<LocalPosition, ParentRef>]                // 局部实体：检测离开本行星 SOI
private static void CheckExit(Entity entity, in LocalPosition pos, in ParentRef parent);
```

**备注**：转换所需的行星位置/速度/`Celestial` 只读获取；排在移动之后，用本 tick 定稿位置判定。`NetServerSystem` 目前把玩家出生在全局域原点，落地后交给本系统做域归属。

### 100 SpaceAoiSystem

**职责**：全局域 3D cell 空间哈希重挂（cell = 100 万定点单位），跨格实体移动索引项。

**查询**：

```csharp
[Query(Parallel = true)]
[All<GlobalPosition, NetworkId>]               // 需要同步的实体才进 AOI
private static void Reindex(Entity entity, in GlobalPosition pos);
```

**备注**：索引键由 `SpaceGrid.CellOf` 现算不存储；索引结构的所有权在本系统，重挂冲突在回放/串行段处理。

### 110 LocalAoiSystem

**职责**：局部域两层索引重挂：地表层（< ~20 km）球面投影四叉树 region（~10 km/格），轨道层 3D cell 哈希（~500 km/格），分层带迟滞。

**查询**：

```csharp
[Query(Parallel = true)]
[All<LocalPosition, ParentRef, NetworkId>]
private static void Reindex(Entity entity, in LocalPosition pos, in ParentRef parent);
```

**备注**：按 `ParentRef` 分空间建索引；高度 = `|pos| − SurfaceRadius` 决定归属层。

### 120 NetSyncSystem（已实现）

**职责**：定步长末读最终状态，对每个连接生成 Keyframe（全量）或 Delta（相对 `LastSent` 的 diff）。周期纠偏每 `KeyframeIntervalTicks` 重发一次 Keyframe，各连接按 `(tick + ClientId) % interval` 错峰。`Systems/NetSyncSystem.cs:18`。

**实现**（当前用 `QueryDescription` 直接收集，未走源码生成器）：

```csharp
[SystemOrder(120)]
public sealed class NetSyncSystem : FixedSystem
{
    private static readonly QueryDescription _globalQuery = new QueryDescription().WithAll<NetworkId, GlobalPosition>();
    private static readonly QueryDescription _localQuery  = new QueryDescription().WithAll<NetworkId, ParentRef, LocalPosition>();

    private readonly Dictionary<int, EntityState> _current = new();   // NetId → 状态

    public override void Update(in float delta)
    {
        Collect();                                  // 收集全局域 + 局部域 → _current
        foreach (var session in registry.Sessions.Values)
        {
            if (session.PlayerEntity == Entity.Null) continue;   // 跳过未握手完成
            if (ShouldSendKeyframe(session, tick)) SendKeyframe(...);   // 全量 + 重置 LastSent
            else SendDelta(...);                                   // 与 LastSent diff → Spawned/Updated/Despawned
        }
    }
}
```

**关键点**：

- 收集：全局域实体 `SpaceNetId = 0`；局部域实体 `SpaceNetId = 父行星 NetId`；速度缺失（着地）时 `TryGet` 取默认值
- `SendDelta` 用 `DiffEntity` 按 `EntityFields` 掩码逐字段比较，无变化则不发；`BaseTick = session.BaselineTick`
- 天体（`Celestial`）不进快照——位置由客户端按根数 + tick 自行解析（见 GbarNet.md）
- 串行执行即可：收集 + 写包非重计算，并行收益低

**待补**：未接 AOI 可见集，当前对每个连接发全量 diff（正确但未剪枝）；AOI 落地后按可见集过滤 Spawned/Despawned。

---

## 变步长组（UpdateSystem，每帧一次）

### 0 NetServerSystem（已实现）

**职责**：**收包系统**。每帧从 `NetServer.Events` 队列取出连接/数据/断开事件，在主循环线程完成握手（Hello → Welcome + Galaxy）、玩家实体创建/销毁、Ack 基线更新。`Systems/NetServerSystem.cs:18`。

```csharp
[SystemOrder(0)]
public sealed class NetServerSystem : UpdateSystem, IPacketHandler
{
    public override void Update(in float delta)
    {
        while (net.Events.TryDequeue(out var ev))
        {
            switch (ev.Kind)
            {
                case NetEventKind.Connected:    registry.Add(ev.ClientId); break;
                case NetEventKind.Packet:       _currentClientId = ev.ClientId;
                                                PacketCodec.TryDispatch(ev.Data, this)  // 分发到 IPacketHandler
                                                /* 非法封包 → net.Disconnect */ break;
                case NetEventKind.Disconnected: OnClientDisconnected(ev.ClientId); break;
            }
        }
    }

    // ---- IPacketHandler：服务器只处理 C→S 方向 ----
    void OnHello(HelloPacket p)   // 校验版本 → AllocNetId → World.Create(玩家) → 发 Welcome + Galaxy
    void OnAck(AckPacket p)       // session.BaselineTick = p.LastAppliedTick
    void OnBye(ByePacket p)       // net.Disconnect
    // OnWelcome/OnGalaxy/OnKeyframe/OnDelta：S→C 方向，服务器留空
}
```

**处理细节**：

- `OnHello`：校验 `ProtocolVersion` → `registry.AllocNetId()` → `CreatePlayerEntity` → 发 `Welcome`（ClientId/TickRate/ServerTick/PlayerNetId）+ `Galaxy`（`CollectCelestials` 收集天体根数构建星图表）
- `CreatePlayerEntity`：`PlayerTag + NetworkId + GlobalPosition + GlobalVelocity`，暂在全局域原点出生，域归属后续交 `SoiTransferSystem`
- `OnAck`：推进该连接的 `BaselineTick`，供 `NetSyncSystem` 生成 Delta 基线
- `OnClientDisconnected`：`World.Destroy(玩家实体)` + `registry.Remove`

**备注**：直接写 `World` 安全——主循环线程独占，不在并行查询内。线程模型与收发时序见 [GbarNet.md](GbarNet.md)。

---

## 实现规划

### 当前状态

| 层 | 状态 |
|---|---|
| 网络链路（传输 + 收包 + 同步） | ✅ 已通（NetServerSystem + NetSyncSystem） |
| 位置积分 | ✅（MoveSystem） |
| 冲量物理管线（引力/推力/阻力/积分） | ⏳ |
| 天体 on-rails（OrbitalSystem） | ✅ |
| 双域切换（SoiTransferSystem） | ⏳ |
| 着地体制（Locomotion/GroundTransition） | ⏳ |
| AOI 索引 | ⏳ |

### 前置组件补全

实现对应系统前先补组件（除 `Thruster`/`MoveIntent` 外均在 GbarMotion.md 定稿）：

| 组件 | 现状 | 说明 |
|---|---|---|
| `Impulse` | ⏳ 由 `Force` 重命名 | 物理冲量池，受力系统只写入 |
| `Thruster` | ⏳ 待设计 | 推力、朝向、燃料关联 |
| `GroundedTag` | ⏳ 待建 | 着地标记（空结构体） |
| `MoveIntent` | ⏳ 待建 | 移动意图：方向 + 速度 |
| `Celestial` / `Orbit` / `Mass` / `ParentRef` / 双域坐标 | ✅ 已实现 | — |

### 里程碑（按依赖顺序）

| 里程碑 | 系统（Order） | 前置依赖 | 产出 |
|---|---|---|---|
| **M0 网络地基**（✅ 已完成） | NetServerSystem(0) + NetSyncSystem(120) + MoveSystem(60) | 传输层、双域坐标、NetworkId/PlayerTag | 客户端可连接、握手、收 Keyframe/Delta、位置积分 |
| **M1 冲量物理管线** | 组件补全 → ImpulseIntegrationSystem(50) + GravitySystem(20) + ThrustSystem(30) + DragSystem(40) | `Impulse` 重命名、`Thruster`、大气参数 | 大气飞行/轨道涌现、残差守恒 |
| **M2 天体 on-rails 与域切换** | OrbitalSystem(10, ✅) + SoiTransferSystem(90) | `Celestial`/`Orbit`（已就绪） | 行星公转、SOI 穿越、双域完整 |
| **M3 着地体制** | LocomotionSystem(70) + GroundTransitionSystem(80) | `GroundedTag`、`MoveIntent` | 地表行走、起降切换 |
| **M4 AOI 与同步剪枝** | SpaceAoiSystem(100) + LocalAoiSystem(110) + NetSyncSystem 接可见集 | 双域坐标、NetworkId（已就绪） | 带宽可行（GbarNet.md 结论） |

### 排序依据与注意

- 受力（20–40）→ 积分（50）→ 移动（60–70）→ 状态切换（80–90，位置定稿后判定，结构变更走 CommandBuffer）→ 索引（100–110）→ 网络输出（120）
- **M1 与 M2 可并行推进**：M1 只依赖组件补全；M2 只依赖已就绪的 `Celestial`/`Orbit`。但 `SoiTransferSystem` 的验证需要 `OrbitalSystem` 先跑出行星位置
- `ImpulseIntegrationSystem` 必须与受力系统（引力/推力/阻力）**同批落地**——受力系统往池写、积分器消费，单独上线任何一边都是死代码
- **AOI 是硬需求**：无 AOI 全量广播 5.4 Gbps 级不可行，MVP 必须包含 M4（见 GbarNet.md 带宽结论）
- 待补的非管线项：NetId 回收复用（`ClientRegistry` 目前只增不回收）、输入上行包（协议已预留 `IPacketHandler` 分发骨架，加 `OnInput` 类即可）
