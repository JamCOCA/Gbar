"""协议层：IPacketHandler 实现，把包翻译成 WorldState 变更 + PacketLogger 记录 + 事件。

C→S 方向（OnHello/OnAck）客户端收不到，但接口方法为抽象成员必须全部实现。
"""

import logging
import threading

from Gbar.Net.Protocol import IPacketHandler  # type: ignore[import]

from .events import ClientEvents
from .recorder import PacketLogger
from .state import WorldState, fmt_entity

log = logging.getLogger("ThinClient")


class ClientPacketHandler(IPacketHandler):
    __namespace__ = "ThinClient"

    def __init__(self, state: WorldState, events: ClientEvents, packets: PacketLogger):
        super().__init__()
        self.state = state
        self.events = events
        self.packets = packets
        self.net = None  # ThinClient 装配时注入，用于回执 Ack
        self.handshake_done = threading.Event()

    # ---- 内部 ----

    def _ack(self):
        """应用快照后立即回执（LiteNetLib Send 线程安全，可在回调线程调用）。

        服务器的 Delta 以"最后一次 Ack 的 tick"为基线（NetSyncSystem），
        慢速回执会导致基线滞后、Delta 被批量丢弃。
        """
        if self.net is not None:
            tick = self.state.snapshot().tick
            self.packets.record(tick, "C→S", "Ack", f"lastAppliedTick={tick}")
            self.net.SendAck(tick)

    # ---- S→C 包 ----

    def OnWelcome(self, packet):
        # ServerTick 仅用于两端 tick 对时（天体轨道求值基准），不是快照基线，不得覆盖
        self.state.set_player(packet.PlayerNetId)
        self.handshake_done.set()
        self.packets.record(
            packet.ServerTick, "S→C", "Welcome",
            f"cid={packet.ClientId} tickRate={packet.TickRate} serverTick={packet.ServerTick} playerNetId={packet.PlayerNetId}",
        )
        self.events.emit(self.events.on_handshake, packet)

    def OnGalaxy(self, packet):
        tick = self.state.snapshot().tick
        self.packets.record(tick, "S→C", "Galaxy", f"bodies={len(packet.Bodies)}")
        for body in packet.Bodies:
            self.packets.detail(f"netId={body.NetId} parent={body.ParentNetId} mu={body.Mu:.3g}")
        self.events.emit(self.events.on_galaxy, packet)

    def OnKeyframe(self, packet):
        self.state.apply_keyframe(packet.Tick, packet.Entities)
        self.packets.record(packet.Tick, "S→C", "Keyframe", f"tick={packet.Tick} entities={len(packet.Entities)}")
        for e in packet.Entities:
            self.packets.detail(fmt_entity(e))
        self._ack()
        self.events.emit(self.events.on_snapshot, self.state.snapshot())

    def OnDelta(self, packet):
        """校验规则：BaseTick <= 本地 tick < CurrTick 即可应用（绝对状态 + 幂等删除，
        基线滞后不影响正确性）；BaseTick > 本地 tick 说明中间漏包，丢弃并回执对齐。
        大包由传输层自动分片重组（PacketCodec/FragmentAssembler），本层拿到的已是完整包。"""
        local = self.state.snapshot().tick
        self.packets.record(
            packet.CurrTick, "S→C", "Delta",
            f"base={packet.BaseTick} curr={packet.CurrTick} local={local}"
            f" spawned={len(packet.Spawned)} updated={len(packet.Updated)} despawned={len(packet.Despawned)}",
        )
        for e in packet.Spawned:
            self.packets.detail("+ " + fmt_entity(e))
        for e in packet.Updated:
            self.packets.detail("~ " + fmt_entity(e))
        for net_id in packet.Despawned:
            self.packets.detail(f"- netId={net_id}")
        if packet.CurrTick <= local:
            return  # 过期包（Sequenced 通道理论上已去重，防御性判断）
        if packet.BaseTick > local:
            log.warning("S→C Delta curr=%d 基线空洞（local=%d < base=%d），丢弃", packet.CurrTick, local, packet.BaseTick)
            self._ack()  # 回执让服务器基线追平本地，避免死锁
            return
        self.state.apply_delta(packet.CurrTick, packet.Spawned, packet.Updated, packet.Despawned)
        self._ack()
        self.events.emit(self.events.on_snapshot, self.state.snapshot())

    def OnBye(self, packet):
        tick = self.state.snapshot().tick
        self.packets.record(tick, "S→C", "Bye", f"reason={packet.Reason}")
        self.events.emit(self.events.on_bye, packet.Reason)

    # ---- C→S 包（客户端不接收，空实现） ----

    def OnHello(self, packet):
        pass

    def OnAck(self, packet):
        pass
