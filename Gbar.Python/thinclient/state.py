"""状态层：线程安全的世界视图。

仅在包回调线程写入，读取一律走 snapshot() 拿一致性副本。
坐标在进入本层前统一由定点（1:100）换算为米。
"""

import threading
from dataclasses import dataclass

from .config import FIXED_SCALE


def fmt_entity(e) -> str:
    """EntityState → 可读字符串（米制坐标），用于日志文件。"""
    s = FIXED_SCALE
    return (
        f"netId={e.NetId} space={e.SpaceNetId}"
        f" pos=({e.PosX / s:.2f}, {e.PosY / s:.2f}, {e.PosZ / s:.2f})"
        f" vel=({e.VelX / s:.2f}, {e.VelY / s:.2f}, {e.VelZ / s:.2f})"
    )


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
        """增量快照：EntityState 携带绝对状态，直接覆盖；Despawned 重复删除无害。"""
        with self._lock:
            for e in spawned:
                self._entities[e.NetId] = EntityView.from_state(e)
            for e in updated:
                self._entities[e.NetId] = EntityView.from_state(e)
            for net_id in despawned:
                self._entities.pop(net_id, None)
            self.last_applied_tick = curr_tick

    def snapshot(self) -> WorldSnapshot:
        with self._lock:
            return WorldSnapshot(self.last_applied_tick, self.player_net_id, dict(self._entities))
