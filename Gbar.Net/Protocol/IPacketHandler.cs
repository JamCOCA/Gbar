namespace Gbar.Net.Protocol;

/// <summary>
/// 包分发回调接口。所有方法带默认空实现，实现方只重写自己关心的包：
/// 服务器实现 OnHello/OnAck/OnBye，客户端实现 OnWelcome/OnKeyframe/OnDelta。
/// 用接口回调而非返回 object 装箱，分发路径零分配。
/// </summary>
public interface IPacketHandler
{
    void OnHello(in HelloPacket packet) { }
    void OnWelcome(in WelcomePacket packet) { }
    void OnKeyframe(in KeyframePacket packet) { }
    void OnDelta(in DeltaPacket packet) { }
    void OnAck(in AckPacket packet) { }
    void OnBye(in ByePacket packet) { }
}
