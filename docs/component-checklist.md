# ECS 组件清单

> 基于 Arch ECS 架构，物理只做碰撞检测（Sweep/Overlap），ECS 为权威数据源。

## 图例

| 标记 | 含义 |
|------|------|
| ✅ | 已有 |
| 🔄 | 已有但需重构/合并 |
| ⬜ | 待实现 |

---

## 1. 空间与物理

| 组件 | 标记 | 说明 |
|------|------|------|
| `Position` | ✅ | 当前世界坐标 `(X, Y, Z)` |
| `Velocity` | ✅ | 当前速度 `(Dx, Dy, Dz)` |
| `Transform` | 🔄 | 当前是 `(X, Y, Z, Yaw, Pitch, Roll)`，可用 `Position` + `Forward` 替代，不需要显式欧拉角 |
| `Forward` | ⬜ | 朝向向量 `(X, Y, Z)`，归一化，直接决定实体面向，配合 Position 即可表示完整 Transform |
| `Collider` | ⬜ | 碰撞形状（Sphere/Capsule/Box）+ 半径/尺寸，供物理 Sweep/Overlap 使用 |
| `CollisionLayer` | ⬜ | 碰撞层掩码，决定与谁发生碰撞检测 |
| `Gravity` | ⬜ | 重力系数或是否受重力影响（用于掉落、跳跃） |

### 建议
- `Transform` 拆分为 `Position` + `Forward`（朝向向量），无需显式欧拉角。Forward 直接用于移动方向和视野计算。

---

## 2. 属性与状态

| 组件 | 标记 | 说明 |
|------|------|------|
| `Health` | ✅ | 生命值 `(Current, Max)`，含 Damage/Heal/Ratio 等方法 |
| `Health` | ✅ | 生命值 `(Current, Max)`，含 Damage/Heal/Ratio 等方法 |
| `Mana` | ✅ | 魔法值 `(Current, Max)`，含 Consume/Recover/Ratio 等方法 |
| `State` | ✅ | 实体状态 `(Value: EntityState)`，Idle/Moving/Attacking/Casting/Stunned/Dead |
| `Stats` | ✅ | 基础属性 `(Strength, Agility, Intelligence, Attack, Defense, MoveSpeed)` |
| `BuffContainer` | ⬜ | 当前挂载的 Buff/Debuff 列表 |

---

## 3. 输入与控制

| 组件 | 标记 | 说明 |
|------|------|------|
| `MoveInput` | ⬜ | 客户端输入的移动方向/目标点，ECS 读取后计算目标位置 |
| `LookInput` | ⬜ | 视角/朝向输入（Yaw, Pitch），影响 Transform 旋转 |
| `ActionInput` | ⬜ | 动作请求：普通攻击、技能槽位、交互等（事件型，消费后移除） |

---

## 4. 移动系统

| 组件 | 标记 | 说明 |
|------|------|------|
| `MovementConfig` | ⬜ | 移动参数（WalkSpeed、RunSpeed、TurnSpeed、Acceleration） |
| `MovementState` | ⬜ | 当前移动模式（Idle / Walk / Run / Sprint），由 ECS 根据输入和碰撞结果设置 |
| `NavigationAgent` | ⬜ | 寻路代理（目标点、路径点列表、是否到达），AI 和点击移动共用 |
| `Knockback` | ⬜ | 击退状态（方向、剩余时间、速度），ECS 临时接管位移 |

---

## 5. 战斗与技能

| 组件 | 标记 | 说明 |
|------|------|------|
| `SkillCaster` | ⬜ | 技能槽配置、当前施法技能、施法进度、冷却时间映射 |
| `Attack` | ⬜ | 普攻配置（范围、间隔、下次可攻击时间） |
| `Projectile` | ⬜ | 抛射物标记（方案 B：若需物理引擎全权模拟弹道） |
| `HitBox` | ⬜ | 攻击判定形状（Overlap 查询用） |
| `HurtBox` | ⬜ | 受击判定形状（可与 Collider 复用或独立配置） |
| `DamageEvent` | ⬜ | 事件型组件：攻击者、伤害值、伤害类型，一帧后移除 |
| `Death` | ⬜ | 死亡标记（死亡时间、是否已处理死亡逻辑、复活倒计时） |

---

## 6. AI

| 组件 | 标记 | 说明 |
|------|------|------|
| `AIController` | ⬜ | AI 类型 ID、当前状态机状态（Idle / Chase / Attack / Return） |
| `Aggro` | ⬜ | 仇恨列表（目标 Entity + 仇恨值），决定攻击对象 |
| `PatrolPath` | ⬜ | 巡逻路径点数组、当前索引、是否循环 |
| `AIPerception` | ⬜ | 感知范围、上次感知时间、视野角度 |

---

## 7. 网络与同步

| 组件 | 标记 | 说明 |
|------|------|------|
| `NetworkId` | ⬜ | 全局唯一网络 ID（uint / ulong），客户端与服务端对齐实体 |
| `OwnerConnection` | ⬜ | 所属网络连接 ID，用于路由输入和状态同步 |
| `SyncDirty` | ⬜ | 脏标记位（位置、血量、状态等），决定本次需要序列化哪些字段 |
| `LastSyncState` | ⬜ | 上次成功同步的快照，用于增量同步或丢包检测 |

---

## 8. 生命周期与杂项

| 组件 | 标记 | 说明 |
|------|------|------|
| `EntityType` | ⬜ | 实体类型标记（Player / Monster / NPC / Projectile / Item） |
| `Team` | ⬜ | 阵营/队伍 ID，用于友伤判定和仇恨过滤 |
| `SpawnInfo` | ⬜ | 出生点、重生时间、重生计数 |
| `DestroyTimer` | ⬜ | 延迟销毁（用于抛射物、特效残留、尸体） |

---

## 优先级建议

| 阶段 | 组件 |
|------|------|
| **P0 - 立即** | 合并 Position/Transform、Collider、CollisionLayer、MoveInput、MovementConfig、NetworkId、EntityType |
| **P1 - 本周** | Velocity 接入 Sweep 检测、MovementState、LookInput、SkillCaster、Attack、DamageEvent、AIController |
| **P2 - 后续** | NavigationAgent、Aggro、PatrolPath、BuffContainer、Projectile、HitBox/HurtBox、OwnerConnection、SyncDirty |

---

## 待决策事项

1. **Position + Forward 替代 Transform**：用 `Position` 表示位置，`Forward` 表示朝向向量（归一化），无需显式欧拉角。这样移动方向、视野、攻击朝向都可以直接用 Forward 计算。
2. **Velocity 语义**：当前是 `(Dx, Dy, Dz)`，需要明确是"每帧位移"还是"每秒速度"，避免与 `MoveSpeed` 混淆。
3. **Velocity 语义**：当前是 `(Dx, Dy, Dz)`，需要明确是“每帧位移”还是“每秒速度”，避免与 `MoveSpeed` 混淆。
