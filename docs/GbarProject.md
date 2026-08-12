# Gbar 项目架构

基于 [Arch](https://arch-ecs.gitbook.io/arch) ECS 框架的游戏服务器。核心设计：**查询内并行计算 + 结构变更延迟回放**。

相关文档：[arch-core.md](arch-core.md)（Arch 核心）、[arch-systems.md](arch-systems.md)（Arch.Extended）

## 目录结构

```
Gbar/
├── Gbar.Core/                        服务器核心库
│   ├── GameServer.cs                 调度核心：持有 World、驱动主循环
│   ├── CommandBufferHub.cs           CommandBuffer 持有包装（线程安全写入）
│   ├── Components/
│   │   └── Components.cs             组件定义（struct：Position/Velocity/...）
│   ├── SystemGroup/
│   │   ├── UpdateSystemGroup.cs      变步长系统组，持有本组 Commands
│   │   └── FixedUpdateSystemGroup.cs 定步长系统组，持有本组 Commands
│   └── Systems/
│       ├── Base/
│       │   ├── GbarBaseSystem.cs     FixedSystem / UpdateSystem 自注册基类
│       │   └── SystemOrderAttribute.cs  反射注册时的执行顺序特性
│       └── MovementSystem.cs         系统标准模板（参考实现）
│
├── Gbar.App/
│   └── Program.cs                    启动入口：创建服务器、注册系统、启动
│
└── docs/
    ├── GbarProject.md                本文档
    ├── arch-core.md                  Arch 核心概念整理
    └── arch-systems.md               Arch.Extended 扩展整理
```

## 架构图

```
GameServer（后台单线程主循环，默认 20 tick/s）
│
├─ World                          所有实体与组件
│
├─ UpdateSystemGroup（变步长，delta = 实测流逝时间，每帧恰好 1 次）
│   ├─ OnUpdate 事件
│   ├─ BeforeUpdate → Update → AfterUpdate
│   │     └─ 各系统 [Query(Parallel=true)] 静态查询
│   │           ├─ 并行计算：JobScheduler 按 chunk 分发到 worker 线程
│   │           └─ 结构变更：写入组内 Commands（不立即执行）
│   └─ Commands.Playback(world)   ← 帧末单线程统一回放
│
└─ FixedUpdateSystemGroup（定步长，delta = 1/TickRate，累加器补帧）
    ├─ accumulator += delta
    ├─ while accumulator >= fixedDelta（单帧最多 5 次）:
    │     ├─ OnFixedUpdate 事件
    │     ├─ BeforeUpdate → Update → AfterUpdate
    │     └─ Commands.Playback(world)   ← 每次步进后立即回放
    └─ 补不过来则丢弃积压，防死亡螺旋
```

## 核心原则

1. **计算与结构变更分离**
   - 纯计算（`ref` 写组件）→ 直接在并行查询中执行
   - 结构变更（`Create`/`Destroy`/`Add`/`Remove`）→ 只记录到 `CommandBuffer`，统一回放

2. **系统之间严格顺序，查询内部并行**
   - 系统按顺序在单线程上依次执行，无系统间竞态
   - 单个查询的实体遍历按 chunk 分发到多线程（`ParallelQuery` 阻塞等待完成）

3. **所有权清晰**
   - `World` 属于 `GameServer`
   - `CommandBufferHub` 属于各自的 `SystemGroup`
   - 系统基类从组获取 Hub 并向组注册，无中间转发层

## 模块说明

### `GameServer`

| 成员 | 说明 |
|------|------|
| `World` | ECS 世界 |
| `UpdateSystems` / `FixedSystems` | 两个系统组（各自持有 Commands） |
| `RegisterSystems()` | 反射扫描程序集，自动实例化注册所有系统子类 |
| `OnUpdate` / `OnFixedUpdate` | 每帧事件，供非 ECS 代码挂入节拍 |
| `Initialize()` → `Start()` → `StopAsync()` | 生命周期 |
| `TickRate` / `Tick` | 每秒逻辑帧数（默认 20）/ 已运行帧数 |

`JobScheduler` 在 `Loop` 线程内创建并赋给 `World.SharedJobScheduler`（`Schedule` 只允许从创建线程调用），循环退出后同线程回收。

### `UpdateSystemGroup` / `FixedUpdateSystemGroup`

继承 `Group<float>`，生命周期可 override 自定义；持有 `Commands`（`CommandBufferHub`），`Dispose` 用 `new` 隐藏以连带释放（`Group.Dispose` 非 virtual）。

### `FixedSystem` / `UpdateSystem`（`Systems/Base/GbarBaseSystem.cs`）

系统自注册基类：

```csharp
protected FixedSystem(GameServer server) : base(server.World)
{
    Commands = server.FixedSystems.Commands;  // 静态字段，供并行查询的 static 方法使用
    server.FixedSystems.Add(this);            // 构造即注册
}
```

### 系统模板（`MovementSystem`）

```csharp
[SystemOrder(0)]
public sealed partial class MovementSystem : FixedSystem
{
    public MovementSystem(GameServer server) : base(server) { }

    [Query(Parallel = true)]
    [All<Position, Velocity>]
    private static void Move([Data] in float delta, Entity entity, ref Position position, in Velocity velocity)
    {
        position.X += velocity.X * delta;   // 计算：直接写组件
        position.Y += velocity.Y * delta;

        if (Math.Abs(position.X) > Bound || Math.Abs(position.Y) > Bound)
            Commands.Current.Destroy(in entity);   // 结构变更：只记录不执行
    }
}
```

规范要点：
- `partial` 类 + `[Query]` 静态方法，`Update` 由源码生成器生成
- 并行查询方法必须 `static`，Hub 经基类静态字段 `Commands` 访问
- `[Data]` 参数接收组传入的 delta；组件参数 `ref`（可写）/ `in`（只读）

### 启动入口（`Program.cs`）

```csharp
var server = new GameServer(tickRate: 20);
server.RegisterSystems();   // 反射自动注册所有系统（[SystemOrder] 控制顺序）
server.Initialize();
server.Start();
```

## 执行模型

### 主循环（`GameServer.Loop`）

- **同步阻塞**等待 `PeriodicTimer` 节拍——整个循环（含 JobScheduler 创建/调度/销毁）钉在同一线程，避免 `await` 续体换线程导致 `Schedule` 异常
- 每拍：秒表实测 delta → update 组一次 → 累加器驱动 fixed 组 0~N 次（慢帧补帧，上限 5 次）
- `CancellationToken` 取消后抛出 `OperationCanceledException`，捕获退出循环

### 线程安全约束

| 操作 | 并行查询中 | 说明 |
|------|-----------|------|
| 读写实体自己的组件 | ✅ 安全 | 天然隔离 |
| 写 `Commands.Current` | ✅ 安全 | CommandBuffer 内部线程安全 |
| 访问共享集合/计数器 | ⚠️ 需自行同步 | 避免或加锁 |
| 直接结构变更 | ❌ 禁止 | 一律走 CommandBuffer |

## 性能说明

- 并行查询有调度开销，实体量小（几百）时单线程更快；数万实体以上才有明显收益
- 20 tick/s 下每帧预算 50ms，应监控单帧耗时
- 慢帧由累加器自动补帧；严重卡顿丢弃积压防死亡螺旋
