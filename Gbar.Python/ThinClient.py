# ThinClient 入口：Gbar.Net 正式测试客户端（对端为 C# GameServer）。
#
# 实现位于 thinclient/ 包，此处仅装配与启动：
#   python ThinClient.py           默认 pyglet 3D 视图
#   python ThinClient.py --2d      旧 tkinter 2D 视图
#   python ThinClient.py --no-ui   无界面模式（压测/CI 挂钩子）
import logging
import sys
import time

from thinclient.client import ThinClient
from thinclient.config import LOG_LEVEL

logging.basicConfig(
    level=LOG_LEVEL,
    format="%(asctime)s.%(msecs)03d %(levelname)-5s %(message)s",
    datefmt="%H:%M:%S",
)


def main():
    headless = "--no-ui" in sys.argv
    use_2d = "--2d" in sys.argv
    client = ThinClient()
    if not client.connect():
        return
    try:
        if headless:
            while client.is_connected:
                time.sleep(0.5)
        elif use_2d:
            from thinclient.ui import ClientUI  # 延迟导入：无界面模式不加载 tkinter

            ClientUI(client).run()
        else:
            from thinclient.glview import GLViewer  # 延迟导入：无界面模式不加载 pyglet

            GLViewer(client).run()
    except KeyboardInterrupt:
        pass
    client.disconnect()


if __name__ == "__main__":
    main()
