# ThinServer：Gbar.Net 服务器端的最小业务循环测试。
#
# 职责：
#   1. 启动 NetServer 监听端口，网络事件由 LiteNetLib 后台轮询线程写入事件队列；
#   2. 主线程以固定 TickRate 驱动业务循环：先排空事件队列（处理连接/收包/断开），
#      再推进世界状态并向所有客户端广播 Keyframe 快照；
#   3. 按协议手动解码 C→S 三种包（Hello/Ack/Bye）。
#
# 协议约定（见 Gbar.Net/Protocol/Packets.cs 注释）：
#   C→S: Hello（版本协商）→ S→C: Welcome（分配身份）→ 常态 S→C Keyframe/Delta，C→S Ack 回执。
#
# 解码说明：PacketCodec.TryDispatch 的参数是 C# 的 ReadOnlySpan<byte>，
# pythonnet 无法把 byte[] 隐式转为 Span（客户端无此问题：TryDispatch 由 C# 侧
# NetClient 内部调用，Python 只提供 IPacketHandler 回调）。
# 而服务器从事件队列拿到的是原始 byte[]，因此这里按封包格式手动解码：
#   [ PacketType: 1 byte ][ MemoryPack 序列化包体 ]
#   HelloPacket/AckPacket 仅含一个 int32（小端）；ByePacket 为 MemoryPack 字符串
#   （int32 字符数 + UTF-16LE 数据，-1 表示 null）。
import os
import struct
import time
from pathlib import Path

BASE = Path(__file__).resolve().parent
DLL_DIR = BASE.parent / "Gbar.Net" / "bin" / "Debug" / "net10.0"

# 让 CoreCLR 能从 DLL 目录解析 LiteNetLib / MemoryPack 等依赖
os.environ["PATH"] = str(DLL_DIR) + os.pathsep + os.environ["PATH"]

from clr_loader import get_coreclr
from pythonnet import set_runtime

# Gbar.Net 目标框架为 net10.0，必须用 runtimeconfig.json 指定运行时
set_runtime(get_coreclr(runtime_config=str(BASE / "runtimeconfig.json")))

import clr

clr.AddReference(str(DLL_DIR / "Gbar.Net.dll"))

from System import Array  # type: ignore[import]
from Gbar.Net.Server import NetEvent, NetServer, NetEventKind  # type: ignore[import]
from Gbar.Net.Protocol import (  # type: ignore[import]
    EntityState,
    KeyframePacket,
    PacketType,
    WelcomePacket,
)

PORT = 9050
MAX_CONNECTIONS = 128  # 最大连接数（压测 100 客户端需大于 100）
TICK_RATE = 10  # 业务循环频率（Hz）
PLAYER_NET_ID_BASE = 100  # 玩家本体实体的 NetId 起点

# PacketType 枚举值（见 PacketType.cs），C→S 方向只出现这三种
TYPE_HELLO = int(PacketType.Hello)
TYPE_ACK = int(PacketType.Ack)
TYPE_BYE = int(PacketType.Bye)


def decode_packet(data):
    """手动解码客户端封包，返回 (包类型, 负载)。

    对应 C# PacketCodec.TryDispatch 的 Hello/Ack/Bye 三个分支。
    """
    if len(data) < 1:
        return None, None
    ptype = int(data[0])
    # .NET byte[] 在 pythonnet 中不支持切片，先迭代转成 Python 列表
    body = bytes(list(data)[1:])
    if ptype == TYPE_HELLO:
        # HelloPacket.ProtocolVersion: int32 小端
        return ptype, struct.unpack_from("<i", body)[0]
    if ptype == TYPE_ACK:
        # AckPacket.LastAppliedTick: int32 小端
        return ptype, struct.unpack_from("<i", body)[0]
    if ptype == TYPE_BYE:
        # MemoryPack 字符串：int32 字符数 + UTF-16LE
        count = struct.unpack_from("<i", body)[0]
        reason = "" if count < 0 else body[4 : 4 + count * 2].decode("utf-16-le")
        return ptype, reason
    return ptype, None


