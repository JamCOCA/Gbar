"""客户端壳：NetClient 生命周期封装（连接 → 握手 → 常态 → 断开）。"""

import logging
import time

from Gbar.Net.Client import NetClient  # type: ignore[import]

from .config import HOST, PORT
from .events import ClientEvents
from .handler import ClientPacketHandler
from .recorder import PacketLogger
from .state import WorldState

log = logging.getLogger("ThinClient")


class ThinClient:
    def __init__(self, host=HOST, port=PORT, events: ClientEvents | None = None):
        self.host, self.port = host, port
        self.events = events or ClientEvents()
        self.state = WorldState()
        self.packets = PacketLogger()
        self.handler = ClientPacketHandler(self.state, self.events, self.packets)
        self.net = NetClient()
        self.handler.net = self.net
        self.net.Handler = self.handler
        # C# event 在 pythonnet 中用 += 订阅；回调运行在后台轮询线程
        self.net.Connected += self._on_connected
        self.net.Disconnected += self._on_disconnected

    @property
    def is_connected(self) -> bool:
        return bool(self.net.IsConnected)

    def connect(self, timeout=5.0, retries=3) -> bool:
        """连接服务器并完成握手（Hello → Welcome）。失败重试，全部超时返回 False。"""
        for attempt in range(retries):
            self.net.Connect(self.host, self.port)
            deadline = time.monotonic() + timeout
            while not self.is_connected and time.monotonic() < deadline:
                time.sleep(0.1)
            if self.is_connected:
                break
            log.warning("第 %d 次连接超时，重试", attempt + 1)
            self.net.Stop()
        else:
            log.error("连接失败，请先启动服务器")
            return False
        log.info("C→S Hello")
        self.packets.record(0, "C→S", "Hello", "")
        self.net.SendHello()
        if not self.handler.handshake_done.wait(timeout):
            log.error("握手超时")
            return False
        return True

    def disconnect(self, reason="test done"):
        log.info("C→S Bye reason=%s", reason)
        self.packets.record(self.state.snapshot().tick, "C→S", "Bye", f"reason={reason}")
        self.net.Disconnect(reason)
        time.sleep(0.3)  # 留出时间让 Bye 包发出
        self.net.Stop()
        self.packets.close()
        log.info("stopped")

    # ---- NetClient 事件转发 ----

    def _on_connected(self):
        log.info("connected")
        self.events.emit(self.events.on_connected)

    def _on_disconnected(self, reason):
        log.info("disconnected: %s", reason)
        self.events.emit(self.events.on_disconnected, reason)
