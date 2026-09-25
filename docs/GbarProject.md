# Gbar 项目架构

基于 [Arch](https://arch-ecs.gitbook.io/arch) ECS 框架的游戏服务器。核心设计：**查询内并行计算 + 结构变更延迟回放**。

相关文档：[GbarMotion.md](GbarMotion.md)（坐标/物理/运动方案）、[GbarSystems.md](GbarSystems.md)（系统清单与查询）、[GbarNet.md](GbarNet.md)（网络同步）、[Arch/arch-core.md](Arch/arch-core.md) / [Arch/arch-systems.md](Arch/arch-systems.md)（Arch 框架参考）

## 目录结构

```
Gbar/
├── Gbar.Core/                        服务器核心库（ECS）
│   ├── GameServer.cs                 调度核心：持有 World、驱动主循环、StartNet
│   ├── CommandBufferHub.cs           CommandBuffer 持有包装（线程安全写入）
│   ├── Components/                   组件定义（一组件一文件，按功能分二级目录）
│   │   ├── Spatial/                  坐标域：Global/Local Position/Velocity + ParentRef
│   │   ├── Physics/                  物理：Mass、Force
│   │   ├── Celestial/                天体：Celestial（引力/自转）、Orbit（公转根数）
│   │   └── Identity/                 标识：NetworkId、PlayerTag
│   ├── Maths/                      定点与确定性数学：Fix.cs（long，1:100）、DetTrig.cs（只用 +−×÷ 的确定性三角函数）
│   ├── Logging/                      日志：Log + ISink + FileSink/LevelFilterSink
│   ├── Net/ClientRegistry.cs         连接会话 + NetId 分配（仅主循环线程访问）
│   ├── Net/NetServer.cs              LiteNetLib 服务器封装 + 事件队列 + 分片发送/重组
│   ├── SystemGroup/                  变步长/定步长两个系统组（各持有 Commands）
│   └── Systems/
│       ├── Base/                     FixedSystem / UpdateSystem 自注册基类 + SystemOrder 特性
│       ├── OrbitalSystem.cs          天体公转/自转历元解析（定步长 Order 10，DetTrig 确定性开普勒求解）
│       ├── MoveSystem.cs             位置积分（定步长 Order 60，唯一位置写入点）
│       ├── NetServerSystem.cs        握手/连接生命周期（变步长 Order 0）
│       └── NetSyncSystem.cs          快照/增量同步（定步长 Order 120）
│
├── Gbar.Net/                         协议层（服务器/客户端共享的线上字节标准，不依赖 Gbar.Core）
│   ├── Client/NetClient.cs           客户端传输封装（pythonnet thinclient 使用；Godot 客户端生命周期自行实现）
│   └── Protocol/                     协议编解码 + 分发
│       ├── Packets/                  包体结构（命名空间 Gbar.Net.Protocol.Packets）
│       │   ├── EntityState.cs        实体完整状态（56 B）
│       │   ├── EntityDelta.cs        字段级增量 + EntityFields 脏掩码
│       │   ├── CelestialState.cs     天体静态定义
│       │   └── *.cs                  各包：Galaxy/Hello/Welcome/Keyframe/Delta/Ack/Bye
│       ├── Packets.cs                序列化 formatter（EntityDelta 字段掩码）
│       ├── PacketCodec.cs            编解码 + 分发 + 通道选择 + 压缩/分片
│       ├── PacketType.cs             包类型枚举
│       ├── ProtocolInfo.cs           协议版本号等标准常量
│       ├── IPacketHandler.cs         分发回调接口（C#/pythonnet 共享）
│       ├── PacketCompressor.cs       包级压缩钩子（默认不压缩）
│       └── FragmentAssembler.cs      分片重组器（按 MessageId 顺序累积）
│
├── Gbar.App/                         启动入口：创建服务器、注册系统、接线
│   ├── Program.cs                    入口（new App → Init → Debug → Start → Quit）
│   ├── App.cs                        生命周期封装 + 调试注入点
│   └── Logging/ConsoleSink.cs        控制台日志 Sink
│
├── Gbar.Python/                      Python 测试客户端（pythonnet 加载 Gbar.Net）
│   ├── ThinClient.py                 入口（带 UI / --no-ui 无界面压测）
│   └── thinclient/                   client/handler/state/recorder/ui/events/config
│
└── docs/                             文档（含 Arch/ 框架参考）
```

## 核心原则

1. **计算与结构变更分离**：纯计算（`ref` 写组件）直接在并行查询中执行；结构变更（Create/Destroy/Add/Remove）只记录到 `CommandBuffer`，帧末统一回放。
2. **系统之间严格顺序，查询内部并行**：系统按 `[SystemOrder]` 在单线程依次执行；单个查询内按 chunk 分发到多线程。
3. **所有权清晰**：`World` 属于 `GameServer`，`CommandBufferHub` 属于各系统组，系统基类构造时自注册，无中间转发层。
4. **数值类型约定**：`Position` 是唯一使用 `double` 的组件（大世界坐标需双精度）；其余组件一律 `float`（连续物理量：速度 / 旋转）或 `int`（计数 / 身份 / 生命 / CPC 储量）。

## 主循环

`GameServer.Loop` 在后台单线程运行，默认 20 tick/s，`PeriodicTimer` 同步阻塞等节拍：

- 每拍：实测 delta → 变步长组 1 次（`OnUpdate` → `BeforeUpdate`/`Update`/`AfterUpdate`）→ `Commands.Playback` 回放 → 累加器驱动定步长组 0~N 次（慢帧补帧，上限 5 次，补不过来丢弃防死亡螺旋）
- 每次固定步进结束立即 `Commands.Playback(world)` 回放结构变更
- `JobScheduler` 在 Loop 线程创建/销毁，赋给 `World.SharedJobScheduler`（Arch 的 ParallelQuery 依赖它，且只能在创建线程调用）
- `StopAsync` 先取消循环并等待退出，再断网（保证无线程竞争）。持久化已移除，后续重新设计（Entity 引用需按 NetworkId 重映射）

## 系统写法规范

```csharp
[SystemOrder(60)]
public sealed partial class MoveSystem : FixedSystem
{
    public MoveSystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        MoveGlobalQuery(World);   // 手动调用源码生成器生成的查询入口（并行）
        MoveLocalQuery(World);
    }

    [Query(Parallel = true)]
    [All<GlobalPosition, GlobalVelocity>]
    private static void MoveGlobal(ref GlobalPosition position, in GlobalVelocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;
        position.Z += velocity.Z;
    }
}
```

- `partial` 类 + `[Query]` 静态方法，源码生成器生成 `<方法名>Query(World)` 入口，**需在 `Update` 里手动调用**（生成器不自动挂进 `Update`）
- `[All<T>]`/`[Any<T>]`/`[None<T>]` 定义查询过滤器；方法签名决定传入的组件；`Parallel = true` 启用并行调度
- `FixedSystem`/`UpdateSystem` 基类构造时自注册；`RegisterSystems()` 反射扫描并实例化
- 并行查询方法必须 `static`；结构变更经基类静态字段 `Commands` 记录，禁止在查询内直接 Create/Destroy
- 20 tick/s 下每帧预算 50ms；并行查询有调度开销，实体量小（几百）时不必开 `Parallel`

## 启动流程

```csharp
var app = new App();
await app.InitAsync();   // 配置日志 → GameServer(20) → RegisterSystems → StartNet(9050) → Initialize
app.Debug();             // 调试注入点：主循环启动前直接写 World 注入测试实体
app.Start();             // 启动主循环 + 状态汇报任务
await app.WaitForQuitAsync();
await app.QuitAsync();   // StopAsync → 释放
```

## 网络同步要点

- 服务器权威，新连接**关键帧对齐一次**，之后每 tick 收**增量（Delta）**（详见 [GbarNet.md](GbarNet.md)）
- `Delta.Updated` 为字段级增量：`EntityDelta` 按 `EntityFields` 掩码只发变化字段，由 `Packets.cs` 里的 formatter 手写序列化
- 分片/压缩在传输层统一处理（`PacketCodec` + `FragmentAssembler` + `PacketCompressor`），业务协议无感知
