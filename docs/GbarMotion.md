# Gbar 坐标、物理与运动

定稿方案总览：**双域扁平坐标 + 冲量池物理 + 三体制运动**。

- 实体任意时刻只处于一个坐标域，坐标只存一份，域切换集中在 SoiTransferSystem
- 所有改变速度的途径收拢为一个入口：`Impulse` 冲量池，`Velocity` 只由积分器写
- 局部域内按运动体制分流：着地走球面位移，飞行/轨道走物理管线

## 定点数

坐标、速度、质量、冲量用 `long` 定点存储，缩放 1:100（两位小数，厘米精度，范围 ±9.2×10¹⁴ m，星系级够用）。

- 加减、比较：直接整数运算，零误差、跨机器确定
- 乘除、浮点转换：走 `Gbar.Core.Maths.Fix` 工具类，防止手写漏 Scale
- 引力、球面旋转等复杂运算：临时转 `double` 算，结果转回 `long`

```csharp
public static class Fix
{
    public const long Scale = 100;

    public static long Mul(long a, long b) => (long)((Int128)a * b / Scale);
    public static long Div(long a, long b) => (long)((Int128)a * Scale / b);

    public static long FromDouble(double v) => (long)Math.Round(v * Scale);
    public static long FromInt(long v) => v * Scale;
    public static double ToDouble(long v) => v / (double)Scale;
}
```

### 量子

每个定点量的最小可表示刻度叫**量子**，低于量子的效应在存储结构里没有格子：

| 量 | 存储 | 缩放 | 量子 |
|---|---|---|---|
| 位置 | `long`，cm | 1:100 | 1 cm |
| 速度 | `long`，cm/tick | 1:100 | 0.01 cm/tick ≈ 0.002 m/s |
| 质量 | `long`，kg | 1:100 | 0.01 kg |
| 冲量 | `long`，N·s | 1:100 | 0.01 N·s |

亚量子量默认被整数截断丢弃；速度通道由冲量残差机制兜底（见后）。设计游戏数值的底线：关心的最小物理效应不能长期低于对应量子。

## 两个坐标域

**全局域**（所有 SOI 之外的深空）：

- 星球本身也是全局坐标实体，由轨道系统推进
- 移动 = 纯整数加法 `position += velocity`

```csharp
public struct GlobalPosition { public long X, Y, Z; }
public struct GlobalVelocity { public long X, Y, Z; }
```

**局部域**（一个星球的整个 SOI：地表 + 大气 + 轨道）：

- `ParentRef` 指向父空间实体（行星/飞船/空间站）
- 行星怎么动，成员零成本跟随——"相对静止"是真实的，网络/AOI 看到的是真静止

```csharp
public struct ParentRef { public Entity Parent; }   // 父空间实体

public struct LocalPosition { public long X, Y, Z; }
public struct LocalVelocity { public long X, Y, Z; }
```

两套组件**互斥**，切换时删一套加一套，不存在并存。局部域选 SOI 为边界的原因：SOI 内只受本行星引力（重力源唯一），若在全局系里，行星公转加速而成员感觉不到恒星引力会掉队——KSP 同样把 SOI 内一切放在行星系模拟。

## 域切换：SoiTransferSystem

唯一的坐标转换点，**切换时机 = 穿越 SOI 边界**（着陆/起飞都不是切换事件，起飞全程在局部域内，大气只是阻力区）。

- 进入 SOI：全局坐标减去行星坐标（及自转项）得到局部坐标，速度同理反向继承
- 离开 SOI：逆变换，`v_全局 = v_行星 + ω×r + v_局部`
- 边界加迟滞（进 0.98 / 出 1.02），防止边界抖动反复切换
- 切换只切容器本身：飞船过 SOI 只换飞船的组件，舱内乘员的 `ParentRef→飞船` 不动

```
进入 SOI：GlobalPosition → ParentRef{行星} + LocalPosition
          局部坐标 = 全局坐标 − 行星全局坐标 [− 自转旋转项]
          局部速度 = 全局速度 − 行星速度 − ω × r（自转线速度）
离开 SOI：逆变换一次，v_全局 = v_行星 + ω × r + v_局部
迟滞：   进入条件 d < SoiRadius × 0.98；离开条件 d > SoiRadius × 1.02
```

## 引力与天体

KSP 式引力圈：任一时刻重力源唯一，SOI 半径按 `r = a·(m/M)^(2/5)` 加载时算好缓存在 `CelestialBody` 里。

