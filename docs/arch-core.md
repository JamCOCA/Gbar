# Arch.Core 完整指南

基于 [Arch 官方文档](https://arch-ecs.gitbook.io/arch) 整理，结合本项目（Gbar）的实际用法。Arch.Extended 扩展部分见 [arch-systems.md](arch-systems.md)。

## 概述

Arch 是一个高性能 C# Archetype & Chunks ECS（Entity Component System）框架：

- 🏎️ **快** — 最佳缓存效率、迭代与分配速度，与 C++/Rust ECS 库同量级
- 🤏 **极简** — 不臃肿，只提供必需品，"Pay what you use"
- ☕️ **简单** — 干净、自解释的 API
- 🚢 **多平台** — 支持 .NET Standard 2.1、.NET 8，可用于 Unity、Godot

```bash
dotnet add PROJECT package Arch --version 2.1.0-beta
```

### 核心概念（ECS 速览）

- **Entity** — 唯一 ID，代表一个对象，本身无逻辑无数据
- **Component** — 附加到 Entity 上的数据（可以是 struct、class、甚至原始类型）
- **System** — 操作组件数据的逻辑
- **Query** — 按组件结构过滤和匹配实体
- 结构相同的实体被打包进 **Archetype** 和 **Chunk**（默认 16KB 内存块，适配 L1 缓存），组件数据在内存中始终连续

```csharp
using Arch.Core;

public record struct Position(float X, float Y);
public record struct Velocity(float Dx, float Dy);

using var world = World.Create();
var player = world.Create(new Position(0, 0), new Velocity(1, 1));

var query = new QueryDescription().WithAll<Position, Velocity>();
world.Query(in query, (Entity entity, ref Position pos, ref Velocity vel) =>
{
    pos.X += vel.Dx;
    pos.Y += vel.Dy;
    Console.WriteLine($"Moved player: {entity.Id}");
});
```

---

## World

World 存储所有实体，提供创建、销毁、查询的方法，处理所有内部机制。可以并行使用多个 World，彼此完全隔离。

### 生命周期

```csharp
var world = World.Create();  // 创建
world.TrimExcess();          // 释放未使用的内存
world.Dispose();             // 清理
World.Destroy(world);        // 销毁
```

### 自定义配置

```csharp
var customizedWorld = World.Create(
    chunkSizeInBytes: 16_382,              // 每个 chunk 的基础大小
    minimumAmountOfEntitiesPerChunk: 100,  // 每个 chunk 的最小实体数
    archetypeCapacity: 8,                  // archetype 数组初始大小
    entityCapacity: 64                     // 实体初始容量
);
```

> 这些都是初始参考值，Arch 可能向上调整，非必要无需修改。

---

## Entity

Entity 就是一个 `record struct Entity { int Id, int WorldId, int Version }`，自身不含数据和逻辑。组件可以是任何类型：原始类型、struct、class。

### 创建与销毁

```csharp
var dwarf = world.Create(new Dwarf("Gimli"), new Position(0, 0), new Velocity());
if (dwarf.IsAlive()) world.Destroy(dwarf);
```

### 修改组件

```csharp
// 获取引用并修改
ref var position = ref dwarf.Get<Position>();  // TryGet 也存在
position.X++;

// 覆盖组件
dwarf.Set(new Velocity(1, 1));

// 添加 / 移除（注意先检查！）
if (!dwarf.Has<Pickaxe>()) dwarf.Add(new Pickaxe());  // Ensure 也存在
dwarf.Remove<Pickaxe>();
```

### 批量操作（最多 25 个泛型参数）

```csharp
dwarf.Add<Pickaxe, DwarfArmor, LeatherBoots, IronHelmet>();

var equipment = dwarf.Get<Pickaxe, DwarfArmor, LeatherBoots, IronHelmet>();
equipment.t0.Durability = 100f;

dwarf.Set(new DwarfArmor(110f), new LeatherBoots(Sole.GRIPPY));

if (dwarf.Has<Bow, ElvishArmor>()) dwarf.Remove<Bow, ElvishArmor>();
```

### 实体引用与版本

实体的 Id 会被回收复用，但 `Version` 不同。因此**必须直接比较 Entity，绝不能只比较 Id**：

```csharp
var dwarf = world.Create<Dwarf, Position, Velocity>();
world.Destroy(dwarf);
var recycledDwarf = world.Create<Dwarf, Position, Velocity>();

Console.WriteLine(dwarf == recycledDwarf); // False，Id 相同但 Version 不同
```

### 检查

```csharp
var types = dwarf.GetComponentTypes();  // 所有组件类型（只读）
var components = dwarf.GetComponents(); // 所有组件（装箱）
Console.WriteLine(dwarf);               // 打印实体
```

### 陷阱

Arch **没有隐藏开销**，一切直接执行、无后台检查。代价是你必须自己保证操作的正确性。查询中创建/销毁实体或改变结构时，**建议用 CommandBuffer 缓存，查询后再执行**。

---

## Query

`QueryDescription` 描述要搜索的实体结构。过滤方法可链式调用，最多接受 25 个泛型参数。

### All — 拥有全部组件

```csharp
var movementQuery = new QueryDescription().WithAll<Position, Velocity>();
world.Query(in movementQuery, (Entity entity, ref Position pos, ref Velocity vel) =>
{
    pos += vel;
});
```

> `QueryDescription` 应只创建一次并复用，不要每次查询前新建。查询中不是必须传入 `Entity` 参数。

### None — 排除组件

```csharp
// 有 Pickaxe 但不是 Elf 的实体
var query = new QueryDescription().WithAll<Pickaxe>().WithNone<Elf>();
world.Query(in query, (ref Pickaxe pickaxe) => { ... });
```

### Any — 至少拥有其一

```csharp
// 有 Bow 或 Sword 的实体
var query = new QueryDescription().WithAny<Bow, Sword>();
world.Query(in query, (Entity entity) =>
{
    ref var bow = ref entity.TryGet<Bow>(out var hasBow);
    if (hasBow) bow.Attack();
});
```

### Exclusive — 精确匹配组件集合

```csharp
// 只匹配恰好拥有这三个组件的实体，多一个少一个都不行
var query = new QueryDescription().WithExclusive<Dwarf, Position, Velocity>();
world.Query(in query, (Entity entity) => Exile(entity));
```

> `WithAll + WithNone` 无法实现精确匹配——`[Dwarf, Position, Velocity, Bow]` 的实体也会被 `WithAll<Dwarf, Position, Velocity>().WithNone<Pickaxe>()` 命中。

---

## Archetypes & Chunks

- **Archetype** 类似数据库表，包含组件结构完全相同的所有实体。实体的结构决定它的 Archetype
- **Chunk** 是实际存储实体和组件的内存块，默认 **16KB**，正好适配 L1 缓存
- 每个 Archetype 有 N 个 Chunk，可快速分配新块或修剪释放内存
- Chunk 大小动态调整：如果单个 Chunk 放不下最少数量的实体，所有 Chunk 会扩大到基础大小的下一个倍数
- 给实体增删组件会让实体在 Archetype 之间迁移（结构性变更）

---

## 查询技术（4 种方式）

### 1. Query（委托）

```csharp
world.Query(in desc, (ref Position pos, ref Velocity vel) => Move(ref pos, ref vel));
```

✅ 代码少、适合原型 ❌ 委托无法内联、组件只能 ref、可能闭包分配

### 2. InlineQuery（结构体 + 接口）

```csharp
public struct VelocityUpdate : IForEach<Position, Velocity>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(ref Position pos, ref Velocity vel) => Move(ref pos, ref vel);
}

world.InlineQuery<VelocityUpdate, Position, Velocity>(in queryDescription);
```

✅ 极快、可内联、可模块化、可传入结构体实例携带数据 ❌ 样板代码多。用 `IForEachEntity` + `InlineEntityQuery` 可同时访问 Entity。

### 3. 手动枚举（最快）

```csharp
var query = world.Query(in desc);
foreach (ref var chunk in query.GetChunkIterator())
{
    var references = chunk.GetFirst<Position, Velocity>();
    foreach (var entity in chunk)
    {
        ref var pos = ref Unsafe.Add(ref references.t0, entity);
        ref var vel = ref Unsafe.Add(ref references.t1, entity);
        Move(ref pos, ref vel);
    }
}
```

✅ 最快、可自定义、支持 SIMD、可自实现多线程 ❌ 样板代码最多

### 4. Source Generator（Arch.Extended）

见 [arch-systems.md](arch-systems.md)。生成的代码与手动枚举等价，支持 `ref`/`in`/`out` 修饰组件参数。

---

## 向查询传递外部数据

### 闭包（简单但有分配）

```csharp
var intruderPosition = DetectIntruder();
world.Query(in queryDesc, (ref Position pos, ref Velocity vel) =>
    MoveTowards(ref pos, ref vel, intruderPosition));  // 每次调用产生堆分配
```

用 `static` lambda 可避免闭包分配（但不能捕获局部变量）：

```csharp
world.Query(in queryDesc, static (ref Position pos, ref Velocity vel) => { ... });
```

### InlineQuery + 结构体（无分配）

```csharp
private struct VelocityUpdate : IForEach<Position, Velocity>
{
    public Position IntruderPosition;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(ref Position pos, ref Velocity vel)
        => MoveTowards(ref pos, ref vel, IntruderPosition);
}

var velUpdate = new VelocityUpdate { IntruderPosition = DetectIntruder() };
world.InlineQuery<VelocityUpdate, Position, Velocity>(in queryDesc, ref velUpdate);
```

### 自定义循环（最灵活）

```csharp
var query = world.Query(in queryDesc);
foreach (ref var chunk in query)
{
    var positions = chunk.GetSpan<Position>();
    var velocities = chunk.GetSpan<Velocity>();

    foreach (var entityIndex in chunk)
    {
        ref var pos = ref positions[entityIndex];
        ref var vel = ref velocities[entityIndex];
        MoveTowards(ref pos, ref vel, intruderPosition);
    }
}
```

---

## 批量与预留（Batch & Bulk）

### 预留容量

```csharp
world.EnsureCapacity<Dwarf, Position, Velocity>(1_000_000);  // 保证总容量为 100 万
for (var index = 0; index < 1_000_000; index++)
    world.Create(signature);
```

### 批量创建

```csharp
world.Create(1_000_000, new Dwarf(), RandomPosition(), RandomVelocity());
// 每个实体拥有相同的组件和值；非泛型版本接受 Signature 并返回 Entity 列表
```

### 批量修改 / 销毁

```csharp
var allDwarfs = new QueryDescription().WithAll<Dwarf>().WithNone<Axe, Armor, Boots>();
world.Add(in allDwarfs, new Axe(), new Armor(), new Boots());   // 批量添加

var dwarfsWithAxe = new QueryDescription().WithAll<Dwarf, Axe>();
world.Set(in dwarfsWithAxe, new Axe(State.Repaired));           // 批量设置

var dwarfsWithArmor = new QueryDescription().WithAll<Armor>();
world.Remove<Armor>(in dwarfsWithArmor);                        // 批量移除

world.Destroy(in dwarfsWithAxe);                                // 批量销毁
// 销毁后容量保留；如需释放内存调用 world.TrimExcess()
```

---

## 多线程

Arch 自带零分配 [JobScheduler](https://github.com/genaray/ZeroAllocJobScheduler)。

### 设置

```csharp
var jobScheduler = new JobScheduler(new JobScheduler.Config
{
    ThreadPrefixName = "Arch.Samples",
    ThreadCount = 0,                 // 0 = 运行时自动决定
    MaxExpectedConcurrentJobs = 64,
    StrictAllocationMode = false,
});
world.SharedJobScheduler = jobScheduler;  // 多个 World 可共享一个 Scheduler
// 结束时 jobScheduler.Dispose();
```

### 并行查询

```csharp
world.ParallelQuery(in queryDesc, (ref Position pos, ref Velocity vel) => Move(ref pos, ref vel));

// Inline 版本
world.InlineParallelQuery<VelocityUpdate, Position, Velocity>(in queryDescription);
```

> ⚠️ `ParallelQuery` / `InlineParallelQuery` 会**阻塞主线程**直到查询处理完毕。访问查询外的共享对象需自行同步。

### 底层：IChunkJob

```csharp
public struct VelocityUpdate : IChunkJob
{
    public void Execute(ref Chunk chunk)
    {
        ref var positions = ref chunk.GetFirst<Position>();
        ref var velocities = ref chunk.GetFirst<Velocity>();

        foreach (var entity in chunk)
        {
            ref var position = ref Unsafe.Add(ref positions, entity);
            ref var velocity = ref Unsafe.Add(ref velocities, entity);
            Move(ref position, ref velocity);
        }
    }
}

world.InlineParallelChunkQuery(in query, new VelocityUpdate());
```

---

## CommandBuffer

将结构性操作缓存起来稍后回放，线程安全，可在 `ParallelQuery` 中使用：

```csharp
var dwarf = world.Create(new Dwarf(), new Position(), new Velocity());

var commandBuffer = new CommandBuffer();
commandBuffer.Add(dwarf, new Pickaxe());              // 延迟添加组件
commandBuffer.Set(dwarf, new Position(10, 10));       // 延迟设置组件

// 甚至可以缓冲整个实体的创建
var nonExistingDwarf = commandBuffer.Create(
    new Signature(typeof(Dwarf), typeof(Position), typeof(Velocity)));

commandBuffer.PlayBack(world, disposal: true);  // 回放；disposal=false 可重复回放
```

---

## 结构性变更注意事项

`Add`/`Remove` **不做任何检查**，重复添加或移除不存在的组件会**破坏内存**：

```csharp
var entity = GetRandomEntity();
entity.Add<Pickaxe>();     // 如果已经有了呢？
entity.Remove<Helmet>();   // 如果没有呢？
```

必须自己检查：

```csharp
if (!entity.Has<Pickaxe>()) entity.Add<Pickaxe>();
if (entity.Has<Helmet>()) entity.Remove<Helmet>();
```

原因：Arch 的信条是 "Pay what you use"，不在后台做任何拖慢代码的检查。

---

## EntityReference

实体 Id 会被回收复用。为避免保存的 Entity 悄悄指向另一个实体，使用 `EntityReference`（比较 Id + Version）：

```csharp
var entity = world.Create<Dwarf, Position, Velocity>();
var reference = entity.Reference();

if (reference.IsAlive())  // 即使 Id 被回收也能正确判断
{
    var referencedEntity = (Entity)reference;
}
```

---

## EntityData

直接访问实体元数据，避免重复查找（只支持 Get/Set，不能 Add/Remove）：

```csharp
var entity = world.Create(new Dwarf(), new Pickaxe());
ref var data = ref world.GetEntityData(entity);

ref var pickaxe = ref data.Get<Pickaxe>();
pickaxe.HP = 100;
data.Set(new Pickaxe());

var archetype = data.Archetype;
ref var chunk = ref data.Chunk;
```

---

## 非泛型 API

每个泛型方法都有非泛型版本，基于 `ComponentType` 和 `Signature`，适合序列化、反射等动态场景：

```csharp
// ComponentType：组件的运行时元数据
var componentType = Component<Dwarf>.ComponentType;                  // 编译时注册，快
var componentType = ComponentRegistry.GetComponentType(typeof(Dwarf)); // 运行时，慢一些

// Signature：组件类型集合
var signature = new Signature(typeof(Dwarf), typeof(Position), typeof(Velocity));
var dwarf = world.Create(signature);

dwarf.Set(typeof(Position), position);
if (!dwarf.Has(typeof(Pickaxe))) dwarf.Add(typeof(Pickaxe), new Pickaxe());
dwarf.Remove(typeof(Pickaxe));

// 非泛型 QueryDescription
var queryDesc = new QueryDescription(
    all: new Signature(typeof(Dwarf), typeof(Velocity)),
    any: Signature.Null,
    none: Signature.Null);
```

---

## Events（需 EVENTS 编译标志）

默认 NuGet 包不含实体事件，需引用源码并设置 `EVENTS` 标志（或使用 [Arch-Events](https://www.nuget.org/packages/Arch-Events/) NuGet）：

```csharp
world.SubscribeEntityCreated((Entity entity) => { });
world.SubscribeEntityDestroyed((Entity entity) => { });

world.SubscribeComponentAdded((Entity entity, ref Position _) => { });
world.SubscribeComponentSet((Entity entity, ref Position _) => { });
world.SubscribeComponentRemoved((Entity entity, ref Position _) => { });
```

事件在操作后立即调用，几乎等同直接方法调用。

---

## PURE_ECS

通过编译标志 `PURE_ECS` 将实体压缩到仅 **4 字节**，让更多实体装入每个 Chunk，所有实体操作自动加速。代价是 Entity 结构体上的方法全部不可用，必须改为通过 World 操作（需引用 Arch 源码或使用 [Arch-PureECS](https://www.nuget.org/packages/Arch-PureECS/) NuGet）：

```csharp
// 普通模式
if (gimli.Has<Position>()) { ref var pos = ref gimli.Get<Position>(); }

// PURE_ECS 模式
if (world.Has<Position>(gimli)) { ref var pos = ref world.Get<Position>(gimli); }
```

---

## 本项目约定

- ECS 核心代码位于 `Gbar.Core/`：组件在 `Components/`，系统在 `Systems/`
- World 与系统调度集中在 `Gbar.Core/GameServer.cs`
- 组件使用 `record struct`，查询优先使用 Source Generator（见 [arch-systems.md](arch-systems.md)）
- 结构性变更（Add/Remove/Create/Destroy）避免在查询迭代中直接执行，必要时用 `CommandBuffer`
