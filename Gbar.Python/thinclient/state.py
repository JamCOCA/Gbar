"""状态层：线程安全的世界视图。

仅在包回调线程写入，读取一律走 snapshot() 拿一致性副本。
坐标在进入本层前统一由定点（1:100）换算为米。
"""

import threading
from dataclasses import dataclass

from .config import FIXED_SCALE

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
    return (
        f"netId={e.NetId} space={e.SpaceNetId}"
        f" pos=({e.PosX / s:.2f}, {e.PosY / s:.2f}, {e.PosZ / s:.2f})"
        f" vel=({e.VelX / s:.2f}, {e.VelY / s:.2f}, {e.VelZ / s:.2f})"
    )


def fmt_delta(d) -> str:
    """EntityDelta → 可读字符串，只显示掩码指示的变化字段。"""
    s = FIXED_SCALE
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
        parts.append(f"vx={d.VelX / s:.2f}")
    if d.Mask & F_VELY:
        parts.append(f"vy={d.VelY / s:.2f}")
    if d.Mask & F_VELZ:
        parts.append(f"vz={d.VelZ / s:.2f}")
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
        return cls(e.NetId, e.SpaceNetId, e.PosX / s, e.PosY / s, e.PosZ / s, e.VelX / s, e.VelY / s, e.VelZ / s)


@dataclass(frozen=True)
class WorldSnapshot:
    """WorldState 的一致性副本，供 UI / 测试钩子消费。"""

    tick: int
    player_net_id: int
    entities: dict  # NetId -> EntityView


class WorldState:
    """线程安全的世界视图。仅在包回调线程写入，读取一律走 snapshot()。"""

    def __init__(self):
        self._lock = threading.Lock()
        self._entities = {}  # NetId -> EntityView
        self.last_applied_tick = 0  # 已应用的最新快照 tick（快照基线，仅由 Keyframe/Delta 推进）
        self.player_net_id = 0

    def set_player(self, net_id: int):
        with self._lock:
            self.player_net_id = net_id

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
        cur = self._entities.get(d.NetId) or EntityView(d.NetId, 0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0)
        m = d.Mask
        self._entities[d.NetId] = EntityView(
            d.NetId,
            d.SpaceNetId if m & F_SPACE else cur.space_net_id,
            d.PosX / s if m & F_POSX else cur.x,
            d.PosY / s if m & F_POSY else cur.y,
            d.PosZ / s if m & F_POSZ else cur.z,
            d.VelX / s if m & F_VELX else cur.vx,
            d.VelY / s if m & F_VELY else cur.vy,
            d.VelZ / s if m & F_VELZ else cur.vz,
        )

    def snapshot(self) -> WorldSnapshot:
        with self._lock:
            return WorldSnapshot(self.last_applied_tick, self.player_net_id, dict(self._entities))
