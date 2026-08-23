"""天体 on-rails 解析：与服务器 DetTrig + OrbitalSystem 逐 bit 一致（见 GbarNet.md / GbarMotion.md）。

确定性纪律：只用 +−×÷ 与 math.floor（IEEE-754 精确定义），不用 math.sin/cos 等超越函数
（.NET 不保证跨平台逐 bit 一致）。坐标与服务器一致：轨道面取 XY 平面（Z=0），
绝对位置 = 父天体绝对位置 + 本轨道偏移，沿父链递归（恒星 parent_net_id == -1 取原点）。

返回米制双精度；服务器把天体位置存成定点网格（1:100），但天体位置不通过网络互传，
客户端仅为本地显示求值，不需要落到定点网格。
"""

import math

PI = 3.14159265358979323846264338327950288
TWO_PI = 6.28318530717958647692528676655900576
HALF_PI = 1.57079632679489661923132169163975144
_QUARTER_PI = 0.785398163397448309615660845819875721

_KEPLER_ITERATIONS = 6


def _sin(x: float) -> float:
    x -= TWO_PI * math.floor(x / TWO_PI + 0.5)
    sign = 1.0
    if x < 0:
        x = -x
        sign = -1.0
    if x > HALF_PI:
        x = PI - x
    r = _cos_poly(HALF_PI - x) if x > _QUARTER_PI else _sin_poly(x)
    return sign * r


def _cos(x: float) -> float:
    x -= TWO_PI * math.floor(x / TWO_PI + 0.5)
    if x < 0:
        x = -x
    sign = 1.0
    if x > HALF_PI:
        x = PI - x
        sign = -1.0
    r = _sin_poly(HALF_PI - x) if x > _QUARTER_PI else _cos_poly(x)
    return sign * r


def _sin_poly(x: float) -> float:
    x2 = x * x
    return x * (1.0 + x2 * (-1.0 / 6.0 + x2 * (1.0 / 120.0 + x2 * (-1.0 / 5040.0
            + x2 * (1.0 / 362880.0 + x2 * (-1.0 / 39916800.0 + x2 * (1.0 / 6227020800.0
            + x2 * (-1.0 / 1307674368000.0 + x2 * (1.0 / 355687428096000.0)))))))))


def _cos_poly(x: float) -> float:
    x2 = x * x
    return 1.0 + x2 * (-1.0 / 2.0 + x2 * (1.0 / 24.0 + x2 * (-1.0 / 720.0
            + x2 * (1.0 / 40320.0 + x2 * (-1.0 / 3628800.0 + x2 * (1.0 / 479001600.0
            + x2 * (-1.0 / 87178291200.0 + x2 * (1.0 / 20922789888000.0))))))))


def _solve_eccentric_anomaly(mean_anomaly: float, e: float) -> float:
    m = mean_anomaly - TWO_PI * math.floor(mean_anomaly / TWO_PI + 0.5)
    ea = m if e < 0.8 else PI
    for _ in range(_KEPLER_ITERATIONS):
        ea -= (ea - e * _sin(ea) - m) / (1 - e * _cos(ea))
    return ea


def _local_offset(body, t: float) -> tuple:
    """本轨道相对父天体的偏移（米，轨道面 XY）；非法根数返回零偏移（与服务器守卫一致）。"""
    if body.orbital_period <= 0 or body.eccentricity < 0 or body.eccentricity >= 1:
        return 0.0, 0.0
    mean_anomaly = TWO_PI * t / body.orbital_period + body.initial_phase
    e = body.eccentricity
    ea = _solve_eccentric_anomaly(mean_anomaly, e)
    a = body.semi_major_axis
    x = a * (_cos(ea) - e)
    y = a * math.sqrt(1 - e * e) * _sin(ea)
    return x, y


def resolve_bodies(bodies, tick: int, tick_rate: int) -> dict:
    """按父链递归求各天体绝对位置（米），返回 {net_id: (x, y, z)}。

    bodies 为可迭代的天体（需含 net_id / parent_net_id / semi_major_axis /
    eccentricity / orbital_period / initial_phase 属性）。tick_rate 为服务器 tick/s。
    """
    by_id = {b.net_id: b for b in bodies}
    t = tick / tick_rate
    memo = {}

    def absolute(body):
        pos = memo.get(body.net_id)
        if pos is not None:
            return pos
        parent = by_id.get(body.parent_net_id)
        px, py, pz = absolute(parent) if parent is not None else (0.0, 0.0, 0.0)
        ox, oy = _local_offset(body, t)
        pos = (px + ox, py + oy, pz)
        memo[body.net_id] = pos
        return pos

    return {body.net_id: absolute(body) for body in bodies}
