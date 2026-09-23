"""状态层：线程安全的世界视图。

仅在包回调线程写入，读取一律走 snapshot() 拿一致性副本。
坐标在进入本层前统一由定点（1:100）换算为米。
"""

import threading
from dataclasses import dataclass

from .config import FIXED_SCALE, VELOCITY_SCALE
from .celestial_index import CelestialIndex
from .orbital import resolve_bodies

# EntityFields 掩码位（与 C# Gbar.Net.Protocol.EntityFields 保持一致）
F_SPACE = 1 << 0
F_POSX = 1 << 1
F_POSY = 1 << 2
F_POSZ = 1 << 3
F_VELX = 1 << 4
F_VELY = 1 << 5
F_VELZ = 1 << 6


def fmt_entity(e) -> str:
    """EntityState → 可读字符串（米制坐标），用于日志文件。"""
    s = FIXED_SCALE
    vs = VELOCITY_SCALE
    return (
        f"netId={e.NetId} space={e.SpaceNetId}"
        f" pos=({e.PosX / s:.2f}, {e.PosY / s:.2f}, {e.PosZ / s:.2f})"
        f" vel=({e.VelX / vs:.2f}, {e.VelY / vs:.2f}, {e.VelZ / vs:.2f})"
    )


def fmt_delta(d) -> str:
    """EntityDelta → 可读字符串，只显示掩码指示的变化字段。"""
    s = FIXED_SCALE
    vs = VELOCITY_SCALE
    parts = [f"netId={d.NetId} mask=0x{d.Mask:02x}"]
    if d.Mask & F_SPACE:
        parts.append(f"space={d.SpaceNetId}")
    if d.Mask & F_POSX:
        parts.append(f"x={d.PosX / s:.2f}")
    if d.Mask & F_POSY:
        parts.append(f"y={d.PosY / s:.2f}")
    if d.Mask & F_POSZ:
        parts.append(f"z={d.PosZ / s:.2f}")
    if d.Mask & F_VELX:
        parts.append(f"vx={d.VelX / vs:.2f}")
    if d.Mask & F_VELY:
        parts.append(f"vy={d.VelY / vs:.2f}")
    if d.Mask & F_VELZ:
        parts.append(f"vz={d.VelZ / vs:.2f}")
    return " ".join(parts)


@dataclass(frozen=True)
class EntityView:
    """单个同步实体的米制视图（由 EntityState 定点坐标换算而来）。"""

    net_id: int
    space_net_id: int  # 所属空间 NetId，0 = 全局域
    x: float
    y: float
    z: float
    vx: float
    vy: float
    vz: float

    @classmethod
    def from_state(cls, e) -> "EntityView":
        s = FIXED_SCALE
        vs = VELOCITY_SCALE
        return cls(e.NetId, e.SpaceNetId, e.PosX / s, e.PosY / s, e.PosZ / s, e.VelX / vs, e.VelY / vs, e.VelZ / vs)


@dataclass(frozen=True)
class CelestialBody:
    """天体静态根数（Galaxy 包下发），位置由轨道解析按 tick 现算，不进快照。"""

    net_id: int
    parent_net_id: int  # 母天体 NetId；-1 = 恒星（轨道中心）
    mu: float
    surface_radius: float
    soi_radius: float
    angular_velocity: float
    initial_rotation: float
    semi_major_axis: float
    eccentricity: float
    orbital_period: float
    initial_phase: float

    @classmethod
    def from_state(cls, b) -> "CelestialBody":
        return cls(
            b.NetId, b.ParentNetId, b.Mu, b.SurfaceRadius, b.SoiRadius,
            b.AngularVelocity, b.InitialRotation, b.SemiMajorAxis,
            b.Eccentricity, b.OrbitalPeriod, b.InitialPhase,
        )


@dataclass(frozen=True)
class CelestialView:
    """天体在某一 tick 的解析视图（米制绝对位置 + 半径 + 恒星标记）。"""

    net_id: int
    parent_net_id: int
    surface_radius: float
    soi_radius: float
    x: float
    y: float
    z: float


