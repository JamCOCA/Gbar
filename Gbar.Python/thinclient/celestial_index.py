"""天体引力系索引：恒星→行星→卫星 层级 + 行星范围的空间哈希（客户端本地）。

数据源为 Galaxy 包下发的天体根数（state.CelestialBody），天体是 cele 而非 entity，
与同步实体（EntityView）完全分离。位置由轨道解析按 tick 现算，rebuild_positions 后支持：

    soi_at(x, y, z)   该点落入的引力源（SoiRadius 包含且最深者）；无行星包含时落到恒星
    nearest(x, y, z)  最近天体
    children_of(id)   某天体的子天体（引力系层级）
    roots             根天体（恒星）列表

空间哈希 cell 尺寸取「行星 SOI 上界」（恒星 SOI = 无限，不参与取界），
保证任意点只需查 3×3×3 邻居 cell 即可覆盖其所属引力圈。
"""

import math
import threading


class CelestialIndex:
    def __init__(self):
        self._lock = threading.Lock()
        self._bodies = {}     # net_id -> CelestialBody
        self._children = {}   # parent_net_id -> [net_id]
        self._roots = []      # 恒星（无母天体）net_id 列表
        self._positions = {}  # net_id -> (x, y, z) 米
        self._grid = {}       # (cx, cy, cz) -> [net_id]
        self._cell_size = 1.0

    # ---- 写入 ----

    def set_bodies(self, bodies):
        """重建引力系层级与 cell 尺寸（Galaxy 到达时调用）。"""
        with self._lock:
            self._bodies = {b.net_id: b for b in bodies}
            self._children = {}
            self._roots = []
            for b in self._bodies.values():
                if b.parent_net_id == -1 or b.parent_net_id not in self._bodies:
                    self._roots.append(b.net_id)
                else:
                    self._children.setdefault(b.parent_net_id, []).append(b.net_id)
            sois = [b.soi_radius for b in self._bodies.values() if b.parent_net_id in self._bodies]
            self._cell_size = max(sois) if sois else 1.0
            self._positions = {}
            self._grid = {}

    def rebuild_positions(self, positions):
        """用某 tick 的解析位置重建空间哈希（每 tick 调用，天体量小成本可忽略）。"""
        with self._lock:
            self._positions = dict(positions)
            self._grid = {}
            for net_id, (x, y, z) in positions.items():
                key = self._cell(x, y, z)
                self._grid.setdefault(key, []).append(net_id)

    # ---- 读取 ----

    @property
    def roots(self):
        with self._lock:
            return list(self._roots)

    def children_of(self, net_id):
        with self._lock:
            return list(self._children.get(net_id, ()))

    def _cell(self, x, y, z):
        return (math.floor(x / self._cell_size),
                math.floor(y / self._cell_size),
                math.floor(z / self._cell_size))

    def _candidates(self, x, y, z):
        cx, cy, cz = self._cell(x, y, z)
        out = list(self._roots)  # 恒星覆盖整个星系，恒为候选
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    out.extend(self._grid.get((cx + dx, cy + dy, cz + dz), ()))
        return out

    def soi_at(self, x, y, z):
        """该点落入的引力源（SoiRadius 包含且最深者）；深空返回恒星。"""
        with self._lock:
            best = None
            best_r = None
            for net_id in self._candidates(x, y, z):
                b = self._bodies.get(net_id)
                if b is None:
                    continue
                px, py, pz = self._positions.get(net_id, (0.0, 0.0, 0.0))
                d2 = (x - px) ** 2 + (y - py) ** 2 + (z - pz) ** 2
                if d2 <= b.soi_radius * b.soi_radius and (best_r is None or b.soi_radius < best_r):
                    best, best_r = b, b.soi_radius
            return best

    def nearest(self, x, y, z):
        """最近天体（含恒星）。"""
        with self._lock:
            best = None
            best_d2 = None
            for net_id, b in self._bodies.items():
                px, py, pz = self._positions.get(net_id, (0.0, 0.0, 0.0))
                d2 = (x - px) ** 2 + (y - py) ** 2 + (z - pz) ** 2
                if best_d2 is None or d2 < best_d2:
                    best, best_d2 = b, d2
            return best
