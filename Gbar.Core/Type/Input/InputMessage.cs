namespace Gbar.Core.Type.Input;

public readonly struct InputMessage
{
    public readonly InputCommand Command;
    public readonly int EntityId;
    public readonly float ParamX;
    public readonly float ParamY;
    public readonly float ParamZ;

    public InputMessage(InputCommand command, int entityId = 0, float paramX = 0, float paramY = 0, float paramZ = 0)
    {
        Command = command;
        EntityId = entityId;
        ParamX = paramX;
        ParamY = paramY;
        ParamZ = paramZ;
    }
}
