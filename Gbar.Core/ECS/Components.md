# ECS 组件索引

> `Gbar.Core/ECS/Components/` 目录下的所有组件定义。

---

## 空间

### `Transform` (`Transform.cs`)

```csharp
public record struct Transform(Position Position, Forward Forward);
```

组合组件，实体的空间表示。拆开使用时可以单独查询 `Position` 或 `Forward`。

### `Vector3` (`Transform.cs`)

```csharp
public readonly record struct Vector3(float X, float Y, float Z);
```

基础三维向量。提供 `Length`、`Normalized()`、`Cross(a, b)` 和常量 `Zero`、`WorldUp`。

### `Position` (`Transform.cs`)

```csharp
public readonly record struct Position(Vector3 Value);
```

世界坐标。`X/Y/Z` 直接代理到内部 `Vector3`。可与 `Forward` 独立附加到实体。

### `Forward` (`Transform.cs`)

```csharp
public readonly record struct Forward(Vector3 Value);
```

朝向向量（归一化）。`X/Y/Z` 直接代理到内部 `Vector3`。
- `Right()` — Forward × WorldUp，返回归一化的右方向
- `Up()` — Right × Forward，返回归一化的上方向

---

## 属性

### `BoundedInt` (`Type/BoundedInt.cs`)

```csharp
public readonly record struct BoundedInt(int Current, int Max);
```

通用「当前值/最大值」类型，自动 clamp 到 `[0, Max]`。
- `Ratio` — `Current / Max`
- `IsEmpty` / `IsFull`
- `Add(int amount)` / `Reduce(int amount)` — 增减当前值，返回新实例
- `WithMax(int max)` — 设置新的上限，当前值超限时截断，返回新实例

### `Health` (`Health.cs`)

```csharp
public readonly record struct Health(BoundedInt Value);
```

- `Current` / `Max` / `Ratio` — 代理到内部 `BoundedInt`
- `IsDead` — `Current <= 0`
- `Add(int amount)` / `Reduce(int amount)` / `WithMax(int max)` — 代理到 `BoundedInt`，返回新实例

### `Mana` (`Mana.cs`)

```csharp
public readonly record struct Mana(BoundedInt Value);
```

- `Current` / `Max` / `Ratio` — 代理到内部 `BoundedInt`
- `IsEmpty` / `IsFull`
- `Add(int amount)` / `Reduce(int amount)` / `WithMax(int max)` — 代理到 `BoundedInt`，返回新实例

### `Stats` (`Stats.cs`)

```csharp
public record struct Stats(
    int Strength,
    int Agility,
    int Intelligence,
    int Attack,
    int Defense,
    float MoveSpeed
);
```

基础战斗属性，系统初始化时写入，Buff/Debuff 通过额外组件叠加。

---

## 状态

### `State` (`State.cs`)

```csharp
public record struct State(EntityState Value);
```

原子化状态组件，枚举值如下：

### `EntityState` (`EntityState.cs`)

| 值 | 说明 |
|---|---|
| `Idle` | 空闲 |
| `Moving` | 移动中 |
| `Attacking` | 攻击中 |
| `Casting` | 施法中 |
| `Stunned` | 眩晕 |
| `Dead` | 死亡 |

---

## 已移除

| 组件 | 移除原因 |
|------|---------|
| `Velocity` | 位置更新由系统直接计算，不需要独立速度组件 |
| `Status` | 拆分为原子化 `Health` + `Mana` + `State` |

---

## 查询示例

```csharp
// 移动系统
var moveQuery = new QueryDescription().WithAll<Position, Forward>();

// 扣血
world.Query(in query, (ref Health hp) => {
    hp = hp.Reduce(10);  // 必须重新赋值
});

// 死亡判定
var deadQuery = new QueryDescription().WithAll<Health>();
world.Query(in deadQuery, (ref Health hp) => {
    if (hp.IsDead) { /* ... */ }
});

// 状态机切换
var stateQuery = new QueryDescription().WithAll<State>();
```
