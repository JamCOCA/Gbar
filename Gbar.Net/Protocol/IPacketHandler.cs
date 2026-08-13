namespace Gbar.Net.Protocol;

/// <summary>
/// 鍖呭垎鍙戝洖璋冩帴鍙ｃ€傛湇鍔″櫒瀹炵幇 OnHello/OnAck/OnBye锛屽鎴风瀹炵幇 OnWelcome/OnKeyframe/OnDelta銆?/// 鐢ㄦ帴鍙ｅ洖璋冭€岄潪杩斿洖 object 瑁呯锛屽垎鍙戣矾寰勯浂鍒嗛厤銆?/// 娉細涓嶄娇鐢ㄩ粯璁ゆ帴鍙ｆ柟娉曪紙DIM锛夆€斺€攑ythonnet 鐢熸垚鐨勬帴鍙ｄ唬鐞嗘棤娉曡鐩?DIM锛?/// C# 渚ц皟鐢ㄤ細鍛戒腑榛樿绌哄疄鐜拌€岄潪 Python 閲嶅啓锛屽洖璋冭闈欓粯鍚炴帀銆?/// </summary>
public interface IPacketHandler
{
    void OnHello(HelloPacket packet);
    void OnWelcome(WelcomePacket packet);
    void OnKeyframe(KeyframePacket packet);
    void OnDelta(DeltaPacket packet);
    void OnAck(AckPacket packet);
    void OnBye(ByePacket packet);
}