@dataclass(frozen=True)
class WorldSnapshot:
    """WorldState 的一致性副本，供 UI / 测试钩子消费。"""

    tick: int
    player_net_id: int
    entities: dict  # NetId -> EntityView
    tick_rate: int
    bodies: dict  # NetId -> CelestialBody


class WorldState:
    """线程安全的世界视图。仅在包回调线程写入，读取一律走 snapshot()。"""

    def __init__(self):
        self._lock = threading.Lock()
        self._entities = {}  # NetId -> EntityView
        self._bodies = {}  # NetId -> CelestialBody
        self.index = CelestialIndex()  # 天体引力系索引（与实体分离）
        self.last_applied_tick = 0  # 已应用的最新快照 tick（快照基线，仅由 Keyframe/Delta 推进）
        self.player_net_id = 0
        self.tick_rate = 0  # 服务器 tick/s（Welcome 下发，用于天体轨道求值）

    def set_welcome(self, tick_rate: int, player_net_id: int):
        with self._lock:
            self.tick_rate = tick_rate
            self.player_net_id = player_net_id

    def set_galaxy(self, bodies) -> None:
        """星图表：一次性存储全部天体根数（Galaxy 包）并重建引力系索引。"""
        with self._lock:
            self._bodies = {b.NetId: CelestialBody.from_state(b) for b in bodies}
            view = list(self._bodies.values())
        self.index.set_bodies(view)

    def resolve_celestials(self, tick: int) -> dict:
        """按父链递归求各天体在指定 tick 的绝对位置，返回 {NetId: CelestialView}，并刷新引力系索引。"""
        with self._lock:
            bodies = dict(self._bodies)
            rate = self.tick_rate
        if not bodies or rate <= 0:
            return {}
        positions = resolve_bodies(bodies.values(), tick, rate)
        self.index.rebuild_positions(positions)
        return {
            net_id: CelestialView(b.net_id, b.parent_net_id, b.surface_radius, b.soi_radius,
                                  positions[net_id][0], positions[net_id][1], positions[net_id][2])
            for net_id, b in bodies.items()
        }

    def apply_keyframe(self, tick: int, entities) -> None:
        """全量快照：重建实体表。"""
        views = {e.NetId: EntityView.from_state(e) for e in entities}
        with self._lock:
            self._entities = views
            self.last_applied_tick = tick

    def apply_delta(self, curr_tick: int, spawned, updated, despawned) -> None:
        """增量快照：Spawned 为完整 EntityState 直接覆盖；Updated 为 EntityDelta，按掩码合并到现有实体；Despawned 幂等删除。"""
        with self._lock:
            for e in spawned:
                self._entities[e.NetId] = EntityView.from_state(e)
            for d in updated:
                self._apply_delta(d)
            for net_id in despawned:
                self._entities.pop(net_id, None)
            self.last_applied_tick = curr_tick

    def _apply_delta(self, d) -> None:
        """把一条字段级增量按掩码合并进现有实体（缺基线时用默认值补全，防御性）。"""
        s = FIXED_SCALE
        vs = VELOCITY_SCALE
        cur = self._entities.get(d.NetId) or EntityView(d.NetId, 0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0)
        m = d.Mask
        self._entities[d.NetId] = EntityView(
            d.NetId,
            d.SpaceNetId if m & F_SPACE else cur.space_net_id,
            d.PosX / s if m & F_POSX else cur.x,
            d.PosY / s if m & F_POSY else cur.y,
            d.PosZ / s if m & F_POSZ else cur.z,
            d.VelX / vs if m & F_VELX else cur.vx,
            d.VelY / vs if m & F_VELY else cur.vy,
            d.VelZ / vs if m & F_VELZ else cur.vz,
        )

    def snapshot(self) -> WorldSnapshot:
        with self._lock:
            return WorldSnapshot(
                self.last_applied_tick, self.player_net_id, dict(self._entities),
                self.tick_rate, dict(self._bodies),
            )