- 局部域飞行实体：引力朝行星心，`d` 直接是 `LocalPosition` 模长
- 全局域实体：受母恒星引力或惯性
- 天体自身不做力积分：星体间引力永不计算，公转由轨道根数解析求值（周期性、可预测、零漂移）
- 星体的"质量"以 μ（= G·M）形式存在：引力公式只需引力源的 μ 和受力方的 m；SOI 质量比 = `μ_行星/μ_恒星`
- 着地实体：不积分，见"着地体制"

天体身份与轨道拆成两个组件：

```csharp
/// 天体引力与自转身份：恒星和行星都挂。
public struct CelestialBody
{
    public double Mu;              // 引力参数 μ = G*M
    public double SurfaceRadius;   // 地表半径（米）
    public double SoiRadius;       // 引力圈半径（米），加载时算好缓存；恒星 = 覆盖整个星系
    public float Rotation;         // 当前自转角（弧度）
    public float AngularVelocity;  // 自转角速度（rad/tick）
}

/// 公转轨道根数：只有行星挂（恒星是轨道中心，不挂）。
public struct BodyOrbit
{
    public Entity CentralBody;     // 母恒星实体
    public double SemiMajorAxis;   // 半长轴 a（米）
    public double Eccentricity;    // 偏心率 e（圆轨道 = 0，退化为匀速圆周）
    public double OrbitalPeriod;   // 公转周期（秒）
    public double PhaseAtEpoch;    // 历元相位（初始平近点角，弧度）
}
```

- **恒星** = `GlobalPosition` + `CelestialBody`，位置固定（或星系质心），不挂 `BodyOrbit`
- **行星** = `GlobalPosition` + `CelestialBody` + `BodyOrbit`，由 OrbitalSystem 按 `M = 2π·t/T + PhaseAtEpoch` 解开普勒方程求位置（double 算转回 long）

## 冲量物理层

### 组件

```csharp
/// 质量，定点 kg（1:100）。没有此组件的实体不参与动量积分。
public struct Mass { public long Value; }

/// 未兑换的冲量池（定点 N·s，1:100），向量方向相对于实体当前坐标域。
/// 受力系统只做加法写入；积分后未凑够一个速度量子的部分留在池中。
public struct Impulse { public long X, long Y, long Z; }
```

`Impulse` 的语义是**冲量**（J = F·Δt，动量的变化量），不是力，也不是实体持有的动量：

- 持续力（引力/推力/阻力）：产生系统每 tick 加 `F / tickRate`
- 一次性事件（爆炸/碰撞）：直接加一次完整冲量 J，时机不敏感
- 池中残留：已交付但还没兑换成速度量子的冲量"零钱"

实体持有的动量由 `Mass × Velocity` 隐式表达，不在池里。无外力时池是空的，惯性滑行零成本——牛顿第一定律在这套结构里是免费的。

### 兑换公式

物理关系 `Δv = p / m`，定点展开（1 v 单位 = 1 cm/tick = 0.2 m/s，故 `v_fix = v_si × 500`）：

```
Δv_fix = p_fix × K / m_fix        K = Scale² / tickRate = 10000 / 20 = 500
```

推导要点：冲量与质量同缩放，比值 `p_fix/m_fix` 的 Scale 约掉；再乘速度换算系数 500。代码必须先乘后除（精度），中间运算走 Int128（`p_fix × 500` 溢出 long）。

积分器每 tick 对每个分量执行：

```csharp
var dv = (long)((Int128)impulse.X * K / mass.Value);   // 截断，能换几格换几格
vel.X += dv;                                            // 入账速度
impulse.X -= (long)((Int128)dv * mass.Value / K);      // 只扣已兑换部分，残差留池
```

兑换阈值：`p ≥ m / 500`（SI）才能换出 1 个量子。1000 kg 船 = 2 N·s。

### 冲量残差机制

速度只能整量子增减。若积分后清零池子，亚量子冲量每 tick 被截断且**不累积**：2 N 推力推 1000 kg 的船永远不动；1 AU 处太阳引力（≈0.006 m/s²）低于每 tick 量子（0.04 m/s²），深空无法入轨。

解法：积分后**不清零**，只扣已兑换的整量子部分，残差留池与后续冲量合并凑整。守恒律逐 bit 成立：

```
Σ 加入的冲量 = Σ (换出的速度 × 质量) + 池中残差
```

例：1000 kg 船 + 2 N 离子推力，每 tick 交付 0.1 N·s，攒 20 tick 凑够 2 N·s 兑换 1 格（+0.002 m/s），每秒整 1 格，加速度 0.002 m/s² 与解析解 `F/m` 完全一致。

要点：

