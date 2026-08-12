using Arch.System;

namespace Gbar.Core;

/// <summary>
/// 变步长系统组：按注册顺序驱动组内系统，帧末回放命令缓冲。
/// 生命周期（Initialize/BeforeUpdate/Update/AfterUpdate/Dispose）均为 virtual，可重写自定义。
/// </summary>
public class UpdateSystemGroup : Group<float>
{
    /// <summary>本组的命令缓冲 Hub，帧末由 <see cref="GameServer"/> 统一回放。</summary>
    public CommandBufferHub Commands { get; } = new();

    public UpdateSystemGroup(string name = "Update") : base(name)
    {
    }

    /// <summary>Group.Dispose 非 virtual 无法 override，用 new 隐藏以连带释放 Commands。</summary>
    public new void Dispose()
    {
        base.Dispose();
        Commands.Dispose();
    }
}
