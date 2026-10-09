# Gbar.Physics 基础概念（白话版）

本文用大白话解释 `PhysicsWorld` 构造函数里的每一行参数在干什么，方便不熟悉 BepuPhysics v2 的读者理解。

相关文档：[GbarProject.md](GbarProject.md)（项目架构）、[GbarSystems.md](GbarSystems.md)（系统清单）。

---

## 构造长什么样

```csharp
Simulation = Simulation.Create(
    _pool,                                    // 内存池
    new NarrowPhaseCallbacks(...),            // 碰撞规则
    new PoseIntegratorCallbacks(gravity),     // 重力
    new SolveDescription(8, 1));              // 求解器配置
```

这四样东西，Bepu **一个都不能少**，少了就编译不过。下面逐个说清楚"它管什么"。

---

## 1. `PoseIntegratorCallbacks(gravity)` —— 重力，管"物体下不下落"

**一句话：它决定"这个世界里物体受到什么力"。**

Bepu 本身**不知道什么叫重力**。它只提供"我该怎么把速度往前推一步"的框架，至于"推多少、往哪推"，由这个回调说了算。

这个类干的事极简单：

```
每个物理步：所有刚体的速度 += 重力 × 时间步长
```

- `gravity` 是个方向向量，比如 `(0, -10, 0)` 表示"向下、每秒加速 10 米"。
- 没有它 → 你扔一个箱子，箱子就**悬浮在空中不动**（因为没有力让它下落）。
- 有了它 → 箱子落地、抛物线、自由落体，全都对了。

**类比**：它是这个世界的"地心引力开关"。

以后想做特殊效果，都改这里：
- 某区域减速 / 悬浮 → 在这里按位置改重力
- 加风力、阻尼 → 在这里往速度上额外加力

---

## 2. `SolveDescription(8, 1)` —— 求解器，管"碰撞解得准不准、稳不稳"

**一句话：它决定"两个物体撞在一起后，怎么把它们正确分开"。**

物体接触、堆叠时，引擎要反复计算"该用多大的力把互相穿透的物体推回去"。这一步叫**求解（solve）**，`SolveDescription` 就是调它的两个旋钮：

```csharp
new SolveDescription(8, 1)
//                      │  └─ 子步数
//                      └──── 迭代次数
```

### 迭代次数 = 8

- 每次碰撞求解会**重复算 8 遍**，一遍比一遍更接近"正确结果"。
- 越多 → 越准、越稳（物体堆叠不易抖动、不易穿透），但**更费 CPU**。
- 8 是 Bepu 的常用默认值，对绝大多数场景够用。

**类比**：像用橡皮擦反复擦一张草图，擦 8 次基本就干净了。

### 子步数 = 1

- 把"一个物理步"再切成几小步来算。1 表示**不切，一步算完**。
- 越多 → 高速物体、堆叠物体更稳定，但同样更费 CPU。
- 服务器是 20 tick/s，每 tick 已经固定，切子步意义不大，所以用 1。

**类比**：1 就是"一口吞"，切多了就是"细嚼慢咽"——更稳但更慢。

---

## 3. 顺带说清另外两个（之前已解释过，这里一句话带过）

| 参数 | 一句话作用 |
|---|---|
| `_pool`（BufferPool） | Bepu 专用的内存池，所有物理数据从这申请，避免频繁 GC |
| `NarrowPhaseCallbacks` | 碰撞规则：谁跟谁能撞、摩擦/弹性是多少 |

---

## 一张表总结

| 参数 | 通俗问法 | 答案 |
|---|---|---|
| `PoseIntegratorCallbacks(gravity)` | 物体受什么力？ | 重力，往下掉 |
| `SolveDescription(8, 1)` | 碰撞解得准不准、切几刀算？ | 迭代 8 次、不切子步 |
| `NarrowPhaseCallbacks` | 谁跟谁撞？多硬多弹？ | 动态体之间都撞、硬且不反弹 |
| `BufferPool` | 物理数据存哪？ | 专用内存池 |

**记住一句话**：`PhysicsWorld` 只是"壳"，负责把这些零件拼起来；真正的"重力、碰撞、求解精度"都是靠这些参数在决定。

---

## 整合进 Gbar.Core 与同步流程

`Gbar.Physics` 只依赖 BepuPhysics，不依赖 `Gbar.Core`；`Gbar.Core` 引用它并把物理引擎接进 ECS。

### 数据流（一个物理实体的完整闭环）

```
作者装配（EntityFactory.CreatePhysicsBody）
  → 实体带上 PhysicsBody(类型/质量/形状/句柄) + Position/Velocity/Rotation/AoiCell/NetworkId
        │
        ▼
PhysicsSystem（Order 70，每 tick）
  1. SyncBodies：Handle 为空 → 建 Bepu 刚体（初始位姿取自 Position/Rotation）
  2. SyncStatics：同上，建静态碰撞体
  3. Step(fixedDelta)：单线程推进 Bepu（重力 + 碰撞求解）
  4. WriteBack：把 Bepu 的位姿/线速度写回 Position/Rotation/Velocity
        │
        ▼
AoiSystem（100）重算 AoiCell → NetSyncSystem（120）照常下发快照/增量
        │
        ▼
客户端只看到 Position/Velocity/Rotation 变化，完全无感知是"物理"还是"运动学"驱动
```

### 关键设计点

| 点 | 说明 |
|---|---|
| 句柄封装 | `PhysicsBodyHandle`/`PhysicsStaticHandle` 是不透明 `int` 包装，`Gbar.Core` 无需直接引用 BepuPhysics 类型 |
| 惰性建体/删体 | 不监听 Destroy，而是每 tick 用"上一 tick 存活集 vs 当前查询结果集" diff，消失者回收 Bepu 体 |
| 双驱动隔离 | `MovementSystem` 加 `[None<PhysicsBody>]`，物理体只由 Bepu 移动，运动学体只由积分移动 |
| 精度 | Bepu 是 float32；`Position` 是 double（大世界）。当前直接 float→double 加宽，远距精度见浮动原点计划 |
| 地面 | `PhysicsSystem.Initialize` 建一个静态大盒作世界地面（顶面 y=0），后续可换成静态 ECS 实体承载地形 |