- **截断而非四舍五入**：四舍五入会超发（池里 5 N·s 换出 6 N·s 的速度），残差正负振荡；截断保证残差与池同号、上界一个量子、只进不出直到凑整
- C# 整数除法向零截断，正负方向对称，反向力（减速/背向引力）的负残差与后续正向冲量自然对冲
- 推力停止后池中残灰 < 1 量子，永远兑换不出去，不再产生运动
- 多力叠加是整数加法，满足交换结合律——求和顺序无关，跨机器逐 bit 确定

### 引力的冲量化与质量约去

```
每 tick 入池： J = F / R = μ·m / (d²·R)     （GravitySystem，double 算 long 存）
积分时：      Δv = J / m = μ / (d²·R)        ← m 约掉
```

引力对所有质量产生相同 Δv（伽利略 equivalence 自动成立）；推力与质量无关、Δv ∝ 1/m——大船推不动、小艇一点就走，物理正确性由框架自动给出。方向按 `d` 的单位向量分解到三分量分别入池。亚量子引力（深空）由残差池累积还原，轨道闭合。

## 运动体制（局部域三分）

| 体制 | 例子 | 组件 | 谁负责移动 | 曲率来源 |
|---|---|---|---|---|
| 着地 | 行走、停机坪上的船 | `LocalPosition` + `GroundedTag` | LocomotionSystem，球面位移 | 几何约束（模型里无引力） |
| 大气飞行 | 飞机、直升机 | `LocalPosition/Velocity` + `Mass` + `Impulse` | 物理管线 | 引力 + 升力/推力合成 |
| 轨道 | 卫星、飞船 | 同上 | 物理管线 | 引力，轨道是涌现结果 |

轨道不需要球面处理：惯性沿切线飞出、引力每 tick 拉回，两者恰好抵消，路径弯成圆锥曲线（100 km 轨道每 tick 切线矢高 ≈ 0.0117 m，引力下拉 ≈ 0.0113 m）。半隐式欧拉（先引力后移动）长期跑轨道不螺旋发散。

### 着地体制：球面位移

着地实体**不挂 `LocalVelocity`**（否则 MoveSystem 直线积分把它沿切线甩出去），移动由 LocomotionSystem 直接改写 `LocalPosition`，与物理管线查询天然不重叠。

```csharp
// 每 tick，对每个着地实体：
var r      = Length(pos);                    // 当前离地心距离
var normal = pos / r;                        // 球面法线，免费
var s      = speed * dt;                     // 本 tick 弧长（米）
var theta  = s / r;                          // 绕星心旋转角（极小，步行 ≈ 3×10⁻⁸ rad）
var axis   = Normalize(Cross(normal, dir));  // 旋转轴 = 法线 × 移动方向

pos = RodriguesRotate(pos, axis, theta);     // double 算，Fix.FromDouble 转回 long
pos = pos * ((r + terrainDelta) / Length(pos)); // 径向缩放贴合地形高度
```

- 全程零直线积分，任意距离精确在球面上（对比切线推进：载具 30 m/s 跑 1 分钟矢高达 25 cm，且误差随距离平方增长）
- 法线 = 位置归一化，朝向维护：`up = normal`，`forward = dir` 在新切平面的投影
- 射线检测的职责只管地形起伏与建筑/甲板，不承担"防止飞离星球"

### 状态判定与切换

```
脚下是什么（射线 / 理想球面用 |pos| ≤ SurfaceRadius + 地形高度 + ε 径向比较，O(1)）：
命中星球表面  → 着地：球面位移
命中载具/建筑 → 挂载：ParentRef 指向该实体，进它的坐标系
都没命中      → 飞行：挂 Mass，进物理管线
```

```
着地 → 飞行（跳跃/被炸飞）：移除 GroundedTag；添加 Mass + LocalVelocity + Impulse，初速度折成冲量入池
飞行 → 着地（着陆）：命中 && 径向速度朝内 && 相对速度 < 阈值；
                     移除 Mass + Impulse + LocalVelocity；添加 GroundedTag，径向吸附地表
```

- 结构变更走 CommandBuffer，组更新结束后统一回放
- 切换加迟滞（着陆速度阈值、离地确认帧数），防落地抖动——与 SOI 边界同一手法
- 行星自转：建议局部坐标系随行星共转（地表在局部系静止），行走系统不追自转；实现 SoiTransferSystem 时拍板
- 网络同步：着地实体无 `LocalVelocity`，同步速度用相邻 tick 位置差分现算，不存储

## 嵌套规则

`ParentRef` 允许嵌套链（角色→飞船→行星，最长 ~4 跳），但只表达真实包含关系（人在船里、船停在站上），规则：

1. 舱内自成小世界：舱内判定只在船舱坐标系做，不与外部实体直接判定
2. 外部交互打在最外层容器上：炮弹命中的是飞船，乘客间接受影响
3. 需要舱内实体世界坐标时（雷达等），沿链展开一次，用完即弃

