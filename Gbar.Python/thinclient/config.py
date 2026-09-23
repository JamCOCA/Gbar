"""ThinClient 常量配置。"""

from pathlib import Path

HOST, PORT = "127.0.0.1", 9050
FIXED_SCALE = 100.0  # 位置/质量/冲量定点缩放 1:100（GbarMotion.md），坐标 ÷ 100 得米
VELOCITY_SCALE = 500.0  # 速度定点缩放（m/s × 500，见 GbarMotion.md 兑换公式 K = Scale²/tickRate）
LOG_LEVEL = 20  # logging.INFO；改为 logging.DEBUG 可全量观察高频包
LOG_FILE = Path(__file__).resolve().parent.parent / "log.txt"  # Gbar.Python/log.txt
UI_REFRESH_MS = 50  # UI 刷新间隔
RECENT_PACKETS = 2000  # UI 日志面板保留的最近收包行数
