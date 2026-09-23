"""pyglet 3D 视图：左侧 3D 视口 + 右侧监视栏（与 2D 布局一致）。

- 左侧 3D：左键拖拽旋转，右键或 Shift+左键拖拽平移，滚轮缩放；单击实体选中
- 右侧监视栏：信息 / 暂停按钮 / Bodies 列表 / Entities 列表 / Inspector
- 单击列表项选中；双击列表项聚焦 + 跟随；f 切换跟随（镜头始终居中该实体）
- 空格暂停，r 重置视野，g 切换 SOI 圈
- 渲染用"浮点原点"：所有坐标相对相机目标，避免天文尺度下单精度抖动
"""

import math
import time

import pyglet
from pyglet import font, gl
from pyglet.graphics.shader import Shader, ShaderProgram
from pyglet.math import Mat4, Vec3

from .config import UI_REFRESH_MS

_VERT_SOLID = """#version 330 core
in vec3 position;
uniform mat4 mvp;
void main() { gl_Position = mvp * vec4(position, 1.0); }
"""

_FRAG_SOLID = """#version 330 core
uniform vec4 color;
out vec4 fragColor;
void main() { fragColor = color; }
"""

_VERT_COLOR = """#version 330 core
in vec3 position;
in vec4 color;
uniform mat4 mvp;
out vec4 vColor;
void main() {
    gl_Position = mvp * vec4(position, 1.0);
    vColor = color;
    gl_PointSize = 8.0;
}
"""

_FRAG_COLOR = """#version 330 core
in vec4 vColor;
out vec4 fragColor;
void main() { fragColor = vColor; }
"""


