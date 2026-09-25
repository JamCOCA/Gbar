# GbarNet 网络协议与数据包处理

服务器权威：客户端与服务器共享 `Gbar.Net.Protocol` 里的线上字节标准，两端用同一套 `PacketType` 枚举与 MemoryPack 结构体。

## 包结构

应用消息 = `[PacketType: 1 byte][MemoryPack 序列化的包体]`

- 首字节是 `PacketType`（`enum : byte`，底层值即线上字节），决定包体反序列化成哪个结构体
- 包体由 MemoryPack 序列化，见 `PacketCodec.Encode`（`Gbar.Net/Protocol/PacketCodec.cs`）

传输层统一封包管线（业务层无感知）：

```
普通帧:  [PacketType: 1][body]
压缩帧:  [0xFE: 1][PacketType: 1][compressed body]   # 超过 MinCompressBytes 且压缩有收益
分片包:  [0xFF: 1][MessageId: 2][FragIndex: 2][FragCount: 2][frame fragment]
```

## 包类型

| 包 | 方向 | 说明 |
|---|---|---|
| `Hello` | C→S | 版本协商 |
| `Auth` | C→S | 注册/登录（`AuthMode` + `Account` + `Password`） |
| `Welcome` | S→C | 认证通过后分配身份（`ClientId`/`PlayerNetId`/`ServerTick`） |
| `AuthFail` | S→C | 认证失败（`AuthFailReason`） |
| `Keyframe` | S→C | 全量快照（新连接对齐 / 周期纠偏） |
| `Delta` | S→C | 常态增量（相对已 ACK 基线） |
| `Ack` | C→S | 回执，建立同步基线 |
| `Bye` | 双向 | 优雅断开 |

## 处理流程

### 1. 网络线程收包（`NetServer`）

后台轮询线程驱动 LiteNetLib，收到数据先分片重组，再把完整帧塞进事件队列交给主循环：

```csharp
// NetServer.cs  NetworkReceiveEvent
var data = new byte[reader.AvailableBytes];
reader.GetBytes(data, data.Length);
var frame = _assembler.Accept(data);   // 分片重组，未收齐返回 null
if (frame is not null)
    _events.Enqueue(NetEvent.Packet(peer.Id, frame));
```

### 2. 主循环取包（`NetServerSystem.Update`）

变步长系统每帧从队列取出事件，按来源 ClientId 分发：

```csharp
// NetServerSystem.cs  Update (Order 0)
case NetEventKind.Packet:
    _currentClientId = ev.ClientId;
    if (ev.Data is null || !PacketCodec.TryDispatch(ev.Data, this))
        net.Disconnect(ev.ClientId, "非法封包");
    break;
```

### 3. 解帧与分发（`PacketCodec.TryDispatch` → `DispatchPlain`）

`TryDispatch` 处理压缩标志后交给 `DispatchPlain`；后者读首字节当 `PacketType`，按类型反序列化并回调接口对应方法：

```csharp
// PacketCodec.cs  DispatchPlain
var type = (PacketType)data[0];       // 首字节 = PacketType
var body = data[1..];                 // 其余 = MemoryPack 包体
switch (type)
{
    case PacketType.Auth:
        handler.OnAuth(MemoryPackSerializer.Deserialize<AuthPacket>(body));
        return true;
    // ...
    default:
        return false;                 // 未知类型 → 上层断开"非法封包"
}
```

### 4. 接口回调（`IPacketHandler`）

`handler` 是实现了 `IPacketHandler` 的对象：服务器 `NetServerSystem` 实现 C→S 方向（`OnHello`/`OnAuth`/`OnAck`/`OnBye`），客户端实现 S→C 方向（`OnWelcome`/`OnAuthFail`/`OnKeyframe`/`OnDelta`）。

以登录为例：客户端发 `0x06`（`PacketType.Auth`）+ `AuthPacket` 序列化字节 → `DispatchPlain` 命中 `case PacketType.Auth` → 调 `NetServerSystem.OnAuth(AuthPacket)` → 读 `_currentClientId` 得知来源连接。

## 握手时序

```
C ── Hello ──→ S    校验版本，标记待认证
C ── Auth ───→ S    注册/登录 → 成功分配 NetId、创建玩家实体
C ←─ AuthFail─ S    认证失败，可重试
C ←─ Welcome ─ S    认证成功，分配身份
C ←─ Keyframe ─S    全量快照（未对齐期间每 tick 重发）
C ── Ack ────→ S    建立基线（BaselineTick）
C ←─ Delta ─── S    常态增量循环
```

## 目录对应

```
Gbar.Net/Protocol/
  PacketType.cs        包类型枚举（首字节）
  PacketCodec.cs       编解码 + 分发 + 通道选择 + 压缩/分片
  IPacketHandler.cs    分发回调接口（服务器/客户端各自实现自己的方向）
  Packets/             各包结构体定义（Hello/Auth/Welcome/AuthFail/Keyframe/Delta/Ack/Bye）

Gbar.Core/
  Net/NetServer.cs           LiteNetLib 封装 + 事件队列 + 分片发送/重组
  Net/NetEvent.cs            网络事件（Connected/Packet/Disconnected）
  Net/ClientRegistry.cs      连接会话 + NetId + 握手状态
  Service/AuthService.cs     注册/登录/签发 token
  Systems/NetServerSystem.cs 握手/认证/生命周期（UpdateSystem, Order 0）
```
