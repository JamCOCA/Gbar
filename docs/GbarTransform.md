# Gbar 坐标与变换（Transform）

定稿方案：**双域扁平坐标**。实体在任意时刻只处于一个坐标域，坐标只存一份，域切换集中在一个系统。

## 定点数

坐标和速度用 `long` 定点存储，缩放 1:100（两位小数，厘米精度，范围 ±9.2×10¹⁴ m，星系级够用）。

- 加减、比较：直接整数运算，零误差、跨机器确定
- 乘除、浮点转换：走 `Gbar.Core.Maths.Fix` 工具类，防止手写漏 Scale
- 引力等复杂运算：临时转 `double` 算，结果转回 `long`

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

## 两个坐标域

**全局域**（所有 SOI 之外的深空）：

- 星球本身也是全局坐标实体，由轨道系统推进
- 移动 = 纯整数加法 `position += velocity`

```csharp
public struct GlobalPosition
{
    public long X;
    public long Y;
    public long Z;
}

public struct GlobalVelocity
{
    public long X;
    public long Y;
    public long Z;
}
```

**局部域**（一个星球的整个 SOI：地表 + 大气 + 轨道）：

- `Parent` 指向父空间实体（行星/飞船/空间站）
- 行星怎么动，成员零成本跟随——"相对静止"是真实的，网络/AOI 看到的是真静止

```csharp
public struct ParentRef
{
    public Entity Parent;    // 父空间实体（行星/飞船/空间站）
}

public struct LocalPosition
{
    public long X;
    public long Y;
    public long Z;
}

public struct LocalVelocity
{
    public long X;
    public long Y;
    public long Z;
}
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

## 引力

KSP 式引力圈：任一时刻重力源唯一，SOI 半径按 `r = a·(m/M)^(2/5)` 加载时算好缓存在 `BodyOrbit` 里。

- 局部域飞行实体：引力朝行星心，`d` 直接是 `LocalPosition` 模长
- 着地实体：不积分，由玩法系统给局部速度
- 全局域实体：受母恒星引力或惯性

```csharp
public struct BodyOrbit
{
    public double Mu;              // 引力参数 μ = G*M
    public double SurfaceRadius;   // 地表半径（米）
    public double SoiRadius;       // 引力圈半径（米），加载时算好缓存
    public float Rotation;         // 当前自转角（弧度）
    public float AngularVelocity;  // 自转角速度（rad/tick）
}
```

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

## 系统一览

| 系统 | 职责 |
|---|---|
| `OrbitalSystem` | 推进星球公转自转（低频） |
| `SoiTransferSystem` | 域切换，唯一坐标转换点（迟滞） |
| `GravitySystem` | 引力积分，double 算 long 存 |
| `MovementSystem` | 两域各自的整数加法移动 |
| `SpaceAoiSystem` / `LocalAoiSystem` | cell/region 索引重挂 |

## 否决的备选

- **方案 B（全局坐标 + 每 tick 跟随改写）**："静止"在全局系不存在，网络/AOI/移动系统全要为假运动打补丁，补丁打完等于重做 A
- **多层通用嵌套**（GbarGame.md 旧设计）：每次判定沿父链换算，判定逻辑被转换代码淹没；本方案只在 `ParentRef` 保留挂载语义，判定层永远只看单域坐标
