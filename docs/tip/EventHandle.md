# EventHandle — 事件处理速查

> ECS 线程与外部世界的交互方式总览。Arch ECS 主循环在 `ArchThread`（20 TPS）中运行，System 按 Group 顺序执行。

---

## 一、输入处理（外部 → ECS）

```
InputThread (控制台/网络) → GameInput.Queue → PlayerInputSystem → ECS 组件变更
```

| 层级 | 职责 |
|------|------|
| **InputThread** | 读取原始输入，解析为结构化数据，写入 `ConcurrentQueue` |
| **GameInput.Queue** | `ConcurrentQueue<PlayerInput>`，线程安全，两边共用 **一个** 队列 |
| **PlayerInputSystem** | ECS 线程中消费队列，按 `NetworkId` 找到 Entity，修改组件 |

```csharp
// 共享队列（放在 Gbar.Core，两边都能访问）
public static class GameInput
{
    public static readonly ConcurrentQueue<PlayerInput> Queue = new();
}
```

**注意**：`Dequeue` 后弹出的 `string` 由 GC 自动回收，不要额外缓存。

---

## 二、网络同步（ECS → 外部）

```
SyncSystem → ConcurrentQueue<SyncPacket> → NetworkThread → 客户端
```

| 层级 | 职责 |
|------|------|
| **SyncSystem** | 遍历 `SyncFlag` 脏标记，收集变化组件，打包为 `SyncPacket` |
| **SyncQueue** | `ConcurrentQueue<SyncPacket>`，ECS 线程生产，网络线程消费 |
| **NetworkThread** | 取出包，序列化，通过连接发送 |

**优化**：AOI 过滤，只同步视野内实体；Sync TPS 可低于逻辑 TPS。

---

## 三、物理查询（ECS ↔ 物理世界）

```
MoveSystem → ICollisionWorld.ValidateMove() → 返回修正位置 → 更新 Transform
```

| 场景 | 处理方式 |
|------|---------|
| 地形阻挡/路径检测 | ECS 线程内直接查询只读 `ICollisionWorld` |
| 复杂刚体模拟 | 独立物理线程 + 请求-响应（有 1 帧延迟） |

**推荐**：服务端用方案 A，`ICollisionWorld` 在 `ArchThread.OnStart` 时加载地形数据。

---

## 四、命令缓冲（CommandBuffer）

**什么时候用**：System 需要在当前帧结束时批量创建/销毁实体，或延迟修改组件。

```csharp
// System 中创建 ECB
var ecb = new CommandBuffer();

// 记录操作（不立即执行）
ecb.Add(entity, new MoveTarget(position));
ecb.Set(entity, new State(EntityState.Moving));

// 将 ECB 加入当前 Group，本帧所有 System 执行完后统一回放
SystemGroup.CommandBuffers.Add(ecb);
```

**SystemGroup 内部**：
```csharp
foreach (var system in _systems) system.Update(deltaTime);
foreach (var ecb in _commandBuffers) ecb.Playback(world, true); // 统一回放
_commandBuffers.Clear();
```

---

## 五、跨线程通信原则

| 原则 | 说明 |
|------|------|
| **单向生产-消费** | ECS 线程只消费输入、生产同步包，不直接做 I/O |
| **线程安全队列** | 只用 `ConcurrentQueue`，不要自己写锁 |
| **不要缓存引用** | `Dequeue` 后处理完就丢，避免内存泄漏 |
| **不要跨线程直接调用** | InputThread 不能直接调用 `world.Set()`，必须通过队列 |

---

## 六、SystemGroup 执行顺序

```
InputSystemGroup → PreSimulationSystemGroup → SimulationSystemGroup →
PhysicsSystemGroup → PostSimulationSystemGroup → EventSystemGroup →
SyncSystemGroup → CleanupSystemGroup
```

**MVP 最小版本**：先合并为 `InputGroup → LogicGroup → OutputGroup`（含 Sync + Cleanup）。

---

## 七、明天待办（按优先级）

1. [ ] 补全 `ArchThread.Update` 中的 `SystemGroups` 遍历
2. [ ] 创建 `GameInput.ConcurrentQueue` 和 `PlayerInput` 结构
3. [ ] 实现 `PlayerInputSystem` 消费输入队列
4. [ ] 实现 `MoveSystem` + `ICollisionWorld` 查询接口
5. [ ] 实现 `SyncSystem` + 同步队列（可先打日志）
6. [ ] 在 `ArchThread.OnStart` 中注册所有 SystemGroup

---

## 八、关键文件

| 文件 | 说明 |
|------|------|
| `Gbar.Core/ECS/GameInput.cs` | 共享输入队列（新建） |
| `Gbar.Core/ECS/Systems/PlayerInputSystem.cs` | 输入消费 System（空文件待实现） |
| `Gbar.Server/GameThread/Threads/ArchThread.cs` | ECS 主线程，需补全 Group 注册和 Update 遍历 |
| `Gbar.Core/ECS/Components/Identity.cs` | 已写，含 `NetworkId` / `Kind` / `TemplateId` |
| `Gbar.Core/ECS/SystemGroup.cs` | 已支持 CommandBuffer 回放 |
