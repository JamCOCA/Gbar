# Gbar 项目架构

基于 [Arch](https://arch-ecs.gitbook.io/arch) ECS 框架的游戏服务器。核心设计：**查询内并行计算 + 结构变更延迟回放**。

相关文档：[GbarMotion.md](GbarMotion.md)（坐标/物理/运动方案）、[GbarSystems.md](GbarSystems.md)（系统清单与查询）、[GbarNet.md](GbarNet.md)（网络同步）、[arch-core.md](arch-core.md) / [arch-systems.md](arch-systems.md)（Arch 框架参考）

## 目录结构

```
Gbar/
├── Gbar.Core/                        服务器核心库
│   ├── GameServer.cs                 调度核心：持有 World、驱动主循环
│   ├── CommandBufferHub.cs           CommandBuffer 持有包装（线程安全写入）
│   ├── Components/                   组件定义（一组件一文件，按功能分二级目录）
│   │   ├── Spatial/                  坐标域：Global/Local Position/Velocity + ParentRef
│   │   ├── Physics/                  物理：Mass、Force（将改 Impulse）
│   │   └── Identity/                 标识：NetworkId、PlayerTag
│   ├── Maths/Fix.cs                  定点数换算工具（long，1:100）
│   ├── SystemGroup/                  变步长/定步长两个系统组（各持有 Commands）
│   └── Systems/
│       ├── Base/                     FixedSystem / UpdateSystem 自注册基类 + SystemOrder 特性
│       └── MovementSystem.cs         系统标准模板（参考实现）
│
├── Gbar.Net/                         协议 DTO + NetServer/NetClient（LiteNetLib、MemoryPack）
├── Gbar.App/                         启动入口：创建服务器、注册系统、接线
├── Gbar.Client/                      控制台测试客户端
└── docs/
```

## 核心原则

1. **计算与结构变更分离**：纯计算（`ref` 写组件）直接在并行查询中执行；结构变更（Create/Destroy/Add/Remove）只记录到 `CommandBuffer`，帧末统一回放。
2. **系统之间严格顺序，查询内部并行**：系统按 `[SystemOrder]` 在单线程依次执行；单个查询内按 chunk 分发到多线程。
3. **所有权清晰**：`World` 属于 `GameServer`，`CommandBufferHub` 属于各系统组，系统基类构造时自注册，无中间转发层。

## 主循环

后台单线程，默认 20 tick/s，同步阻塞等 `PeriodicTimer` 节拍：

- 每拍：实测 delta → 变步长组 1 次 → 累加器驱动定步长组 0~N 次（慢帧补帧，上限 5 次，补不过来丢弃防死亡螺旋）
- 每步进结束立即 `Commands.Playback(world)` 回放结构变更
- `JobScheduler` 钉在 Loop 线程创建/销毁，赋给 `World.SharedJobScheduler`

## 系统写法规范

```csharp
[SystemOrder(0)]
public sealed partial class MovementSystem : FixedSystem
{
    public MovementSystem(GameServer server) : base(server) { }

    [Query(Parallel = true)]
    [All<Position, Velocity>]
    private static void Move([Data] in float delta, ref Position position, in Velocity velocity)
    {
        position.X += velocity.X * delta;
        position.Y += velocity.Y * delta;
    }
}
```

- `partial` 类 + `[Query]` 静态方法，`Update` 由源码生成器生成；`Program.cs` 里 `RegisterSystems()` 反射自动注册
- 并行查询方法必须 `static`；结构变更经基类静态字段 `Commands` 记录
- 线程安全：读写自己的组件安全；写 `Commands` 安全；直接结构变更禁止
- 20 tick/s 下每帧预算 50ms；并行查询有调度开销，实体量小（几百）时不必开 `Parallel`

启动入口：

```csharp
var server = new GameServer(tickRate: 20);
server.RegisterSystems();   // 反射自动注册所有系统（[SystemOrder] 控制顺序）
server.Initialize();
server.Start();
```
