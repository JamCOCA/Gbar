"""tkinter 视图层。

- 左上：俯视画布（X/Y 平面，玩家橙色高亮，速度向量箭头，视野自适应）
- 右上：实体列表（Listbox）+ Unity 式监视栏（选中实体的全部字段）
- 底部：收包日志面板（log.txt 的实时视图，支持按 tick / 关键字过滤）

主线程通过 after() 周期性读取快照重绘，与包回调线程无共享可变状态。
"""

import tkinter as tk
import time
from tkinter import ttk

from .client import ThinClient
from .config import UI_REFRESH_MS
from .state import EntityView, WorldSnapshot


class ClientUI:
    CANVAS_W, CANVAS_H = 720, 560
    LOG_H = 10  # 日志面板行数
    MIN_SPAN = 10.0  # 视野最小跨度（米），防止空场景时除以零

    def __init__(self, client: ThinClient):
        self._client = client
        self._selected_net_id = None  # 监视栏当前选中的实体
        self._entity_ids = []  # 列表行号 → NetId 映射
        self._log_revision = -1  # 已渲染的日志版本号
        self._net_last_time = time.monotonic()  # 网速采样基准时间
        self._net_last_sent = 0                  # 采样起点的累计发送字节
        self._net_last_recv = 0                  # 采样起点的累计接收字节
        self._net_up = 0.0                       # 上行速率 B/s
        self._net_down = 0.0                     # 下行速率 B/s

        self._root = tk.Tk()
        self._root.title("ThinClient")
        self._root.grid_rowconfigure(0, weight=1)
        self._root.grid_columnconfigure(0, weight=1)

        self._canvas = tk.Canvas(self._root, width=self.CANVAS_W, height=self.CANVAS_H, bg="#101418")
        self._canvas.grid(row=0, column=0, sticky="nsew")

        right = tk.Frame(self._root, bg="#181c22")
        right.grid(row=0, column=1, sticky="ns")
        self._info = tk.Label(right, justify=tk.LEFT, anchor="nw", bg="#181c22", fg="#d7dce2",
                              font=("Consolas", 10), padx=10, pady=6)
        self._info.pack(fill=tk.X)
        tk.Label(right, text="Entities", bg="#181c22", fg="#8a94a2", font=("Consolas", 9)).pack(anchor="w", padx=10)
        self._listbox = tk.Listbox(right, width=30, height=10, bg="#101418", fg="#d7dce2",
                                   font=("Consolas", 10), selectbackground="#2d3a4a",
                                   highlightthickness=0, activestyle="none")
        self._listbox.pack(fill=tk.X, padx=10)
        self._listbox.bind("<<ListboxSelect>>", self._on_select)
        tk.Label(right, text="Inspector", bg="#181c22", fg="#8a94a2", font=("Consolas", 9)).pack(anchor="w", padx=10)
        self._inspector = tk.Label(right, justify=tk.LEFT, anchor="nw", bg="#181c22", fg="#d7dce2",
                                   font=("Consolas", 10), padx=10, pady=6)
        self._inspector.pack(fill=tk.BOTH, expand=True)

        bottom = tk.Frame(self._root, bg="#181c22")
        bottom.grid(row=1, column=0, columnspan=2, sticky="ew")
        tk.Label(bottom, text="filter:", bg="#181c22", fg="#8a94a2", font=("Consolas", 9)).pack(side=tk.LEFT, padx=(10, 2))
        self._filter = tk.Entry(bottom, width=16, bg="#101418", fg="#d7dce2", insertbackground="#d7dce2",
                                font=("Consolas", 9))
        self._filter.pack(side=tk.LEFT, pady=2)
        self._filter.bind("<KeyRelease>", lambda _e: self._invalidate_log())
        self._log_text = tk.Text(bottom, height=self.LOG_H, bg="#101418", fg="#9fb2c8",
                                 font=("Consolas", 9), state=tk.DISABLED, wrap=tk.NONE)
        self._log_text.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=10, pady=(0, 6))
        scroll = ttk.Scrollbar(bottom, command=self._log_text.yview)
        scroll.pack(side=tk.RIGHT, fill=tk.Y)
        self._log_text.config(yscrollcommand=scroll.set)

    def run(self):
        self._refresh()
        self._root.mainloop()

    # ---- 刷新 ----

    def _refresh(self):
        snap = self._client.state.snapshot()
        self._draw(snap)
        self._update_info(snap)
        self._update_entity_list(snap)
        self._update_inspector(snap)
        self._update_log()
        self._root.after(UI_REFRESH_MS, self._refresh)

    # ---- 画布 ----

    def _draw(self, snap: WorldSnapshot):
        c = self._canvas
        c.delete("all")
        if not snap.entities:
            c.create_text(self.CANVAS_W / 2, self.CANVAS_H / 2, text="waiting for snapshot...",
                          fill="#5a6472", font=("Consolas", 14))
            return
        # 自适应视野：以实体包围盒 + 10% 边距映射到画布
        xs = [e.x for e in snap.entities.values()]
        ys = [e.y for e in snap.entities.values()]
        cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
        span = max(max(xs) - min(xs), max(ys) - min(ys), self.MIN_SPAN) * 1.1
        half = span / 2

        def to_px(x, y):
            px = (x - cx + half) / span * self.CANVAS_W
            py = (cy + half - y) / span * self.CANVAS_H  # 世界 Y 向上，画布 Y 向下
            return px, py

        for e in snap.entities.values():
            px, py = to_px(e.x, e.y)
            is_player = e.net_id == snap.player_net_id
            color, r = ("#ffb454", 7) if is_player else ("#4fc3f7", 5)
            if e.net_id == self._selected_net_id:
                c.create_oval(px - r - 3, py - r - 3, px + r + 3, py + r + 3, outline="#ffffff")
            c.create_oval(px - r, py - r, px + r, py + r, fill=color, outline="")
            c.create_text(px + 10, py, text=str(e.net_id), fill="#8a94a2", anchor="w", font=("Consolas", 8))
            c.create_line(px, py, *to_px(e.x + e.vx, e.y + e.vy), fill=color, arrow=tk.LAST)

    # ---- 信息与监视栏 ----

    @staticmethod
    def _entity_name(e: EntityView, player_net_id: int) -> str:
        return f"Player #{e.net_id}" if e.net_id == player_net_id else f"Entity #{e.net_id}"

    NET_SAMPLE_S = 0.5  # 网速采样窗口（秒），低于此间隔沿用上一采样值

    @staticmethod
    def _fmt_rate(bps: float) -> str:
        if bps >= 1_048_576:
            return f"{bps / 1_048_576:.2f} MB/s"
        if bps >= 1024:
            return f"{bps / 1024:.2f} KB/s"
        return f"{bps:.1f} B/s"

    def _update_info(self, snap: WorldSnapshot):
        now = time.monotonic()
        elapsed = now - self._net_last_time
        if elapsed >= self.NET_SAMPLE_S:
            sent = self._client.net.BytesSent
            recv = self._client.net.BytesReceived
            self._net_up = (sent - self._net_last_sent) / elapsed
            self._net_down = (recv - self._net_last_recv) / elapsed
            self._net_last_time = now
            self._net_last_sent = sent
            self._net_last_recv = recv
        self._info.config(
            text=(
                f"tick      {snap.tick}\n"
                f"entities  {len(snap.entities)}\n"
                f"player    netId={snap.player_net_id}\n"
                f"up        {self._fmt_rate(self._net_up)}\n"
                f"down      {self._fmt_rate(self._net_down)}"
            )
        )

    def _update_entity_list(self, snap: WorldSnapshot):
        ids = sorted(snap.entities)
        if ids == self._entity_ids:
            return  # 列表无变化，避免重绘打断选择
        self._entity_ids = ids
        self._listbox.delete(0, tk.END)
        for net_id in ids:
            self._listbox.insert(tk.END, self._entity_name(snap.entities[net_id], snap.player_net_id))
        if self._selected_net_id in ids:
            self._listbox.selection_set(ids.index(self._selected_net_id))

    def _on_select(self, _event):
        sel = self._listbox.curselection()
        self._selected_net_id = self._entity_ids[sel[0]] if sel else None

    def _update_inspector(self, snap: WorldSnapshot):
        e = snap.entities.get(self._selected_net_id) if self._selected_net_id is not None else None
        if e is None:
            e = snap.entities.get(snap.player_net_id)  # 未选中时默认监视玩家
        if e is None:
            self._inspector.config(text="(no entity)")
            return
        self._inspector.config(
            text=(
                f"{self._entity_name(e, snap.player_net_id)}\n"
                f"netId   {e.net_id}\n"
                f"space   {e.space_net_id}\n"
                f"pos     ({e.x:.2f}, {e.y:.2f}, {e.z:.2f})\n"
                f"vel     ({e.vx:.2f}, {e.vy:.2f}, {e.vz:.2f})"
            )
        )

    # ---- 收包日志面板 ----

    def _invalidate_log(self):
        self._log_revision = -1  # 过滤条件变化，强制重绘

    def _update_log(self):
        revision, lines = self._client.packets.recent()
        if revision == self._log_revision:
            return
        self._log_revision = revision
        needle = self._filter.get().strip()
        if needle:
            lines = [ln for ln in lines if needle in ln]
        self._log_text.config(state=tk.NORMAL)
        self._log_text.delete("1.0", tk.END)
        self._log_text.insert(tk.END, "\n".join(lines))
        self._log_text.see(tk.END)  # 自动滚动到最新
        self._log_text.config(state=tk.DISABLED)
