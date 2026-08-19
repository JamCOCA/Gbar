"""收包记录器：全量追加写 log.txt，内存保留最近若干行供 UI 显示。

每行格式：[tick=  N] 方向 包类型 内容；实体明细以缩进续行跟在包行后。
在编辑器里搜索 "tick=  6193" 即可查阅该 tick 的全部包。
仅在包回调线程写入，UI 通过 recent() 拿副本。
"""

import threading
from collections import deque

from .config import LOG_FILE, RECENT_PACKETS


class PacketLogger:
    def __init__(self, path=LOG_FILE, keep=RECENT_PACKETS):
        self._lock = threading.Lock()
        self._file = open(path, "w", encoding="utf-8")
        self._recent = deque(maxlen=keep)
        self._revision = 0  # 内容版本号，UI 据此判断是否需要重绘

    def record(self, tick: int, direction: str, ptype: str, content: str):
        self._write(f"[tick={tick:>6}] {direction} {ptype} {content}")

    def detail(self, text: str):
        """包内容的缩进续行（如实体明细），归属上一条包记录。"""
        self._write(f"          {text}")

    def _write(self, line: str):
        with self._lock:
            self._file.write(line + "\n")
            self._file.flush()
            self._recent.append(line)
            self._revision += 1

    def recent(self):
        """返回 (版本号, 行副本)。"""
        with self._lock:
            return self._revision, list(self._recent)

    def close(self):
        with self._lock:
            self._file.close()
