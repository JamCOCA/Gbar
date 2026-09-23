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
    FOCUS_SPAN = 2e7  # 双击列表项聚焦时画布视野跨度（米）

    def __init__(self, client: ThinClient):
        self._client = client
        self._selected_net_id = None  # 监视栏当前选中的实体
        self._selected_body_id = None  # 监视栏当前选中的天体（cele）
        self._follow_net_id = None  # 正在跟随的实体（镜头始终居中），None = 不跟随
        self._entity_ids = []  # 列表行号 → NetId 映射
        self._body_ids = []  # 天体列表行号 → NetId 映射
        self._log_revision = -1  # 已渲染的日志版本号
        self._net_last_time = time.monotonic()  # 网速采样基准时间
        self._net_last_sent = 0                  # 采样起点的累计发送字节
        self._net_last_recv = 0                  # 采样起点的累计接收字节
        self._net_up = 0.0                       # 上行速率 B/s
        self._net_down = 0.0                     # 下行速率 B/s

        self._paused = False               # 暂停快照：冻结显示，连接不断、数据照常收发
        self._frozen_snap = None           # 暂停时捕获的 WorldSnapshot
        self._frozen_planets = None        # 暂停时捕获的天体解析视图
        self._snap = None                  # 当前显示的 WorldSnapshot（双击聚焦用）
        self._planets = None               # 当前显示的天体解析视图

        self._root = tk.Tk()
        self._root.title("ThinClient")
        self._root.grid_rowconfigure(0, weight=1)
        self._root.grid_columnconfigure(0, weight=1)

        self._canvas = tk.Canvas(self._root, width=self.CANVAS_W, height=self.CANVAS_H, bg="#101418")
        self._canvas.grid(row=0, column=0, sticky="nsew")

        # 矢量视图状态：px/m 缩放 + 世界中心；scale=None 表示自动适配视野
        self._view_scale = None
        self._view_cx = 0.0
        self._view_cy = 0.0
        self._drag = None  # 拖拽起点 (x, y)
        self._show_soi = True  # 是否绘制 SOI 圈（g 键切换）
        self._canvas.bind("<Button-1>", self._on_press)
        self._canvas.bind("<B1-Motion>", self._on_drag)
        self._canvas.bind("<ButtonRelease-1>", self._on_release)
        self._canvas.bind("<MouseWheel>", self._on_wheel)
        self._canvas.bind("<Double-Button-1>", self._on_double)
        self._canvas.bind("<Button-4>", lambda e: self._zoom_at(e.x, e.y, 1.2))
        self._canvas.bind("<Button-5>", lambda e: self._zoom_at(e.x, e.y, 1 / 1.2))
        self._canvas.bind("<KeyPress-g>", lambda _e: self._toggle_soi())
        self._canvas.focus_set()
        # 用 bind_all 让快捷键全局生效（不依赖画布焦点），输入框内由处理器自行忽略
        self._root.bind_all("<KeyPress-space>", self._toggle_pause)
        self._root.bind_all("<KeyPress-p>", self._toggle_pause)
        self._root.bind_all("<KeyPress-f>", self._toggle_follow)

        right = tk.Frame(self._root, bg="#181c22")
        right.grid(row=0, column=1, sticky="ns")
        self._info = tk.Label(right, justify=tk.LEFT, anchor="nw", bg="#181c22", fg="#d7dce2",
                              font=("Consolas", 10), padx=10, pady=6)
        self._info.pack(fill=tk.X)
        self._pause_btn = tk.Button(right, text="Pause (space)", command=self._toggle_pause,
                                    bg="#2d3a4a", fg="#d7dce2", activebackground="#3d4a5a",
                                    activeforeground="#ffffff", relief=tk.FLAT, font=("Consolas", 9))
        self._pause_btn.pack(fill=tk.X, padx=10, pady=(0, 4))
        tk.Label(right, text="Bodies", bg="#181c22", fg="#8a94a2", font=("Consolas", 9)).pack(anchor="w", padx=10)
        self._body_listbox = tk.Listbox(right, width=30, height=5, bg="#101418", fg="#d7dce2",
                                        font=("Consolas", 10), selectbackground="#2d3a4a",
                                        highlightthickness=0, activestyle="none")
        self._body_listbox.pack(fill=tk.X, padx=10)
        self._body_listbox.bind("<<ListboxSelect>>", self._on_select_body)
        self._body_listbox.bind("<Double-Button-1>", self._focus_body)
        tk.Label(right, text="Entities", bg="#181c22", fg="#8a94a2", font=("Consolas", 9)).pack(anchor="w", padx=10)
        self._listbox = tk.Listbox(right, width=30, height=5, bg="#101418", fg="#d7dce2",
                                   font=("Consolas", 10), selectbackground="#2d3a4a",
                                   highlightthickness=0, activestyle="none")
        self._listbox.pack(fill=tk.X, padx=10)
        self._listbox.bind("<<ListboxSelect>>", self._on_select)
        self._listbox.bind("<Double-Button-1>", self._focus_entity)
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
        if self._paused:
            snap = self._frozen_snap
            planets = self._frozen_planets
        else:
            snap = self._client.state.snapshot()
            planets = self._client.state.resolve_celestials(snap.tick)
        self._snap = snap
        self._planets = planets
        # 跟随：镜头始终居中跟随实体（保留当前缩放，只改中心）
        if self._follow_net_id is not None:
            e = snap.entities.get(self._follow_net_id)
            if e is not None:
                wx, wy, _ = self._world_pos(e, planets)
                if self._view_scale is None:
                    self._view_scale = min(self.CANVAS_W, self.CANVAS_H) / self.FOCUS_SPAN
                self._view_cx = wx
                self._view_cy = wy
        self._draw(snap, planets)
        self._update_info(snap, planets)
        self._update_body_list(snap)
        self._update_entity_list(snap)
        self._update_inspector(snap, planets)
        self._update_log()
        self._root.after(UI_REFRESH_MS, self._refresh)

    # ---- 暂停 / 聚焦 ----

    def _toggle_pause(self, _event=None):
        """冻结当前快照（不关闭连接）：捕获此刻的实体表与天体位置，之后 UI 一直显示这份副本。"""
        # 输入框（filter/日志）里打字不触发暂停快捷键
        w = getattr(_event, "widget", None)
        if w is not None and isinstance(w, (tk.Entry, tk.Text)):
            return
        if not self._paused:
            self._frozen_snap = self._client.state.snapshot()
            self._frozen_planets = self._client.state.resolve_celestials(self._frozen_snap.tick)
        self._paused = not self._paused
        self._pause_btn.config(text="Resume (space)" if self._paused else "Pause (space)")

    def _focus_entity(self, _event):
        if self._snap is None or self._planets is None:
            return
        sel = self._listbox.curselection()
        if not sel:
            return
        net_id = self._entity_ids[sel[0]]
        e = self._snap.entities.get(net_id)
        if e is None:
            return
        wx, wy, _ = self._world_pos(e, self._planets)
        self._view_cx = wx
        self._view_cy = wy
        self._view_scale = min(self.CANVAS_W, self.CANVAS_H) / self.FOCUS_SPAN
        self._follow_net_id = net_id  # 双击后开始跟随该实体

    def _focus_body(self, _event):
        if self._snap is None or self._planets is None:
            return
        sel = self._body_listbox.curselection()
        if not sel:
            return
        net_id = self._body_ids[sel[0]]
        p = self._planets.get(net_id)
        if p is None:
            return
        self._view_cx = p.x
        self._view_cy = p.y
        self._view_scale = min(self.CANVAS_W, self.CANVAS_H) / self.FOCUS_SPAN

    def _toggle_follow(self, _event=None):
        """切换跟随：跟随当前选中的实体（镜头始终居中），再按一次取消。"""
        w = getattr(_event, "widget", None)
        if w is not None and isinstance(w, (tk.Entry, tk.Text)):
            return
        if self._follow_net_id is not None:
            self._follow_net_id = None
        elif self._selected_net_id is not None:
            self._follow_net_id = self._selected_net_id
            if self._view_scale is None:
                self._view_scale = min(self.CANVAS_W, self.CANVAS_H) / self.FOCUS_SPAN

    # ---- 视图变换（矢量缩放 / 拖拽） ----

    @staticmethod
    def _world_pos(e, planets: dict) -> tuple:
        """实体世界坐标：局部域实体（space_net_id != 0）叠加到母天体全局位置，全局域实体无偏移。"""
        p = planets.get(e.space_net_id)
        if p is not None:
            return e.x + p.x, e.y + p.y, e.z + p.z
        return e.x, e.y, e.z

    def _ensure_fit(self, snap: WorldSnapshot, planets: dict):
        if self._view_scale is not None:
            return
        ent = [self._world_pos(e, planets) for e in snap.entities.values()]
        xs = [w[0] for w in ent] + [p.x for p in planets.values()]
        ys = [w[1] for w in ent] + [p.y for p in planets.values()]
        if not xs:
            return
        w = max(max(xs) - min(xs), 1.0)
        h = max(max(ys) - min(ys), 1.0)
        self._view_scale = min(self.CANVAS_W / w, self.CANVAS_H / h) * 0.9
        self._view_cx = (min(xs) + max(xs)) / 2
        self._view_cy = (min(ys) + max(ys)) / 2

    def _on_press(self, event):
        self._drag = (event.x, event.y)

    def _on_drag(self, event):
        if self._drag is None or self._view_scale is None:
            return
        dx = event.x - self._drag[0]
        dy = event.y - self._drag[1]
        self._drag = (event.x, event.y)
        self._view_cx -= dx / self._view_scale
        self._view_cy += dy / self._view_scale

    def _on_release(self, _event):
        self._drag = None

    def _on_wheel(self, event):
        self._zoom_at(event.x, event.y, 1.2 if event.delta > 0 else 1 / 1.2)

    def _on_double(self, _event):
        self._view_scale = None  # 下一帧重新自动适配视野

    def _toggle_soi(self):
        self._show_soi = not self._show_soi

    def _zoom_at(self, sx, sy, factor):
        """以屏幕点 (sx, sy) 为锚缩放：先反解世界坐标，缩放后回设使锚点不动。"""
        if self._view_scale is None:
            return
        wx = (sx - self.CANVAS_W / 2) / self._view_scale + self._view_cx
        wy = self._view_cy - (sy - self.CANVAS_H / 2) / self._view_scale
        self._view_scale = max(1e-12, min(10.0, self._view_scale * factor))
        self._view_cx = wx - (sx - self.CANVAS_W / 2) / self._view_scale
        self._view_cy = wy + (sy - self.CANVAS_H / 2) / self._view_scale

    # ---- 画布 ----

    def _draw(self, snap: WorldSnapshot, planets: dict):
        c = self._canvas
        c.delete("all")
        if not snap.entities and not planets:
            c.create_text(self.CANVAS_W / 2, self.CANVAS_H / 2, text="waiting for snapshot...",
                          fill="#5a6472", font=("Consolas", 14))
            return
        self._ensure_fit(snap, planets)
        scale = self._view_scale

        def to_px(x, y):
            return ((x - self._view_cx) * scale + self.CANVAS_W / 2,
                    (self._view_cy - y) * scale + self.CANVAS_H / 2)

        # 行星层（cele，与 entity 分离）：SOI 圈 + 星体
        for p in planets.values():
            px, py = to_px(p.x, p.y)
            if self._show_soi and p.parent_net_id != -1:
                r = p.soi_radius * scale
                if r > 1.0:
                    c.create_oval(px - r, py - r, px + r, py + r, outline="#33506b", dash=(2, 4))
        for p in planets.values():
            px, py = to_px(p.x, p.y)
            r = min(max(p.surface_radius * scale, 3.0), 24.0)
            color = "#ffd54f" if p.parent_net_id == -1 else "#90a4ae"
            c.create_oval(px - r, py - r, px + r, py + r, fill=color, outline="")
            c.create_text(px, py - r - 6, text=str(p.net_id), fill="#cfd8dc",
                          font=("Consolas", 8))

        for e in snap.entities.values():
            wx, wy, _ = self._world_pos(e, planets)
            px, py = to_px(wx, wy)
            is_player = e.net_id == snap.player_net_id
            color, r = ("#ffb454", 7) if is_player else ("#4fc3f7", 5)
            if e.net_id == self._selected_net_id:
                c.create_oval(px - r - 3, py - r - 3, px + r + 3, py + r + 3, outline="#ffffff")
            c.create_oval(px - r, py - r, px + r, py + r, fill=color, outline="")
            c.create_text(px + 10, py, text=str(e.net_id), fill="#8a94a2", anchor="w", font=("Consolas", 8))
            c.create_line(px, py, *to_px(wx + e.vx, wy + e.vy), fill=color, arrow=tk.LAST)

        c.create_text(6, self.CANVAS_H - 6, text="drag=pan  wheel=zoom  dblclick=fit  g=soi  space=pause  f=follow  2x-list=focus",
                      fill="#5a6472", anchor="sw", font=("Consolas", 8))

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

    def _update_info(self, snap: WorldSnapshot, planets: dict):
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
        # 玩家当前引力源（引力系索引查询）
        gravity = "—"
        player = snap.entities.get(snap.player_net_id)
        if player is not None and planets:
            src = self._client.state.index.soi_at(player.x, player.y, player.z)
            if src is not None:
                gravity = f"netId={src.net_id}"
        self._info.config(
            text=(
                f"state     {'PAUSED' if self._paused else 'live'}\n"
                f"tick      {snap.tick}\n"
                f"entities  {len(snap.entities)}\n"
                f"bodies    {len(snap.bodies)}\n"
                f"player    netId={snap.player_net_id}\n"
                f"gravity   {gravity}\n"
                f"follow    {self._follow_net_id if self._follow_net_id is not None else '-'}\n"
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
        if self._selected_net_id is not None:
            self._selected_body_id = None
            self._body_listbox.selection_clear(0, tk.END)

    def _on_select_body(self, _event):
        sel = self._body_listbox.curselection()
        self._selected_body_id = self._body_ids[sel[0]] if sel else None
        if self._selected_body_id is not None:
            self._selected_net_id = None
            self._listbox.selection_clear(0, tk.END)

    @staticmethod
    def _body_name(b, snap: WorldSnapshot) -> str:
        if b.parent_net_id == -1:
            return f"Star   #{b.net_id}"
        if any(o.parent_net_id == b.net_id for o in snap.bodies.values()):
            return f"Planet #{b.net_id}"
        return f"Moon   #{b.net_id}"

    def _update_body_list(self, snap: WorldSnapshot):
        ids = sorted(snap.bodies)
        if ids == self._body_ids:
            return  # 列表无变化，避免重绘打断选择
        self._body_ids = ids
        self._body_listbox.delete(0, tk.END)
        for net_id in ids:
            self._body_listbox.insert(tk.END, self._body_name(snap.bodies[net_id], snap))
        if self._selected_body_id in ids:
            self._body_listbox.selection_set(ids.index(self._selected_body_id))

    @staticmethod
    def _body_info(b, view, snap: WorldSnapshot) -> str:
        if b.parent_net_id == -1:
            kind = "Star"
        elif any(o.parent_net_id == b.net_id for o in snap.bodies.values()):
            kind = "Planet"
        else:
            kind = "Moon"
        soi = "∞" if b.soi_radius > 1e100 else f"{b.soi_radius:.6g} m"
        lines = [
            f"{kind} #{b.net_id}",
            f"netId   {b.net_id}",
            f"parent  {b.parent_net_id}",
        ]
        if view is not None:
            lines.append(f"pos     ({view.x:.3e}, {view.y:.3e}, {view.z:.3e})")
        lines.append(f"radius  {b.surface_radius:.6g} m")
        lines.append(f"soi     {soi}")
        lines.append(f"mu      {b.mu:.6g}")
        if b.parent_net_id != -1:
            lines.append(f"orbit   a={b.semi_major_axis:.6g} e={b.eccentricity:.4g}")
            lines.append(f"        T={b.orbital_period:.6g}s phase={b.initial_phase:.4g}")
        return "\n".join(lines)

    def _update_inspector(self, snap: WorldSnapshot, planets: dict):
        if self._selected_body_id is not None:
            b = snap.bodies.get(self._selected_body_id)
            if b is not None:
                self._inspector.config(text=self._body_info(b, planets.get(b.net_id), snap))
                return
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
