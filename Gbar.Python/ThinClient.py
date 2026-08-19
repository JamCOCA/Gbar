# ThinClient 入口：Gbar.Net 正式测试客户端（对端为 C# GameServer）。
#
# 实现位于 thinclient/ 包，此处仅装配与启动：
#   python ThinClient.py           带 UI 运行
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
    client = ThinClient()
    if not client.connect():
        return
    try:
        if headless:
            while client.is_connected:
                time.sleep(0.5)
        else:
            from thinclient.ui import ClientUI  # 延迟导入：无界面模式不加载 tkinter

            ClientUI(client).run()  # 窗口关闭后返回
    except KeyboardInterrupt:
        pass
    client.disconnect()


if __name__ == "__main__":
    main()
