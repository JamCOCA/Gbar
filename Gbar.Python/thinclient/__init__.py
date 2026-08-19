"""ThinClient 包：Gbar.Net 正式测试客户端（对端为 C# GameServer）。

模块划分：
    _bootstrap  pythonnet 运行时初始化（必须最先执行，由本 __init__ 触发）
    config      常量（地址 / 定点缩放 / UI 参数）
    state       线程安全的世界视图（EntityView / WorldSnapshot / WorldState）
    recorder    收包记录器（全量写 log.txt，内存留最近 N 条供 UI）
    events      注入点（生命周期钩子列表）
    handler     协议层（IPacketHandler 实现，依赖 Gbar.Net）
    client      客户端壳（NetClient 生命周期）
    ui          tkinter 视图（画布 + 实体列表/监视栏 + 收包日志面板）

线程模型：包回调与事件钩子运行在 NetClient 后台轮询线程，钩子内禁止直接操作 UI；
UI 在主线程通过 WorldState.snapshot() / PacketLogger.recent() 拿一致性副本。
"""

from . import _bootstrap  # noqa: F401  必须先初始化 CoreCLR 运行时，再导入依赖 Gbar.Net 的子模块