class GLViewer:
    FOV = 60.0
    PANEL_W = 320

    def __init__(self, client):
        self._client = client
        self._paused = False
        self._frozen_snap = None
        self._frozen_planets = None
        self._snap = None
        self._planets = None
        self._selected_net_id = None
        self._selected_body_id = None
        self._follow_net_id = None
        self._show_soi = True
        self._body_ids = []
        self._entity_ids = []
        self._bodies_scroll = 0
        self._entities_scroll = 0
        self._bodies_key = None
        self._entities_key = None
        self._line_h = float(font.load("Consolas", 10).ascent - font.load("Consolas", 10).descent)

        self._net_last_time = 0.0
        self._net_last_sent = 0
        self._net_last_recv = 0
        self._net_up = 0.0
        self._net_down = 0.0
        self._last_click = (0.0, -1, -1)

        self._target = [0.0, 0.0, 0.0]
        self._distance = 3.0e11
        self._yaw = 0.8
        self._pitch = 0.55
        self._drag_mode = None
        self._press = (0, 0)

        self._window = pyglet.window.Window(
            width=1400, height=820, caption="ThinClient 3D", resizable=True,
            config=gl.Config(double_buffer=True, depth_size=24))
        self._window.on_mouse_press = self._on_mouse_press
        self._window.on_mouse_drag = self._on_mouse_drag
        self._window.on_mouse_release = self._on_mouse_release
        self._window.on_mouse_scroll = self._on_mouse_scroll
        self._window.on_key_press = self._on_key_press
        self._window.on_draw = self._on_draw
        self._window.on_resize = self._on_resize

        self._solid = ShaderProgram(Shader(_VERT_SOLID, "vertex"), Shader(_FRAG_SOLID, "fragment"))
        self._points = ShaderProgram(Shader(_VERT_COLOR, "vertex"), Shader(_FRAG_COLOR, "fragment"))

        sphere = self._make_sphere(32, 20)
        self._sphere_vlist = self._solid.vertex_list(len(sphere) // 3, gl.GL_TRIANGLES, position=("f", sphere))
        circle = self._make_circle(96)
        self._circle_vlist = self._solid.vertex_list(len(circle) // 3, gl.GL_LINE_LOOP, position=("f", circle))

        self._build_panel()
        self._layout_panel()
        pyglet.clock.schedule_interval(self._update, UI_REFRESH_MS / 1000.0)

    def run(self):
        pyglet.app.run()

    # ---- 面板 ----

    def _build_panel(self):
        self._panel_bg = pyglet.shapes.Rectangle(0, 0, self.PANEL_W, 0, color=(24, 28, 34))
        self._pause_rect = pyglet.shapes.Rectangle(0, 0, 0, 0, color=(45, 58, 74))
        self._info_label = pyglet.text.Label("", font_name="Consolas", font_size=10, anchor_y="top", multiline=True, width=self.PANEL_W - 20, color=(215, 220, 226, 255))
        self._pause_label = pyglet.text.Label("Pause (space)", font_name="Consolas", font_size=9, anchor_y="center", color=(215, 220, 226, 255))
        self._bodies_header = pyglet.text.Label("Bodies", font_name="Consolas", font_size=9, anchor_y="top", color=(138, 148, 162, 255))
        self._bodies_list = pyglet.text.Label("", font_name="Consolas", font_size=10, anchor_y="top", multiline=True, width=self.PANEL_W - 20, color=(215, 220, 226, 255))
        self._entities_header = pyglet.text.Label("Entities", font_name="Consolas", font_size=9, anchor_y="top", color=(138, 148, 162, 255))
        self._entities_list = pyglet.text.Label("", font_name="Consolas", font_size=10, anchor_y="top", multiline=True, width=self.PANEL_W - 20, color=(215, 220, 226, 255))
        self._inspector_header = pyglet.text.Label("Inspector", font_name="Consolas", font_size=9, anchor_y="top", color=(138, 148, 162, 255))
        self._inspector = pyglet.text.Label("(no entity)", font_name="Consolas", font_size=10, anchor_y="top", multiline=True, width=self.PANEL_W - 20, color=(215, 220, 226, 255))

    def _layout_panel(self):
        w, h = self._window.width, self._window.height
        pw = w - self.PANEL_W
        pad = 10
        x0 = pw + pad
        inner_w = self.PANEL_W - 2 * pad
        lh = self._line_h
        hdr = lh + 4  # 每个列表区标题占用的高度

        self._panel_bg.x, self._panel_bg.y = pw, 0
        self._panel_bg.width, self._panel_bg.height = self.PANEL_W, h

        # 信息区（顶部，固定 8 行）
        info_h = 8 * lh
        self._info_region = (x0, h - pad - info_h, inner_w, info_h)
        self._info_label.x, self._info_label.y, self._info_label.width = x0, h - pad, inner_w

        # 暂停按钮
        btn_h = 22
        self._pause_rect.x, self._pause_rect.y = x0, h - pad - info_h - 6 - btn_h
        self._pause_rect.width, self._pause_rect.height = inner_w, btn_h
        self._pause_label.x, self._pause_label.y = x0 + 8, self._pause_rect.y + btn_h / 2

        # 列表区可用高度（从上到下：Bodies / Entities / Inspector）
        top = self._pause_rect.y - 12
        bottom = pad
        avail = max(top - bottom, 3 * hdr + 3 * lh)
        bodies_h = int(avail * 0.28)
        entities_h = int(avail * 0.40)
        inspector_h = avail - bodies_h - entities_h - 2 * 12

        # Bodies
        self._bodies_header.x, self._bodies_header.y = x0, top
        self._bodies_top = top - hdr
        self._bodies_region = (x0, self._bodies_top - bodies_h, inner_w, bodies_h)
        self._bodies_list.x, self._bodies_list.y = x0 + 2, self._bodies_top
        self._bodies_list.width = inner_w - 4

        # Entities
        etop = self._bodies_region[1] - 12
        self._entities_header.x, self._entities_header.y = x0, etop
        self._entities_top = etop - hdr
        self._entities_region = (x0, self._entities_top - entities_h, inner_w, entities_h)
        self._entities_list.x, self._entities_list.y = x0 + 2, self._entities_top
        self._entities_list.width = inner_w - 4

        # Inspector
        itop = self._entities_region[1] - 12
        self._inspector_header.x, self._inspector_header.y = x0, itop
        self._inspector_top = itop - hdr
        self._inspector_region = (x0, bottom, inner_w, self._inspector_top - bottom)
        self._inspector.x, self._inspector.y = x0 + 2, self._inspector_top
        self._inspector.width = inner_w - 4

    # ---- 数据 ----

    def _update(self, _dt):
        if self._paused:
            snap, planets = self._frozen_snap, self._frozen_planets
        else:
            snap = self._client.state.snapshot()
            planets = self._client.state.resolve_celestials(snap.tick)
        self._snap = snap
        self._planets = planets

        if self._follow_net_id is not None and snap is not None:
            e = snap.entities.get(self._follow_net_id)
            if e is not None:
                wx, wy, wz = self._world_pos(e, planets)
                self._target = [wx, wy, wz]

        self._update_net_rate()
        self._info_label.text = self._info_text()
        self._update_lists()
        self._inspector.text = self._inspector_text()

    def _update_net_rate(self):
        net = getattr(self._client, "net", None)
        if net is None:
            return
        now = time.monotonic()
        if self._net_last_time == 0.0:
            self._net_last_time = now
            self._net_last_sent = net.BytesSent
            self._net_last_recv = net.BytesReceived
            return
        elapsed = now - self._net_last_time
        if elapsed >= 0.5:
            sent, recv = net.BytesSent, net.BytesReceived
            self._net_up = (sent - self._net_last_sent) / elapsed
            self._net_down = (recv - self._net_last_recv) / elapsed
            self._net_last_time, self._net_last_sent, self._net_last_recv = now, sent, recv

    def _info_text(self):
        s = self._snap
        if s is None:
            return "waiting for snapshot..."
        gravity = "-"
        player = s.entities.get(s.player_net_id)
        if player is not None and self._planets:
            src = self._client.state.index.soi_at(player.x, player.y, player.z)
            if src is not None:
                gravity = f"netId={src.net_id}"
        return "\n".join((
            f"{'PAUSED' if self._paused else 'live'}  tick={s.tick}",
            f"entities  {len(s.entities)}",
            f"bodies    {len(s.bodies)}",
            f"player    netId={s.player_net_id}",
            f"gravity   {gravity}",
            f"follow    {self._follow_net_id if self._follow_net_id is not None else '-'}",
            f"up        {self._fmt_rate(self._net_up)}",
            f"down      {self._fmt_rate(self._net_down)}",
        ))

    @staticmethod
    def _fmt_rate(bps):
        if bps >= 1_048_576:
            return f"{bps / 1_048_576:.2f} MB/s"
        if bps >= 1024:
            return f"{bps / 1024:.2f} KB/s"
        return f"{bps:.1f} B/s"

    @staticmethod
    def _entity_name(e, player_net_id):
        return f"Player #{e.net_id}" if e.net_id == player_net_id else f"Entity #{e.net_id}"

    @staticmethod
    def _body_name(b, snap):
        if b.parent_net_id == -1:
            return f"Star   #{b.net_id}"
        if any(o.parent_net_id == b.net_id for o in snap.bodies.values()):
            return f"Planet #{b.net_id}"
        return f"Moon   #{b.net_id}"

    def _update_lists(self):
        snap = self._snap
        if snap is None:
            return
        body_ids = sorted(snap.bodies)
        entity_ids = sorted(snap.entities)
        bkey = (tuple(body_ids), self._selected_body_id)
        ekey = (tuple(entity_ids), self._selected_net_id)
        if bkey != self._bodies_key:
            self._bodies_key = bkey
            self._body_ids = body_ids
            self._bodies_header.text = f"Bodies ({len(body_ids)})"
            self._bodies_list.text = "\n".join(
                (">" if b == self._selected_body_id else " ") + self._body_name(snap.bodies[b], snap)
                for b in body_ids)
        if ekey != self._entities_key:
            self._entities_key = ekey
            self._entity_ids = entity_ids
            self._entities_header.text = f"Entities ({len(entity_ids)})"
            self._entities_list.text = "\n".join(
                (">" if e == self._selected_net_id else " ") + self._entity_name(snap.entities[e], snap.player_net_id)
                for e in entity_ids)
        self._position_lists()

    def _position_lists(self):
        lh = self._line_h
        self._bodies_scroll = self._clamp_scroll(self._bodies_scroll, len(self._body_ids), self._bodies_region[3])
        self._entities_scroll = self._clamp_scroll(self._entities_scroll, len(self._entity_ids), self._entities_region[3])
        self._bodies_list.y = self._bodies_top + self._bodies_scroll * lh
        self._entities_list.y = self._entities_top + self._entities_scroll * lh

    def _clamp_scroll(self, scroll, count, region_h):
        visible = max(1, int(region_h // self._line_h))
        return max(0, min(int(scroll), max(0, count - visible)))

    def _inspector_text(self):
        snap = self._snap
        if snap is None:
            return "(no snapshot)"
        if self._selected_body_id is not None:
            b = snap.bodies.get(self._selected_body_id)
            if b is not None:
                return self._body_info(b, (self._planets or {}).get(b.net_id), snap)
        e = snap.entities.get(self._selected_net_id) if self._selected_net_id is not None else None
        if e is None:
            e = snap.entities.get(snap.player_net_id)
        if e is None:
            return "(no entity)"
        return "\n".join((
            self._entity_name(e, snap.player_net_id),
            f"netId   {e.net_id}",
            f"space   {e.space_net_id}",
            f"pos     ({e.x:.2f}, {e.y:.2f}, {e.z:.2f})",
            f"vel     ({e.vx:.2f}, {e.vy:.2f}, {e.vz:.2f})",
        ))

    @staticmethod
    def _body_info(b, view, snap):
        if b.parent_net_id == -1:
            kind = "Star"
        elif any(o.parent_net_id == b.net_id for o in snap.bodies.values()):
            kind = "Planet"
        else:
            kind = "Moon"
        soi = "inf" if b.soi_radius > 1e100 else f"{b.soi_radius:.6g} m"
        lines = [f"{kind} #{b.net_id}", f"netId   {b.net_id}", f"parent  {b.parent_net_id}"]
        if view is not None:
            lines.append(f"pos     ({view.x:.3e}, {view.y:.3e}, {view.z:.3e})")
        lines.append(f"radius  {b.surface_radius:.6g} m")
        lines.append(f"soi     {soi}")
        lines.append(f"mu      {b.mu:.6g}")
        if b.parent_net_id != -1:
            lines.append(f"orbit   a={b.semi_major_axis:.6g} e={b.eccentricity:.4g}")
            lines.append(f"        T={b.orbital_period:.6g}s phase={b.initial_phase:.4g}")
        return "\n".join(lines)

    # ---- 几何 ----

    @staticmethod
    def _make_sphere(segments, rings):
        verts = []
        for i in range(rings):
            t0 = math.pi * i / rings
            t1 = math.pi * (i + 1) / rings
            for j in range(segments):
                p0 = 2 * math.pi * j / segments
                p1 = 2 * math.pi * (j + 1) / segments
                a = (math.sin(t0) * math.cos(p0), math.cos(t0), math.sin(t0) * math.sin(p0))
                b = (math.sin(t0) * math.cos(p1), math.cos(t0), math.sin(t0) * math.sin(p1))
                c = (math.sin(t1) * math.cos(p1), math.cos(t1), math.sin(t1) * math.sin(p1))
                d = (math.sin(t1) * math.cos(p0), math.cos(t1), math.sin(t1) * math.sin(p0))
                verts += (a, b, c, a, c, d)
        return [f for tri in verts for f in tri]

    @staticmethod
    def _make_circle(segments):
        out = []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            out += [math.cos(a), math.sin(a), 0.0]
        return out

    # ---- 相机 ----

    def _eye(self):
        cp = math.cos(self._pitch)
        return (self._target[0] + self._distance * cp * math.cos(self._yaw),
                self._target[1] + self._distance * cp * math.sin(self._yaw),
                self._target[2] + self._distance * math.sin(self._pitch))

    def _camera_basis(self):
        cp, sp = math.cos(self._pitch), math.sin(self._pitch)
        cy, sy = math.cos(self._yaw), math.sin(self._yaw)
        fx, fy, fz = -cp * cy, -cp * sy, -sp
        rx, ry = fy, -fx
        rl = math.hypot(rx, ry)
        rx, ry = (1.0, 0.0) if rl < 1e-12 else (rx / rl, ry / rl)
        rz = 0.0
        ux, uy, uz = ry * fz, -rx * fz, rx * fy - ry * fx
        return (fx, fy, fz), (rx, ry, rz), (ux, uy, uz)

    def _reset_view(self):
        planets = self._planets or {}
        xs = [p.x for p in planets.values()]
        ys = [p.y for p in planets.values()]
        if not xs:
            self._target = [0.0, 0.0, 0.0]
            self._distance = 3.0e11
            return
        self._target = [(min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, 0.0]
        span = max(max(xs) - min(xs), max(ys) - min(ys), 1.0)
        self._distance = span * 1.3

    # ---- 输入 ----

    def _on_mouse_press(self, x, y, button, modifiers):
        self._press = (x, y)
        w = self._window.width
        if x >= w - self.PANEL_W:
            self._drag_mode = None
            if button == pyglet.window.mouse.LEFT:
                self._handle_panel_click(x, y)
            return
        if button == pyglet.window.mouse.LEFT:
            self._drag_mode = "pan" if modifiers & pyglet.window.key.MOD_SHIFT else "rotate"
        elif button == pyglet.window.mouse.RIGHT:
            self._drag_mode = "pan"

    def _on_mouse_drag(self, x, y, dx, dy, button, modifiers):
        if self._drag_mode is None:
            return
        if self._drag_mode == "rotate":
            self._yaw -= dx * 0.008
            self._pitch += dy * 0.008
            self._pitch = max(-1.55, min(1.55, self._pitch))
        else:
            _, (rx, ry, rz), (ux, uy, uz) = self._camera_basis()
            s = self._distance * 0.0006
            self._target[0] += (-dx * rx + dy * ux) * s
            self._target[1] += (-dx * ry + dy * uy) * s
            self._target[2] += (-dx * rz + dy * uz) * s

    def _on_mouse_release(self, x, y, button, modifiers):
        if self._drag_mode is not None:
            if button == pyglet.window.mouse.LEFT and math.hypot(x - self._press[0], y - self._press[1]) < 4:
                self._pick_entity(x, y)
        self._drag_mode = None

    def _on_mouse_scroll(self, x, y, scroll_x, scroll_y):
        w = self._window.width
        if x >= w - self.PANEL_W:
            self._scroll_panel(x, y, scroll_y)
            return
        self._distance *= 1.1 ** (-scroll_y)
        self._distance = max(1e3, min(1e13, self._distance))

    def _scroll_panel(self, x, y, scroll_y):
        bx, by, bw, bh = self._bodies_region
        ex, ey, ew, eh = self._entities_region
        if bx <= x <= bx + bw and by <= y <= by + bh:
            self._bodies_scroll = max(0, self._bodies_scroll - int(scroll_y))
        elif ex <= x <= ex + ew and ey <= y <= ey + eh:
            self._entities_scroll = max(0, self._entities_scroll - int(scroll_y))

    def _on_key_press(self, symbol, modifiers):
        if symbol == pyglet.window.key.SPACE:
            self._toggle_pause()
        elif symbol == pyglet.window.key.R:
            self._follow_net_id = None
            self._reset_view()
        elif symbol == pyglet.window.key.G:
            self._show_soi = not self._show_soi
        elif symbol == pyglet.window.key.F:
            self._toggle_follow()

    def _on_resize(self, width, height):
        self._layout_panel()

    def _toggle_pause(self):
        if not self._paused:
            self._frozen_snap = self._client.state.snapshot()
            self._frozen_planets = self._client.state.resolve_celestials(self._frozen_snap.tick)
        self._paused = not self._paused
        self._pause_label.text = "Resume (space)" if self._paused else "Pause (space)"

    def _toggle_follow(self):
        if self._follow_net_id is not None:
            self._follow_net_id = None
        elif self._selected_net_id is not None:
            self._follow_net_id = self._selected_net_id
            self._focus_entity(self._selected_net_id)

    def _focus_entity(self, net_id):
        snap, planets = self._snap, self._planets
        if snap is None or planets is None:
            return
        e = snap.entities.get(net_id)
        if e is None:
            return
        wx, wy, wz = self._world_pos(e, planets)
        self._target = [wx, wy, wz]
        self._distance = 5e7

    # ---- 面板交互 ----

    def _handle_panel_click(self, x, y):
        now = time.monotonic()
        is_double = (now - self._last_click[0] < 0.4
                     and abs(x - self._last_click[1]) < 6 and abs(y - self._last_click[2]) < 6)
        self._last_click = (now, x, y)

        if self._pause_rect.x <= x <= self._pause_rect.x + self._pause_rect.width \
                and self._pause_rect.y <= y <= self._pause_rect.y + self._pause_rect.height:
            self._toggle_pause()
            return

        # Bodies 列表
        idx = self._row_at(self._bodies_region, self._bodies_top, self._bodies_scroll, x, y, len(self._body_ids))
        if idx >= 0:
            net_id = self._body_ids[idx]
            self._selected_body_id = net_id
            self._selected_net_id = None
            self._follow_net_id = None
            if is_double:
                p = (self._planets or {}).get(net_id)
                if p is not None:
                    self._target = [p.x, p.y, p.z]
                    self._distance = 2e8
            return

        # Entities 列表
        idx = self._row_at(self._entities_region, self._entities_top, self._entities_scroll, x, y, len(self._entity_ids))
        if idx >= 0:
            net_id = self._entity_ids[idx]
            self._selected_net_id = net_id
            self._selected_body_id = None
            self._focus_entity(net_id)
            if is_double:
                self._follow_net_id = net_id
            return

    def _row_at(self, region, top, scroll, x, y, count):
        rx, ry, rw, rh = region
        if not (rx <= x <= rx + rw and ry <= y <= ry + rh):
            return -1
        idx = scroll + int((top - y) / self._line_h)
        return idx if 0 <= idx < count else -1

    # ---- 拾取 ----

    @staticmethod
    def _world_pos(e, planets):
        p = planets.get(e.space_net_id)
        if p is not None:
            return e.x + p.x, e.y + p.y, e.z + p.z
        return e.x, e.y, e.z

    def _project(self, wx, wy, wz):
        ex, ey, ez = self._eye()
        (fx, fy, fz), (rx, ry, rz), (ux, uy, uz) = self._camera_basis()
        dx, dy, dz = wx - ex, wy - ey, wz - ez
        cam_x = dx * rx + dy * ry + dz * rz
        cam_y = dx * ux + dy * uy + dz * uz
        cam_z = dx * fx + dy * fy + dz * fz
        if cam_z <= 0:
            return None
        vw = self._window.width - self.PANEL_W
        vh = self._window.height
        focal = (vh / 2) / math.tan(math.radians(self.FOV) / 2)
        return vw / 2 + (cam_x / cam_z) * focal, vh / 2 + (cam_y / cam_z) * focal

    def _pick_entity(self, sx, sy):
        snap, planets = self._snap, self._planets
        if snap is None or planets is None:
            return
        best, best_d = None, 14.0
        for e in snap.entities.values():
            wx, wy, wz = self._world_pos(e, planets)
            p = self._project(wx, wy, wz)
            if p is None:
                continue
            d = math.hypot(p[0] - sx, p[1] - sy)
            if d < best_d:
                best_d, best = d, e
        if best is not None:
            self._selected_net_id = best.net_id
            self._selected_body_id = None
            self._focus_entity(best.net_id)

    # ---- 渲染 ----

    def _on_draw(self):
        w, h = self._window.width, self._window.height
        vw = w - self.PANEL_W

        gl.glClearColor(0.04, 0.05, 0.07, 1.0)
        gl.glClear(gl.GL_COLOR_BUFFER_BIT | gl.GL_DEPTH_BUFFER_BIT)
        gl.glEnable(gl.GL_DEPTH_TEST)

        gl.glViewport(0, 0, vw, h)
        self._draw_scene(vw, h)

        gl.glViewport(0, 0, w, h)
        gl.glDisable(gl.GL_DEPTH_TEST)
        self._panel_bg.draw()
        self._pause_rect.draw()
        self._pause_label.draw()
        self._draw_clipped(self._info_region, self._info_label)
        self._draw_clipped(self._bodies_region, self._bodies_list)
        self._draw_clipped(self._entities_region, self._entities_list)
        self._draw_clipped(self._inspector_region, self._inspector)
        self._bodies_header.draw()
        self._entities_header.draw()
        self._inspector_header.draw()

    @staticmethod
    def _draw_clipped(region, label):
        x, y, w, h = region
        if h <= 0:
            return
        gl.glEnable(gl.GL_SCISSOR_TEST)
        gl.glScissor(int(x), int(y), int(w), int(h))
        label.draw()
        gl.glDisable(gl.GL_SCISSOR_TEST)

    def _draw_scene(self, vw, vh):
        tx, ty, tz = self._target
        ex, ey, ez = self._eye()
        near = max(self._distance * 0.01, 1.0)
        far = max(self._distance * 1000.0, 1e13)
        proj = Mat4.perspective_projection(vw / max(vh, 1), near, far, fov=self.FOV)
        view = Mat4.look_at(Vec3(ex - tx, ey - ty, ez - tz), Vec3(0.0, 0.0, 0.0), Vec3(0.0, 0.0, 1.0))
        vp = proj @ view

        planets = self._planets or {}
        snap = self._snap

        self._solid.use()
        if self._show_soi:
            self._solid["color"] = (0.25, 0.35, 0.5, 1.0)
            for p in planets.values():
                if p.parent_net_id == -1:
                    continue
                m = Mat4.from_translation(Vec3(p.x - tx, p.y - ty, p.z - tz)) @ Mat4.from_scale(
                    Vec3(p.soi_radius, p.soi_radius, p.soi_radius))
                self._solid["mvp"] = tuple(vp @ m)
                self._circle_vlist.draw(gl.GL_LINE_LOOP)
        for p in planets.values():
            m = Mat4.from_translation(Vec3(p.x - tx, p.y - ty, p.z - tz)) @ Mat4.from_scale(
                Vec3(p.surface_radius, p.surface_radius, p.surface_radius))
            self._solid["mvp"] = tuple(vp @ m)
            self._solid["color"] = (1.0, 0.85, 0.35, 1.0) if p.parent_net_id == -1 else (0.55, 0.65, 0.75, 1.0)
            self._sphere_vlist.draw(gl.GL_TRIANGLES)
        self._solid.stop()

        if snap is not None and snap.entities:
            pos, col = [], []
            for e in snap.entities.values():
                wx, wy, wz = self._world_pos(e, planets)
                pos += [wx - tx, wy - ty, wz - tz]
                if e.net_id == snap.player_net_id:
                    c = (1.0, 0.7, 0.3, 1.0)
                elif e.net_id == self._selected_net_id or e.net_id == self._follow_net_id:
                    c = (1.0, 1.0, 1.0, 1.0)
                else:
                    c = (0.3, 0.75, 1.0, 1.0)
                col += list(c)
            gl.glEnable(gl.GL_PROGRAM_POINT_SIZE)
            self._points.use()
            self._points["mvp"] = tuple(vp)
            vlist = self._points.vertex_list(len(snap.entities), gl.GL_POINTS,
                                             position=("f", pos), color=("f", col))
            vlist.draw(gl.GL_POINTS)
            self._points.stop()
