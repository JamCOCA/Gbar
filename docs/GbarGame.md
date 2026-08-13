# Gbar 游戏数据设计方案（3D）

坐标三层模型：宇宙格子（L0）→ 天体/载具局部空间（L1）→ 浮点活动区域（L2）。
3D 世界，所有坐标均为 XYZ。

## 一、坐标组件

```csharp
namespace Gbar.Core.Components;

// L0 宇宙格子：1 格 = 1 km，只挂在最外层天体/空间站上
// 用 long：int 仅 ±14 AU 装不下恒星系；long 达 ±92 亿亿 km
public struct AbsoluteCell
{
    public long X;
    public long Y;
    public long Z;
}

// 当前所属父空间（行星/飞船/空间站/宇宙根空间实体）
public struct SpaceRef
{
    public Entity Parent;
}

// L1 空间内坐标：float，配合 Region 重定保证精度
public struct LocalPosition
{
    public float X;
    public float Y;
    public float Z;
}

public struct LocalVelocity
{
    public float X;
    public float Y;
    public float Z;
}

// 局部朝向（行星表面需跟随地表切平面旋转）
public struct LocalRotation
{
    public float X;   // 四元数
    public float Y;
    public float Z;
    public float W;
}
```

## 二、空间与玩法组件

**坐标层面不需要"空间定义"组件**：`AbsoluteCell + SpaceRef + LocalPosition` 三件套即可表达任意嵌套。任何实体都可以被 `SpaceRef` 指向，被指向了它就是空间，无需标记。

以下是**玩法数据**，按需挂在空间实体上：

```csharp
// 重力源（行星/飞船/空间站按需挂）：该空间对其成员施加的重力
public struct GravitySource
{
    public float Acceleration;     // 重力加速度 m/s²（0=无重力）
    public byte Mode;              // 0=径向(指向本实体中心) 1=定向(沿 Down 向量)
    public float DownX;            // Mode=1 时的"下"方向单位向量
    public float DownY;
    public float DownZ;
}

// 仅行星额外需要：轨道与引力圈（KSP 式 patched conics）
public struct BodyOrbit
{
    public float Rotation;         // 当前自转角（弧度）
    public float AngularVelocity;  // 自转角速度（rad/tick）
    public double Mu;              // 引力参数 μ = G*M（m³/s²），双精度
    public double SurfaceRadius;   // 地表半径（米）
    public double SoiRadius;       // 引力圈半径（米），加载时按 KSP 公式算好缓存
    public float RegionGridSize;   // 地表 Region 角步长（度），见 9.2
}

// KSP 引力圈公式：r_SOI = a · (m / M)^(2/5)
//   a = 本天体轨道半长轴，m = 本天体质量，M = 母天体质量
// 语义：SOI 内只受本天体引力，SOI 外只受母天体引力 —— 任一时刻重力源唯一，
//       与 SpaceRef 切换天然对应（进 SOI = 换父空间）
```

规则：
- 实体有 `SpaceRef` → 它是某个空间的**成员**。
- 实体被别的 `SpaceRef` 指向 → 它**是**一个空间（隐式，无组件）。
- 飞船/空间站：既有 `SpaceRef`（属于父空间），又被角色的 `SpaceRef` 指向（是子空间）。
- 世界坐标换算（第六节）只依赖坐标三件套，不读任何玩法组件。

## 三、重力组件

```csharp
// 受当前空间重力影响的标记；太空漂浮实体移除此组件
public struct GravityAffected;

// 缓存的"上"方向单位向量，由重力系统写入，移动/渲染消费
public struct UpVector
{
    public float X;
    public float Y;
    public float Z;
}
```

## 四、网络同步组件

```csharp
public struct PlayerTag;

public struct NetworkId
{
    public int Value;
}
```

同步包：`{ spaceNetId, networkId, localPos, localVel }`。
跨空间事件（登陆/起飞/上下飞船）离散下发：`{ networkId, fromSpaceNetId, toSpaceNetId, newLocalPos, newLocalVel }`。

## 五、嵌套实例（核心）

### 5.1 组件组合速查

坐标三件套（`AbsoluteCell` 仅最外层需要）+ 玩法组件按需挂：

| 实体 | AbsoluteCell | SpaceRef | LocalPosition/Velocity | GravitySource | BodyOrbit |
|---|---|---|---|---|---|
| 宇宙根空间 | — | — | — | — | — |
| 行星 | ✔ | → 宇宙根 | ✔（公转由 OrbitalSystem 算） | ✔ 径向 | ✔ |
| 飞船（飞行中） | — | → 宇宙根 或 行星 | ✔ | 按需 | — |
| 飞船（停在行星上） | — | → 行星 | ✔（静止） | 按需 | — |
| 飞船（停在空间站） | — | → 空间站 | ✔（静止） | 按需 | — |
| 空间站 | ✔ | → 宇宙根 | ✔ | ✔ 定向 | — |
| 角色 | — | → 所在的空间 | ✔ | — | — |

