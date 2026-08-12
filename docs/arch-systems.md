# Arch.Extended 完整指南

基于 [Arch.Extended Wiki](https://github.com/genaray/Arch.Extended/wiki) 整理，结合本项目（Gbar）的实际用法。

## 概述

`Arch.Extended` 是 [Arch](https://github.com/genaray/Arch) ECS 框架的扩展包，提供系统组织、源码生成、事件总线、持久化、实体关系等实用功能。

- 🛠️ **高效** — 为主仓库添加实用工具与特性
- ☕️ **简单** — 易用、可靠、易懂
- 💪 **持续维护** — 积极开发与维护中
- 🚢 **多平台** — 支持 .NET Standard 2.1、.NET 6/7，可用于 Unity 和 Godot

### 功能一览

| 模块 | 说明 |
|------|------|
| [Systems API](#systems-api) | 通过系统组织、复用和编排查询 |
| [Source Generator](#source-generator) | 声明式特性 + 源码生成，让查询自动编写 |
| [AOT Source Generator](#aot-source-generator) | 自动注册组件，支持 Native AOT |
| [EventBus](#eventbus) | 源码生成的事件总线，零开销事件分发 |
| [Relationships](#relationships) | 实体间关系引用 |
| [Persistence](#persistence) | World/Entity 的 JSON 与二进制序列化 |
| [Lowlevel & 资源管理](#lowlevel--资源管理) | 原生内存数据结构与资源句柄 |

---

## Systems API

World、Query 和 Entity 可以通过 System API 进行组织和结构化，系统还可以分组统一调度。System API 可以完全脱离源码生成器使用。

### 生命周期

`BaseSystem<World, float>` 提供以下可重写的方法，由 `Group` 统一调用：

| 方法 | 说明 |
|------|------|
| `Initialize()` | 初始化，所有系统注册后调用一次 |
| `BeforeUpdate(in T state)` | 每帧 Update 之前调用 |
| `Update(in T state)` | 每帧核心逻辑（必须实现） |
| `AfterUpdate(in T state)` | 每帧 Update 之后调用 |
| `Dispose()` | 释放资源 |

### 手动查询（无 Source Generator）

```csharp
public struct Position { public float X, Y; }
public struct Velocity { public float X, Y; }

public class MovementSystem : BaseSystem<World, float>
{
    private readonly QueryDescription _desc = new QueryDescription().WithAll<Position, Velocity>();

    public MovementSystem(World world) : base(world) { }

    public override void Update(in float deltaTime)
    {
        World.Query(in _desc, (ref Position pos, ref Velocity vel) =>
        {
            pos.X += vel.X;
            pos.Y += vel.Y;
        });
    }
}
```

### 分组与调度（Group）

```csharp
var deltaTime = 0.05f; // 通常由引擎/框架提供

var world = World.Create();
var systems = new Group<float>(
    new MovementSystem(world),   // 按注册顺序执行
    new MyOtherSystem(world)
);

systems.Initialize();               // 初始化所有已注册系统
systems.BeforeUpdate(in deltaTime); // 调用所有系统的 BeforeUpdate
systems.Update(in deltaTime);       // 调用所有系统的 Update
systems.AfterUpdate(in deltaTime);  // 调用所有系统的 AfterUpdate
systems.Dispose();                  // 调用所有系统的 Dispose
```

典型游戏循环中每帧只需调用 `BeforeUpdate` / `Update` / `AfterUpdate`；`Initialize` 和 `Dispose` 各调用一次。

---

## Source Generator

`Arch.System.SourceGenerator` 包提供代码生成工具，查询几乎可以"自动编写"，节省大量样板代码。

查询方法可以在任何 **partial** 类中生成，但在 `BaseSystem` 中最有意义。特性用于描述要生成的查询，生成的查询总会调用被标注的方法。生成的方法也可以手动调用。

> 注意：使用源码生成的类必须位于命名空间内，暂不支持全局命名空间。

### 特性语法

- `[Query(Parallel = bool)]` — 标记方法，让代码生成器将其转换为查询（必需）
- `[All<T0...T25>]` — 对应 `QueryDescription.All`，实体必须拥有全部这些组件
- `[Any<T0...T25>]` — 对应 `QueryDescription.Any`，实体至少拥有其中一个组件
- `[None<T0...T25>]` — 对应 `QueryDescription.None`，实体不能拥有这些组件
- `[Exclusive<T0...T25>]` — 对应 `QueryDescription.Exclusive`，定义独占的组件集合
- `[Data]` — 标记方法参数，指定该类型应透传给查询

`Query` 为必需，`All`、`Any`、`None`、`Exclusive`、`Data` 可选且相互独立。

对于使用 `.NET Standard 2.1` 或 `.NET 6` 的项目，还有非泛型版本：

```csharp
[All(typeof(...), ...)]
[Any(typeof(...), ...)]
[None(typeof(...), ...)]
[Exclusive(typeof(...), ...)]
```

### BaseSystem 中的查询方法

在 `BaseSystem` 中使用时，`Update` 方法会被自动生成，查询按顺序调用：

```csharp
public partial class MovementSystem : BaseSystem<World, float>
{
    public MovementSystem(World world) : base(world) { }

    // 生成查询，对所有拥有 Position 和 Velocity 的实体调用此方法
    [Query]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MoveEntity([Data] ref float time, ref Position pos, ref Velocity vel)
    {
        pos.X += time * vel.X;
        pos.Y += time * vel.Y;
    }

    // 生成带过滤的查询：拥有 Velocity、Player、Mob、Particle，
    // 且拥有 Moving 或 Idle，且不拥有 Alive 的实体
    [Query]
    [All<Player, Mob, Particle>, Any<Moving, Idle>, None<Alive>]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void StopDeadEntities(ref Velocity vel)
    {
        vel.X = 0;
        vel.Y = 0;
    }
}
```

被标记为 `[Data]` 的参数会直接接收 `BaseSystem.Update(in T state)` 传入的状态（如上例的 `float time`）。

如果你自己重写了 `Update`，生成器只生成查询方法（命名规则为 `方法名 + Query`），可以手动调用：

```csharp
public partial class MovementSystem : BaseSystem<World, GameTime>
{
    private readonly QueryDescription _customQuery = new QueryDescription().WithAll<Position, Velocity>();

    public MovementSystem(World world) : base(world) { }

    public override void Update(in GameTime t)
    {
        World.Query(in _customQuery, (in Entity entity) => Console.WriteLine($"Custom: {entity}")); // 手动查询
        MoveEntityQuery(World); // 调用生成的查询方法
    }

    [Query]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MoveEntity(ref Position pos, ref Velocity vel)
    {
        pos.X += vel.X;
        pos.Y += vel.Y;
    }
}
```

### 自定义类中的查询方法

查询可以在任何类中生成，便于分组和复用：

```csharp
public partial class MyQueries
{
    [Query]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MoveEntities([Data] in float time, ref Position pos, ref Velocity vel)
    {
        pos.X += time * vel.X;
        pos.Y += time * vel.Y;
    }

    [Query]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DamageEntities(ref Hitted hit, ref Health health)
    {
        health.value -= hit.value;
    }
}

// 实例化并调用生成的方法
var myQueries = new MyQueries();
myQueries.MoveEntitiesQuery(someWorld, 10.0f);  // World 为必需参数，10.0f 是 [Data] 参数
myQueries.DamageEntitiesQuery(someWorld);
```

静态类同样支持，且性能更好（无需类实例）：

```csharp
public static partial class MyQueries
{
    [Query]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MoveEntities([Data] in float time, ref Position pos, ref Velocity vel)
    {
        pos.X += time * vel.X;
        pos.Y += time * vel.Y;
    }
}

MyQueries.MoveEntitiesQuery(someWorld, 10.0f);
```

### 并行 / 多线程查询

源码生成器支持多线程并行查询，只需设置 `Parallel = true`（仅支持静态方法）：

```csharp
[Query(Parallel = true)]  // 由 Arch 的多线程机制调用
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void MoveEntities([Data] in float time, ref Position pos, ref Velocity vel)
{
    pos.X += time * vel.X;
    pos.Y += time * vel.Y;
}
```

### 使用源码时的项目配置

如果不使用 NuGet 而是引用源码，需要在 `.csproj` 中将 SourceGenerator 链接为 Analyzer：

```xml
<ItemGroup>
  <ProjectReference Include="..\Arch\src\Arch\Arch.csproj" />
  <ProjectReference Include="..\Arch.Extended\Arch.System\Arch.System.csproj" />
  <ProjectReference Include="..\Arch.Extended\Arch.System.SourceGenerator\Arch.System.SourceGenerator.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

---

## AOT Source Generator

Arch 本身基本兼容 Native AOT，但仍需要一些样板代码（尤其是组件注册）。AOT 源码生成器解决了这个问题。

### 自动 AOT 友好的组件注册

只需用 `[Component]` 特性标记组件：

```csharp
[Component]
public struct Velocity { ... }

[Component]
public struct Transform { ... }
```

生成器会生成如下注册模块：

```csharp
namespace Arch.AOT.SourceGenerator
{
   internal static class GeneratedComponentRegistry
   {
      [ModuleInitializer]
      public static void Initialize()
      {
          ComponentRegistry.Add(new ComponentType(ComponentRegistry.Size + 1, typeof(Velocity), Unsafe.SizeOf<Velocity>(), false));
          ArrayRegistry.Add<Velocity>();

          ComponentRegistry.Add(new ComponentType(ComponentRegistry.Size + 1, typeof(Transform), Unsafe.SizeOf<Transform>(), false));
          ArrayRegistry.Add<Transform>();
      }
   }
}
```

注意 `[ModuleInitializer]`：生成的类会在应用启动时自动调用来注册组件。

---

## EventBus

用于解耦代码、通过事件外置逻辑。特点是**完全由源码生成**，速度与原生方法调用相当，甚至可以被内联，支持静态类和实例。

```csharp
public partial class MyInstanceReceiver
{
    public MyInstanceReceiver() { Hook(); } // 开始监听

    [Event(order: ...)]
    public void OnShootEvent(ref ShootEvent @event)
    {
        // 处理事件
    }
}

public static class SomeEventHandler
{
    [Event(order: ...)]
    public static void OnShootFireBullets(ref ShootEvent @event)
    {
        // ref / in / 无修饰符均可，事件类型不限，但只能有一个参数
    }
}

var shootEvent = ...;
EventBus.Send(ref shootEvent); // 将事件广播给所有被标注的方法
```

---

## Relationships

实体间引用关系的最佳方案。无需自己在组件中维护列表，直接使用 Relationships：

```csharp
// 关系标记（也可以存储数据）
public struct ParentOf { }

var parent = world.Create();
var childOne = world.Create();
var childTwo = world.Create();

// 添加实体间关系
parent.AddRelationship<ParentOf>(childOne);
parent.AddRelationship<ParentOf>(childTwo, new ParentOf(...));

// 判断、获取、设置关系
var hasRelationship = parent.HasRelationship<ParentOf>();
var relationToChildOne = parent.GetRelationship<ParentOf>(childOne);
parent.SetRelationship<ParentOf>(childOne, new ParentOf());

// 遍历所有关系
ref var parentOfRelation = ref parent.GetRelationships<ParentOf>();
foreach (var child in parentOfRelation)
{
    ...
}

// 移除关系
parent.RemoveRelationship<ParentOf>(childOne);
parent.RemoveRelationship<ParentOf>(childTwo);
```

> 首次添加关系对源实体和目标实体都是一次结构性变更（structural change）。

---

## Persistence

将 World 或单个 Entity 与 JSON/二进制互转。注意：

- 单实体序列化不支持循环引用，需要自定义处理
- World 序列化可以处理循环实体引用
- 无法处理跨 World 引用

两种序列化器都支持 bytes 和 stream，二进制序列化器还支持 `IBufferWriter<T>`。

### JSON

底层使用 [Utf8Json](https://github.com/neuecc/Utf8Json)：

```csharp
// 初始化序列化器，可注入自定义序列化器处理特定类/结构体
var serializer = new ArchJsonSerializer(new SpriteSerializer { GraphicsDevice = GraphicsDevice }, ...);

var worldJson = serializer.ToJson(world);          // World -> json 字符串
world = serializer.FromJson(worldJson);            // json 字符串 -> World

var entityJson = serializer.ToJson(world, someEntity);  // 单个 Entity -> json
serializer.FromJson(world, entityJson);                 // 反序列化到指定 World

// 自定义序列化器示例
public class SpriteSerializer : IJsonFormatter<Sprite>
{
    public void Serialize(ref JsonWriter writer, Sprite value, IJsonFormatterResolver formatterResolver) { ... }
    public Sprite Deserialize(ref JsonReader reader, IJsonFormatterResolver formatterResolver) { ... }
}
```

### 二进制

底层使用 [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)，快速高效但不可读：

```csharp
var serializer = new ArchBinarySerializer(new SpriteSerializer { GraphicsDevice = GraphicsDevice }, ...);

var worldBytes = serializer.Serialize(world);        // World -> byte[]
world = serializer.Deserialize(worldBytes);          // byte[] -> World

var entityBytes = serializer.Serialize(world, someEntity);  // 单个 Entity -> byte[]
serializer.Deserialize(world, entityBytes);                 // 反序列化到指定 World

// 自定义序列化器示例
public class SpriteSerializer : IMessagePackFormatter<Sprite>
{
    public void Serialize(ref MessagePackWriter writer, Sprite value, MessagePackSerializerOptions options) { ... }
    public Sprite Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) { ... }
}
```

---

## Lowlevel & 资源管理

为降低 GC 压力，底层扩展提供了使用原生内存的类和数据结构，速度快、blittable，可以直接嵌入任何组件。

支持的数据结构：

- `UnsafeArray<T>`
- `UnsafeList<T>`
- `UnsafeStack<T>`
- `UnsafeQueue<T>`
- `UnsafeJaggedArray<T>`

附加数据结构：

- `JaggedArray<T>`
- `SparseJaggedArray<T>`

### UnsafeArray 示例

```csharp
// 创建（using 自动释放）
using var array = new UnsafeArray<int>(8);
array[0] = 0;
array[1] = 1;

// 访问
ref var intRef = ref array[0];

// 遍历
foreach (ref var item in array)
{
    ...
}
```

### UnsafeList 示例

```csharp
using var list = new UnsafeList<int>(8);
list.Add(1);
list.Add(2);

ref var intRef = ref list[0];

foreach (ref var item in list)
{
    ...
}
```

### 资源管理示例

通过 Resource Manager，可以使用唯一 ID 从任何地方访问托管资源。节省内存、blittable，可用于 unsafe 代码：

```csharp
// 创建资源数组并添加 sprite 句柄
var resources = new Resources<Sprite>(IntPtr.Size, capacity: 64);
var handle = resources.Add(new Sprite());
var sprite = resources.Get(in handle);

// 在任何地方使用句柄访问 sprite
public struct SpriteComponent
{
    public Handle<Sprite> handle;
}
```

---

## 本项目约定

- 所有系统放在 `Gbar.Core/Systems/` 目录
- 组件放在 `Gbar.Core/Components/`
- 系统继承 `BaseSystem<World, float>`，`float` 为帧间隔时间（秒）
- 优先使用 Source Generator 方式（`partial class` + `[Query]`），避免手写 `QueryDescription`
- 系统注册与调度集中在 `Gbar.Core/GameServer.cs`

参考实现：`Gbar.Core/Systems/MovementSystem.cs`