## AOI

分域两套索引，索引键全部由坐标现算，不存储：

- **全局域**：3D cell 空间哈希，cell ~100 万单位，实体稀疏，邻域 O(1)
- **局部域**：坐标一套、索引两层，按高度分层（迟滞防抖动）：
  - 地表层（高度 < ~20 km）：球面投影四叉树 region，~10 km 一格
  - 轨道层：3D cell 哈希，cell ~500 km

```csharp
public static class SpaceGrid
{
    public const long CellSize = 1_000_000_00;  // 100 万单位/格（定点）

    public static (int cx, int cy, int cz) CellOf(in GlobalPosition p)
        => ((int)(p.X / CellSize), (int)(p.Y / CellSize), (int)(p.Z / CellSize));
}
// 哈希键：Dictionary<(int, int, int), List<Entity>>，实体跨格由 AOI 系统重挂
```

兴趣集合 = 本层邻域 + 跨层投影查询（地表查头顶 R 范围轨道 cell，轨道船查星下点地表 region），R = 最大交互距离。超出 R 的高轨目标不进 AOI，走天体级低频通道，客户端只见光点。

## 系统管线（每 tick 固定顺序，间隔 10 预留插入位）

| Order | 系统 | 职责 | 结构变更 |
|---|---|---|---|
| 10 | `OrbitalSystem` | 推进星球公转自转（低频，内部计数降频），最先跑保证引力/切换用到最新行星位置 | — |
| 20 | `GravitySystem` | 引力：往池加 `μ·m/d²/tickRate`（double 算 long 存） | — |
| 30 | `ThrustSystem` | 推力：读推进组件，往池加 `F/tickRate`，扣燃料 | — |
| 40 | `DragSystem` | 阻力：大气内往池加 `-k·v·\|v\|/tickRate` | — |
| 50 | `ImpulseIntegrationSystem` | 唯一积分点：`Δv = p/m`，残差留池 | — |
| 60 | `MoveSystem` | 唯一移动点：两域各自 `pos += v`，纯整数加法（已实现） | — |
| 70 | `LocomotionSystem` | 着地实体球面位移 + 地形吸附（与 MoveSystem 查询不重叠） | — |
| 80 | `GroundTransitionSystem` | 着地/飞行切换（迟滞），位置已定稿后判定 | CommandBuffer |
| 90 | `SoiTransferSystem` | 域切换，唯一坐标转换点（迟滞），最后判定穿越 | CommandBuffer |
| 100 | `SpaceAoiSystem` | 全局域 cell 索引重挂 | CommandBuffer |
| 110 | `LocalAoiSystem` | 局部域分层 region/cell 索引重挂 | CommandBuffer |
| 120 | `NetSyncSystem` | 快照/增量生成（GbarNet.md），读最终状态 | — |

排序依据：受力（20–40）→ 积分（50）→ 移动（60–70）→ 状态切换（80–90，位置定稿后判定，结构变更全部走 CommandBuffer 统一回放）→ 索引（100–110）→ 网络输出（120）。

规则：

1. 受力系统只准加 `Impulse`，谁都不准碰 `Velocity`
2. 产生冲量的系统查询带 `[All<Impulse, Mass, ...Velocity>]`，避免往无积分能力的实体写入永不兑换的冲量
3. 没有 `Mass` 的实体（着地角色、舱内乘员）由玩法系统直接管理位置，物理实体与玩法实体靠组件有无自然分层
4. 天体挂 `CelestialBody`（引力/自转身份）+ `BodyOrbit`（公转根数，仅行星），走开普勒轨道，不进力积分管线
5. 碰撞/爆炸统一往池里加冲量，不直接改 `Velocity`

## 明确不做

- 旋转/力矩/惯性张量：飞船朝向是玩法层的事，需要姿态控制时再加
- 碰撞求解：碰撞检测是独立系统，本层只定义其输出形式（冲量入池）
- 浮点累加顺序问题：不存在——定点整数加法天然顺序无关

## 否决的备选

- **全局坐标 + 每 tick 跟随改写**："静止"在全局系不存在，网络/AOI/移动系统全要为假运动打补丁，补丁打完等于重做双域方案
- **多层通用嵌套**：每次判定沿父链换算，判定逻辑被转换代码淹没；本方案只在 `ParentRef` 保留挂载语义，判定层永远只看单域坐标
- **切线推进 + 引力/射线吸附（着地）**：矢高误差随距离平方增长，依赖兜底掩盖模型错误；球面旋转位移几何约束零误差
- **四舍五入兑换**：超发冲量、残差正负振荡；截断 + 残差池守恒且可单调推理
