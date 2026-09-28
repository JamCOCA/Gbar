# Gbar 项目架构

基于 [Arch](https://arch-ecs.gitbook.io/arch) ECS 框架的游戏服务器。核心设计：**查询内并行计算 + 结构变更延迟回放**。

相关文档：[GbarSystems.md](GbarSystems.md)（系统清单与查询）、[GbarNet.md](GbarNet.md)（网络同步）、[Arch/arch-core.md](Arch/arch-core.md) / [Arch/arch-systems.md](Arch/arch-systems.md)（Arch 框架参考）；线格式与新增包细节见 `Gbar.Net/Protocol/README.md`

## 目录结构

```
Gbar/
├── Gbar.Core/                        服务器核心库（ECS）
│   ├── GameServer.cs                 调度核心：持有 World、驱动主循环、StartNet
│   ├── CommandBufferHub.cs           CommandBuffer 持有包装（线程安全写入）
│   ├── EntityFactory.cs              实体工厂：统一装配常见实体组件（CreatePlayer/CreateSurfaceWanderer/CreateStaticEntity）
│   ├── Components/                   服务器本地组件（一组件一文件，按功能分二级目录；不同步）
│   │   ├── Identity/                 标识：NetworkId、PlayerTag、EntityTypeTag
│   │   ├── Input/                    输入：MoveInput（移动意图，latest-wins）
│   │   └── Spatial/                  空间：AoiCell（AOI 网格 cell）
│   ├── Logging/                      日志：Log + ISink + FileSink/LevelFilterSink
│   ├── Net/                          网络传输（依赖 ECS，留在 Core）
│   │   ├── ClientRegistry.cs         连接会话 + NetId 分配（仅主循环线程访问）
│   │   ├── NetServer.cs              LiteNetLib 服务器封装 + 事件队列 + 分片发送/重组
│   │   └── NetEvent.cs               网络事件（Connected/Packet/Disconnected）
│   ├── Service/AuthService.cs        注册/登录认证服务
│   ├── Persistence/                  持久化（Database/ + Users/，含 PasswordHasher）
│   ├── SystemGroup/                  变步长/定步长两个系统组（各持有 Commands）
│   └── Systems/
│       ├── Base/                     FixedSystem / UpdateSystem 自注册基类 + SystemOrder 特性
│       ├── NetServerSystem.cs        收包/握手/玩家生命周期（变步长 Order 0）
│       ├── MoveInputSystem.cs        玩家输入 → 速度（定步长 Order 50）
│       ├── MovementSystem.cs         运动学积分，唯一位置写入点（定步长 Order 60）
│       ├── AoiSystem.cs              按坐标写 AOI cell 标签（定步长 Order 100）
│       └── NetSyncSystem.cs          快照/增量同步（定步长 Order 120）
│
├── Gbar.Net/                         协议层（服务器/客户端共享的线上字节标准，不依赖 Gbar.Core）
│   └── Protocol/                     协议编解码 + 分发
│       ├── PacketAttribute.cs        [Packet] 特性（包元数据唯一事实源）
│       ├── SyncComponentAttribute.cs [SyncComponent] 标记可同步组件（源生成器消费）
│       ├── PacketType.cs             包类型枚举（首字节）
│       ├── ProtocolInfo.cs           协议版本号等标准常量
│       ├── Packets.cs                组件数组的自定义 MemoryPack formatter
│       ├── Components/               可同步组件 struct（Position/Velocity/Rotation/Health/Cpc）
│       ├── Entities/                 实体同步数据定义
│       │   ├── EntityType.cs         实体类型枚举
│       │   ├── IComponentCodec.cs    组件编解码器接口（实现由生成器产出）
│       │   └── State/                EntitySpawn（全量）+ EntityUpdate（组件级增量）
│       ├── Packets/                  各包体结构体（Hello/Welcome/Auth/AuthFail/Keyframe/Delta/Ack/Bye/MoveInput）
│       ├── Tools/                    功能/管线类
│       │   ├── PacketCodec.cs        序列化 + 压缩 + 分片 + 解压入口
│       │   ├── PacketDispatcher.cs   接收侧分发（字典查表，无 switch）
│       │   ├── PacketMeta.cs         从 [Packet] 读取元数据的静态泛型缓存
│       │   ├── PacketCompressor.cs   压缩器接口 + 默认不压缩实现
│       │   └── FragmentAssembler.cs  分片重组器（按 MessageId 顺序累积）
│       └── README.md                 协议说明（封包格式 / 新增包 / 新增可复制组件）
│
├── Gbar.App/                         启动入口：创建服务器、注册系统、接线
│   ├── Program.cs                    入口（new App → Init → Debug → Start → Quit）
│   ├── App.cs                        生命周期封装 + 调试注入点
│   └── Logging/ConsoleSink.cs        控制台日志 Sink
│
├── Gbar.Sync.Generator/              源生成器：扫描 [SyncComponent]，产出组件 ID / ComponentEntry / codec / SyncReader
│   └── SyncComponentGenerator.cs
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
- `StopAsync` 先取消循环并等待退出，再断网（保证无线程竞争）。持久化目前仅覆盖账号（`AuthService` + `Persistence`）；实体/世界状态持久化后续再设计（Entity 引用需按 NetworkId 重映射）

## 系统写法规范

```csharp
[SystemOrder(60)]
public sealed partial class MovementSystem : FixedSystem
{
    public MovementSystem(GameServer server) : base(server) { }

    public override void Update(in float delta)
    {
        MoveQuery(World);   // 手动调用源码生成器生成的查询入口（并行）
    }

    [Query(Parallel = true)]
    [All<Position, Velocity>]
    private static void Move(ref Position position, in Velocity velocity)
    {
        position.X += velocity.X * _delta;
        position.Y += velocity.Y * _delta;
        position.Z += velocity.Z * _delta;
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
- 实体同步采用**组件级增量**：状态是带 `ReplicatedComponent` 标签的 `ComponentEntry[]` 标签联合，每个实体只携带它实际持有的组件；`Delta.Updated` 只发变化的组件（`EntitySpawn` 全量 / `EntityUpdate` 增量），`Delta.Despawned` 为待销毁的 NetId
- 分片/压缩在传输层统一处理（`PacketCodec` + `FragmentAssembler` + `PacketCompressor`），业务协议无感知