注：飞船/空间站作为"空间"没有额外组件——被角色 `SpaceRef` 指向即成立。

### 5.2 场景举例

**星球**
```
Planet#101
  AbsoluteCell  = (1_500_000, 0, 0)          // 距恒星 150 万 km
  GravitySource { Acceleration=9.8, Mode=径向 }
  BodyOrbit     { SurfaceRadius=6_000_000m, SoiRadius=80_000_000m }
  SpaceRef      → UniverseRoot
```

**太空中的飞船 + 船内角色**
```
Ship#201
  SpaceRef      → UniverseRoot
  LocalPosition = (1_500_003_210, 500, -200)  // 宇宙空间内坐标（米）
  // 无 GravitySource：船内失重

Character#301
  SpaceRef      → Ship#201
  LocalPosition = (2.5, 0, -1.2)              // 船舱内坐标
```

**行星表面的飞船 + 船内角色**
```
Ship#201
  SpaceRef      → Planet#101                  // 着陆：参照系切换
  LocalPosition = (6_000_012, 3, -850)        // 行星局部坐标（地表附近）

Character#301
  SpaceRef      → Ship#201                    // 角色不变，自动跟随行星公转
  LocalPosition = (2.5, 0, -1.2)
```
角色一整条链：角色 → 飞船 → 行星 → 宇宙。**任何一层父空间运动，子对象全部零成本跟随。**

**空间站 + 站内飞船 + 船内角色**
```
Station#401
  AbsoluteCell  = (1_500_100, 0, 0)
  GravitySource { Acceleration=9.8, Mode=定向, Down=(0,-1,0) }  // 旋转人工重力
  SpaceRef      → UniverseRoot

Ship#202
  GravitySource { Acceleration=9.8, Mode=定向, Down=(0,-1,0) }
  SpaceRef      → Station#401                 // 停靠在站内
  LocalPosition = (120, 0, 45)                // 停机坪位置

Character#302
  SpaceRef      → Ship#202
  LocalPosition = (0.8, 0, 0.3)               // 驾驶舱座位
```

嵌套链最长 4 层：`角色 → 飞船 → 空间站 → 宇宙根`。

## 六、世界坐标换算

```csharp
// 沿 SpaceRef 链向上递归，每层做一次 旋转+平移
Vector3 ToWorld(Entity e)
{
    var pos = new Vector3(e.LocalPosition);
    var space = e.SpaceRef.Parent;
    while (space.Has<SpaceRef>())
    {
        pos = Rotate(pos, space.LocalRotation) + new Vector3(space.LocalPosition);
        space = space.SpaceRef.Parent;
    }
    // 顶层：加上宇宙格子
    if (space.Has<AbsoluteCell>())
        pos += new Vector3(space.AbsoluteCell) * 1000f;   // 格→米
    return pos;
}
```

## 七、系统与组件消费关系

| 系统 | 组 | 读 | 写 | 结构变更（CommandBuffer） |
|---|---|---|---|---|
| `OrbitalSystem` | FixedUpdate（低频） | BodyOrbit | AbsoluteCell、BodyOrbit.Rotation | — |
| `RadialGravitySystem` | FixedUpdate | GravitySource、LocalPosition | LocalVelocity、UpVector | — |
| `MovementSystem` | FixedUpdate | LocalVelocity | LocalPosition | — |
| `SoiTransferSystem` | FixedUpdate | AbsoluteCell、BodyOrbit、LocalPosition | — | 改 SpaceRef、加/移除 GravityAffected |
| `DockTransferSystem` | FixedUpdate | 上下飞船/停靠交互 | — | 改 SpaceRef |
| `RegionRebaseSystem` | Update | LocalPosition | LocalPosition | — |

## 八、参照系切换规则

```
行星引力圈：位置在某天体 SOI 内 → SpaceRef = 该天体（KSP 语义：任一时刻重力源唯一）
           离开所有 SOI       → SpaceRef = 宇宙根（受母恒星引力或惯性）
上下飞船：  进入船体范围     → SpaceRef = 飞船；出舱       → SpaceRef = 飞船的父空间
停靠：      飞船进入站内泊位  → 飞船.SpaceRef = 空间站
跨 Region： 越过 Region 边界  → SpaceRef = 新 Region（锚点换算一次）
```

每次切换只做一次坐标换算（沿新链求逆变换），帧内永远只在单一空间内积分。

## 九、巨型天体：引力范围与 AOI

### 9.1 引力范围：KSP 引力圈（patched conics）

