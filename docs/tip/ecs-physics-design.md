# ECS 与物理世界交互设计

## 核心原则

ECS 是权威数据源（Source of Truth），物理世界只做碰撞检测，不做动力学模拟。

## 职责划分

| ECS 世界 | 物理世界 |
|---------|---------|
| 最终位置、速度、状态 | 碰撞检测、射线检测 |
| 移动逻辑（玩家输入、寻路） | 形状投射（Sweep） |
| 技能判定、伤害计算 | 触发器事件（进入/离开） |
| AI 决策 | — |

## 每帧同步流程

```
1. ECS 处理输入 → 计算目标位置和速度
          ↓
2. 物理世界：Sweep 检测路径上的障碍
          ↓
3. 物理世界：返回碰撞信息（是否碰撞、碰撞点、法线）
          ↓
4. ECS：根据碰撞结果修正位移
          ↓
5. ECS：更新最终位置（存入 ECS）
```

## 为什么物理不做积分

物理引擎只做**形状查询**（Sweep / Overlap），不做 `速度 * dt` 积分和碰撞响应。ECS 负责计算位移，物理只回答"这条路能不能走"。

避免的问题：
- 两次积分导致位置漂移
- 碰撞穿透
- 无限循环修正

## 代码示例

```csharp
// 1. ECS 计算目标位置
var targetPos = pos + velocity * dt;

// 2. 物理世界 Sweep 检测
var hit = physicsWorld.SweepSphere(
    start: pos,
    direction: velocity,
    distance: velocity.Length * dt,
    radius: playerRadius
);

// 3. ECS 根据碰撞结果修正位移
if (!hit)
{
    pos = targetPos; // 路径畅通
}
else
{
    // 移动到碰撞点（留间隙防止浮点误差）
    pos += velocity * dt * hit.Fraction - velocity.Normalized * 0.001f;
}
```

## 滑动处理（可选）

```csharp
if (hit)
{
    var slideDir = Vector3.ProjectOnPlane(velocity, hit.Normal);
    var remaining = velocity * dt * (1 - hit.Fraction);
    pos += slideDir.Normalized * remaining.Length;
}
```

## 谁需要物理

| 场景 | 处理方式 |
|-----|---------|
| 玩家 WASD 移动 | ECS 直接算 + Sweep 检测 |
| 被击退、掉落、弹道 | 物理引擎全权模拟（方案 B） |
| 技能范围判定 | 物理 Overlap 查询 |

## 两种方案对比

| | 方案 A（Sweep） | 方案 B（RigidBody） |
|--|---------------|-------------------|
| 谁算位置 | ECS | 物理引擎 |
| 碰撞响应 | 手动处理 | 自动计算 |
| 是否穿墙 | 不（先检测后移动） | 不 |
| 适合 | 角色移动、简单项目 | 抛射物、刚体堆叠 |

服务端角色移动推荐方案 A。
