# ThinClinet：Gbar.Net 客户端的最小业务循环测试。
#
# 流程（与 ThinServer 配对使用，先启动 ThinServer.py）：
#   1. NetClient.Connect 建立连接（LiteNetLib 后台轮询线程处理网络事件）；
#   2. SendHello 发起版本协商；
#   3. 服务器回 Welcome 后进入常态循环：收 Keyframe 快照 → 应用 → SendAck 回执；
#   4. 结束测试时 Disconnect 优雅断开（发送 Bye 包）。
#
# pythonnet 说明：
#   - clr_loader + runtimeconfig.json 加载 .NET 10 CoreCLR 运行时（Gbar.Net 目标为 net10.0）；
#   - Gbar.Net.* 命名空间由 pythonnet 在运行时从 DLL 动态生成，
#     Pylance 静态分析看不到属于正常现象，故 import 行标记 type: ignore；
#   - C# 带 in/ref/out 参数的泛型方法在 Python 中返回 (返回值, 修改后的参数) 元组。
import os
import time
from pathlib import Path

BASE = Path(__file__).resolve().parent
DLL_DIR = BASE.parent / "Gbar.Net" / "bin" / "Debug" / "net10.0"

# 让 CoreCLR 能从 DLL 目录解析 LiteNetLib / MemoryPack 等依赖
os.environ["PATH"] = str(DLL_DIR) + os.pathsep + os.environ["PATH"]

from clr_loader import get_coreclr
from pythonnet import set_runtime

set_runtime(get_coreclr(runtime_config=str(BASE / "runtimeconfig.json")))

import clr

clr.AddReference(str(DLL_DIR / "Gbar.Net.dll"))

from Gbar.Net.Client import NetClient  # type: ignore[import]
from Gbar.Net.Protocol import IPacketHandler  # type: ignore[import]

HOST, PORT = "127.0.0.1", 9050


class ClientPacketHandler(IPacketHandler):
    """客户端包处理器（Python 实现 .NET 接口）。

    注意：回调在 NetClient 的后台轮询线程上执行，这里只做轻量记录，
    真正的状态应用放在主循环里做（简化起见测试直接记录）。
    """

    __namespace__ = "ThinClinet"

    def __init__(self):
        super().__init__()
        self.welcome = None  # 收到的 Welcome 包（握手完成标志）
        self.last_applied_tick = 0  # 已应用的最新快照 tick
        self.player_net_id = 0

    def OnWelcome(self, packet):
        """握手完成：记录服务器分配的身份。"""
        self.welcome = packet
        self.player_net_id = packet.PlayerNetId
        self.last_applied_tick = packet.ServerTick
        print(
            f"[C] Welcome: cid={packet.ClientId}, tickRate={packet.TickRate},"
            f" serverTick={packet.ServerTick}, playerNetId={packet.PlayerNetId}"
        )

    def OnKeyframe(self, packet):
        """应用全量快照：更新已应用 tick，打印玩家实体状态。"""
        self.last_applied_tick = packet.Tick
        for entity in packet.Entities:
            if entity.NetId == self.player_net_id:
                print(f"[C] Keyframe tick={packet.Tick}: player pos=({entity.PosX:.1f}, {entity.PosY:.1f})")

    def OnDelta(self, packet):
        """应用增量包：校验基线连续（BaseTick 必须等于本地已应用 tick），否则丢弃等 Keyframe。"""
        if packet.BaseTick != self.last_applied_tick:
            print(f"[C] Delta tick={packet.CurrTick} 基线不连续，丢弃")
            return
        self.last_applied_tick = packet.CurrTick

    def OnBye(self, packet):
        print(f"[C] Bye: {packet.Reason}")

    # 以下为 C→S 方向的包，客户端不关心，但接口方法为抽象成员必须全部实现
    def OnHello(self, packet):
        pass

    def OnAck(self, packet):
        pass


def connect_and_handshake(client, handler, timeout=5.0):
    """连接服务器并等待握手完成（发送 Hello → 收到 Welcome）。

    轮询等待是因为 UDP 建连耗时不定；超时返回 False。
    """
    client.Connect(HOST, PORT)
    deadline = time.monotonic() + timeout
    while not client.IsConnected and time.monotonic() < deadline:
        time.sleep(0.1)
    if not client.IsConnected:
        print("[C] 连接失败，请先启动 ThinServer.py")
        return False
    client.SendHello()
    while handler.welcome is None and time.monotonic() < deadline:
        time.sleep(0.05)  # 等待服务器回 Welcome
    if handler.welcome is None:
        print("[C] 握手超时")
        return False
    return True


def run_loop(client, handler, duration=8.0):
    """常态业务循环：快照由回调线程应用，主循环按固定间隔回执 Ack。"""
    end_time = time.monotonic() + duration
    while time.monotonic() < end_time:
        # 已应用最新快照，向服务器回执（真实业务按固定间隔限频发送，这里每次循环发一次）
        client.SendAck(handler.last_applied_tick)
        time.sleep(0.2)


def main():
    client = NetClient()
    handler = ClientPacketHandler()
    client.Handler = handler

    # C# event 在 pythonnet 中用 += 订阅；回调同样运行在后台轮询线程
    client.Connected += lambda: print("[C] connected")
    client.Disconnected += lambda reason: print(f"[C] disconnected: {reason}")

    if not connect_and_handshake(client, handler):
        return
    run_loop(client, handler)

    client.Disconnect("test done")
    time.sleep(0.3)  # 留出时间让 Bye 包发出
    client.Stop()
    print("[C] stopped")


if __name__ == "__main__":
    main()