行星再大（半径 6000 km = 6000 格），引力判定也只在**行星局部空间**内做一次：

```
d = |角色.LocalPosition|            // 角色在行星空间时，原点即行星中心
d < SoiRadius      → 受引力，g(r) = μ / r²   // μ 来自 BodyOrbit，径向朝心
d < SurfaceRadius  → 着陆
```

- SOI 半径按 KSP 公式 `r_SOI = a·(m/M)^(2/5)` 加载时算好缓存进 `BodyOrbit.SoiRadius`，运行时只查不算；
- **任一时刻重力源唯一**：SOI 内只受本行星引力，SOI 外只受母恒星引力（或惯性飞行）——没有多体叠加，`SoiTransferSystem` 判定 = 找"当前位置处于哪个天体的 SOI 内"，换 `SpaceRef` 即换重力；
- 距离比较用 `double` 算 d²，避免大数平方溢出；
- 与 Cell 数量无关：行星永远只占中心那一格，SOI 是米制球壳不是格子集合。

### 9.2 地表分割：经纬网格（首选） vs 立方球面四叉树

巨行星地表距中心 6000 km，角色坐标若直接存行星空间，float 精度仅 ~0.5 m，无法行走。必须把地表切成 Region（一层普通嵌套空间）。两种分割方案对比：

| | 经纬网格（Lat-Long Grid）✔ 首选 | 立方球面四叉树（Cube-Sphere Quadtree） |
|---|---|---|
| 映射 | `atan2` 一次求 lat/lon，除步长取整得 Region 编号，**几行代码** | 先定面（最大分量）再投影 uv，再沿四叉树下探，代码量数倍 |
| 邻域查询 | 行列 ±1，平凡 | 面内简单，**跨面邻接需要旋转换算表** |
| 面积均匀性 | 差：高纬度 Region 东西向收缩 | 好：全域近似均匀 |
| 极点 | 奇异性：极点处经度无意义，需特判 | 无极点问题 |
| 层级细分 | 固定步长，粗细不可调 | 天然支持四叉树层级细分 |
| ECS 落地 | Region 编号 = (latIdx, lonIdx) 直接做哈希键 | 需要维护树结构 |

**结论：经纬网格更好实现**，用两步化解其缺陷：

1. **极冠合并**：|lat| > 85° 的区域整体合并为单个"极冠 Region"（极点附近本来就少人去），消除经度奇异性与 Region 面积趋零问题；
2. **变步长（可选）**：高纬度行加大经度步长，使 Region 实际边长保持 ~10 km。

```
地表 Region 划分（6000 km 行星，步长 0.1° ≈ 10.5 km）：
  lat ∈ [-85°, 85°]：3600 × 1700 个 Region（稀疏创建，有人才实例化）
  |lat| > 85°：南北各 1 个极冠 Region
Region 实体：
  SpaceRef      → Planet
  LocalPosition → 锚点（Region 中心，行星空间 double 坐标）
  RegionKey     → (latIdx, lonIdx)，用于邻域查询与懒创建
```

角色/建筑 `SpaceRef → Region`，`LocalPosition` 量级 < 10 km，float 精度充裕。跨 Region 走 `DockTransferSystem` 同款参照系切换；起飞后 `SoiTransferSystem` 直接把 SpaceRef 从 Region 换到行星（跳过中间层）。四叉树留作升级路径——Region 接口不变，只换 RegionKey 的编码与邻域算法。

嵌套链最长 5 层：`角色 → Region → 行星 → 宇宙根`（船内角色 6 层）。

### 9.3 AOI 分层同步

| 层 | 范围 | 同步内容 | 频率 |
|---|---|---|---|
| L0 天体间 | 角色所在 Cell ± N 格邻域 | 天体/空间站：仅 `AbsoluteCell` + 轨道参数（客户端自算轨道，远景只是光点） | 低频（1 tick/s 或变化时） |
| L1 空间内 | 角色当前空间（Region/船/站）内按 LocalPosition 半径过滤 | 完整 `EntityState`（NetId + SpaceNetId + 位置速度） | 每 tick Delta |
| 父链 | 角色 SpaceRef 链上所有空间实体 | Region、行星、飞船、空间站本体 | 变化时 |

- L0 邻域查询：Cell 做哈希格索引，`O(1)` 查邻格，**不关心行星占几格**——行星永远只占中心那一格；
- 跨层事件（进入新 Region / 新 Cell 邻域）复用 Delta 的 `Spawned/Despawned`；
- 效果：角色在 6000 km 行星表面行走时，同步集合 = 本 Region ± 邻 Region 的实体（几十~几百个）+ 父链 4 个实体 + 邻域 Cell 的远景天体（个位数），与行星尺寸完全解耦。
