# Gbar 系统清单

定步长管线（`FixedSystem`，20 tick/s）全量系统一览。坐标/物理/运动的原理见 [GbarMotion.md](GbarMotion.md)；网络协议见 [GbarNet.md](GbarNet.md)。

约定：

- Order 间隔 10 为插入位（如 `CollisionSystem` 可插 35）
- 所有查询用 Arch 源码生成器（`[Query(Parallel = true)]` + `[All<...>]`），查询内只做纯计算
- 结构变更（Create/Destroy/Add/Remove）一律写 `Commands`（CommandBufferHub），组更新结束后统一回放
- 标记 ⏳ 的组件尚未实现，实现对应系统前先补组件

## 总览

| Order | 系统 | 职责一句话 | 状态 |
|---|---|---|---|
| 10 | `OrbitalSystem` | 推进星球公转自转 | 待实现 |
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
| 120 | `NetSyncSystem` | 快照/增量包生成 | 待实现 |

---

## 10 OrbitalSystem

**职责**：按 `Orbit` 的轨道根数解析推进行星公转位置（`M = 2π·t/T + InitialPhase`，解开普勒方程，double 算转回 long），并推进所有天体的自转角（`Celestial.Rotation += AngularVelocity`）。内部计数降频（如每 20 tick 实际推进一次），但必须排在最前——引力和 SOI 切换都依赖行星当前位置。

**查询**：

```csharp
[Query(Parallel = true)]
[All<Celestial, Orbit, GlobalPosition>]   // ⏳ Celestial、Orbit 待建（GbarMotion.md 已定字段）
private static void Orbit([Data] in float delta, ref Celestial body, in Orbit orbit, ref GlobalPosition pos);

[Query(Parallel = true)]
[All<Celestial, GlobalPosition>]
[None<Orbit>]                                 // 恒星等不挂轨道的天体：只推进自转
private static void Spin([Data] in float delta, ref Celestial body);
```

**备注**：天体不挂 `Mass`/`Impulse`，不走力积分；位置由轨道参数直接解析。

## 20 GravitySystem

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

**备注**：`μ` 从引力源的 `Celestial` 只读获取（`World.Get<Celestial>(parent.Parent)`，并行只读安全）；着地实体无 `Mass`，天然不匹配。

## 30 ThrustSystem

**职责**：读推进组件的推力向量与朝向，每 tick 入池 `F/R`，同时扣燃料；燃料耗尽或玩法限速到达时停止输出。

**查询**：

```csharp
[Query(Parallel = true)]
[All<Thruster, Mass, Impulse>]   // ⏳ Thruster 待设计（推力、朝向、燃料关联）
private static void Thrust(ref Thruster thruster, ref Impulse impulse, in Mass mass);
```

**备注**：限速等玩法规则在本系统实现，物理管线保持纯粹。

## 40 DragSystem

**职责**：大气层内实体，按高度查密度，阻力冲量 `-k·ρ·v·|v|/R` 入池。

**查询**：

```csharp
[Query(Parallel = true)]
[All<LocalPosition, LocalVelocity, Mass, Impulse>]   // 仅局部域有大气
private static void Drag(ref Impulse impulse, in LocalPosition pos, in LocalVelocity vel, in Mass mass);
```

**备注**：高度 = `|LocalPosition| − SurfaceRadius`；高于大气顶直接跳过。⏳ 大气参数（密度剖面）需扩展 `Celestial` 或独立组件。

## 50 ImpulseIntegrationSystem

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

**备注**：实现前先把 `Force` 组件重命名为 `Impulse`（语义已按冲量定稿）。

## 60 MoveSystem（已实现）

**职责**：唯一移动点。两域各自纯整数加法 `pos += v`，速度量纲 = 定点单位/tick。

**查询**（`Systems/MoveSystem.cs`）：

```csharp
[Query(Parallel = true)]
[All<GlobalPosition, GlobalVelocity>]
private static void MoveGlobal(ref GlobalPosition position, in GlobalVelocity velocity);

[Query(Parallel = true)]
[All<LocalPosition, LocalVelocity>]
private static void MoveLocal(ref LocalPosition position, in LocalVelocity velocity);
```

## 70 LocomotionSystem

**职责**：着地实体的球面约束移动：位移 → 绕星心旋转（θ = s/r，Rodrigues，double 算），再径向缩放贴合地形高度。直接改写 `LocalPosition`，不经速度积分。

**查询**：

```csharp
[Query(Parallel = true)]
[All<LocalPosition, GroundedTag, MoveIntent>]   // ⏳ GroundedTag、MoveIntent（移动意图：方向+速度）待建
private static void Walk(ref LocalPosition pos, in MoveIntent intent);
```

**备注**：着地实体不挂 `LocalVelocity`，与本管线其他查询天然不重叠；法线 = `pos` 归一化，射线只查地形起伏。

## 80 GroundTransitionSystem

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

## 90 SoiTransferSystem

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

**备注**：转换所需的行星位置/速度/`Celestial` 只读获取；排在移动之后，用本 tick 定稿位置判定。

## 100 SpaceAoiSystem

**职责**：全局域 3D cell 空间哈希重挂（cell = 100 万定点单位），跨格实体移动索引项。

**查询**：

```csharp
[Query(Parallel = true)]
[All<GlobalPosition, NetworkId>]               // 需要同步的实体才进 AOI
private static void Reindex(Entity entity, in GlobalPosition pos);
```

**备注**：索引键由 `SpaceGrid.CellOf` 现算不存储；索引结构的所有权在本系统，重挂冲突在回放/串行段处理。

## 110 LocalAoiSystem

**职责**：局部域两层索引重挂：地表层（< ~20 km）球面投影四叉树 region（~10 km/格），轨道层 3D cell 哈希（~500 km/格），分层带迟滞。

**查询**：

```csharp
[Query(Parallel = true)]
[All<LocalPosition, ParentRef, NetworkId>]
private static void Reindex(Entity entity, in LocalPosition pos, in ParentRef parent);
```

**备注**：按 `ParentRef` 分空间建索引；高度 = `|pos| − SurfaceRadius` 决定归属层。

## 120 NetSyncSystem

**职责**：读最终状态，按 GbarNet.md 生成 Keyframe/Delta：以各连接已 ACK 的 tick 为基线产出差量，收 `AckPacket` 推进基线。

**查询**：

```csharp
[Query]
[All<NetworkId, GlobalPosition>]               // 全局域实体状态
private static void CollectGlobal(in NetworkId id, in GlobalPosition pos /* , in GlobalVelocity */);

[Query]
[All<NetworkId, ParentRef, LocalPosition>]     // 局部域实体状态（含所属空间）
private static void CollectLocal(in NetworkId id, in ParentRef parent, in LocalPosition pos);
```

**备注**：着地实体无速度组件，同步速度用相邻 tick 位置差分现算；`EntityState` 协议结构与 ECS 组件解耦（Packets.cs），本系统负责转换。串行执行即可（收集 + 写包不是重计算，并行收益低）。

---

## 变步长组（UpdateSystem，每帧一次）

当前为空。规划：网络收包/连接管理（与 tick 解耦的 IO 侧逻辑）放这里，实现网络层时再定。
