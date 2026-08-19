"""注入点：测试功能通过 ClientEvents 的钩子列表挂接。"""

import logging

log = logging.getLogger("ThinClient")


class ClientEvents:
    """测试功能注入点。所有钩子均为 callable 列表，按需 append。

    触发时机（均在 NetClient 轮询线程）：
        on_connected()                  连接建立
        on_handshake(welcome_packet)    握手完成（拿到 playerNetId）
        on_galaxy(galaxy_packet)        收到星图表
        on_snapshot(WorldSnapshot)      每应用一个 Keyframe/Delta
        on_bye(reason)                  收到服务器 Bye
        on_disconnected(reason)         连接断开
    """

    def __init__(self):
        self.on_connected = []
        self.on_handshake = []
        self.on_galaxy = []
        self.on_snapshot = []
        self.on_bye = []
        self.on_disconnected = []

    @staticmethod
    def emit(hooks, *args):
        for hook in hooks:
            try:
                hook(*args)
            except Exception:
                log.exception("事件钩子 %r 执行异常", hook)
