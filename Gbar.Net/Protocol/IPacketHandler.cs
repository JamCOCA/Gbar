using Gbar.Net.Protocol.Packets;

namespace Gbar.Net.Protocol;

/// <summary>
/// 包分发回调接口。服务器实现 C→S 方向（OnHello/OnAck/OnBye），
/// 客户端实现 S→C 方向（OnWelcome/OnKeyframe/OnDelta）。
/// 用接口回调而非返回 object 装箱，分发路径零分配。
/// 注意：不使用默认接口方法（DIM）——pythonnet 生成的接口代理无法覆盖 DIM，
/// C# 侧调用会命中默认空实现而非 Python 重写，回调被静默吞掉。
/// </summary>
public interface IPacketHandler
{
    void OnHello(HelloPacket packet);
    void OnAuth(AuthPacket packet);
    void OnWelcome(WelcomePacket packet);
    void OnAuthFail(AuthFailPacket packet);
    void OnKeyframe(KeyframePacket packet);
    void OnDelta(DeltaPacket packet);
    void OnAck(AckPacket packet);
    void OnBye(ByePacket packet);
}