def handle_packet(server, cid, data, tick, last_ack):
    """处理一个客户端封包：按首字节识别类型，解码后执行对应业务。"""
    ptype, payload = decode_packet(data)
    if ptype == TYPE_HELLO:
        # 版本协商：不符直接断开；通过则回 Welcome 分配身份
        print(f"[S] Hello from client {cid}, protocol v{payload}")
        if payload != NetServer.ProtocolVersion:
            server.Disconnect(cid, "protocol mismatch")
            return
        welcome = WelcomePacket()
        welcome.ClientId = cid
        welcome.TickRate = TICK_RATE
        welcome.ServerTick = tick
        welcome.PlayerNetId = PLAYER_NET_ID_BASE + cid
        server.Send[WelcomePacket](cid, PacketType.Welcome, welcome)
        print(f"[S] -> Welcome cid={cid}, playerNetId={welcome.PlayerNetId}")
    elif ptype == TYPE_ACK:
        # 记录已应用 tick（真实业务中是生成 Delta 的基线，这里只打印）
        last_ack[cid] = payload
        # print(f"[S] Ack from client {cid}: tick {payload}")
    elif ptype == TYPE_BYE:
        print(f"[S] Bye from client {cid}: {payload}")
    else:
        print(f"[S] unknown packet type {ptype} from client {cid}")


def send_keyframe(server, cid, tick, pos_x, pos_y, vel_x=0.0, vel_y=0.0):
    """把单个实体状态打包成 KeyframePacket 并发送给指定客户端。"""
    entity = EntityState()
    entity.NetId = PLAYER_NET_ID_BASE + cid
    entity.PosX, entity.PosY = pos_x, pos_y
    entity.VelX, entity.VelY = vel_x, vel_y
    keyframe = KeyframePacket()
    keyframe.Tick = tick
    keyframe.Entities = Array.CreateInstance(EntityState, 1)
    keyframe.Entities[0] = entity
    server.Send[KeyframePacket](cid, PacketType.Keyframe, keyframe)


def main():
    server = NetServer()
    server.Start(PORT, MAX_CONNECTIONS)
    print(f"[S] listening on port {PORT}, tick rate {TICK_RATE}Hz")

    tick = 0
    clients = set()  # 已连接客户端 ID 集合（由连接/断开事件维护）
    last_ack = {}  # clientId -> 该客户端已应用的最新 tick（收到过 Ack 即视为握手完成）

    tick_interval = 1.0 / TICK_RATE

    try:
        while True:  # 无限运行，Ctrl+C 退出
            loop_start = time.monotonic()
            tick += 1

            # 1) 排空网络事件队列（ConcurrentQueue 线程安全，跨线程传递）
            #    pythonnet 的 out 参数：传入占位实例，返回 (bool, out值) 元组
            while True:
                ok, ev = server.Events.TryDequeue(NetEvent.Connected(0))
                if not ok:
                    break
                if ev.Kind == NetEventKind.Connected:
                    clients.add(ev.ClientId)
                    print(f"[S] client {ev.ClientId} connected")
                elif ev.Kind == NetEventKind.Disconnected:
                    clients.discard(ev.ClientId)
                    last_ack.pop(ev.ClientId, None)
                    print(f"[S] client {ev.ClientId} disconnected: {ev.Reason}")
                else:  # Packet：按首字节识别包类型后解码分发
                    handle_packet(server, ev.ClientId, ev.Data, tick, last_ack)

            # 2) 推进世界状态：玩家实体沿直线匀速运动
            pos = tick * 0.1
            # 3) 向已完成握手的客户端广播 Keyframe 快照
            #    （真实业务中常态应发 Delta 增量，这里简化演示每 tick 全量快照）
            for cid in clients:
                if cid not in last_ack:
                    continue  # 尚未完成握手（没收到过 Ack）的客户端不发快照
                send_keyframe(server, cid, tick, pos, 0.0)

            # 4) 固定频率休眠，维持稳定 tick 间隔
            elapsed = time.monotonic() - loop_start
            time.sleep(max(0.0, tick_interval - elapsed))
    except KeyboardInterrupt:
        pass
    finally:
        server.Stop()
        print("[S] stopped")


if __name__ == "__main__":
    main()
