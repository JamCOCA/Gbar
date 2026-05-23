namespace Gbar.Core.Type;

// 有限int-用于血量和魔法等即时状态
public readonly record struct BoundedInt(int Current, int Max)
{
    public readonly float Ratio => Max > 0 ? (float)Current / Max : 0;
    public readonly bool IsEmpty => Current <= 0;
    public readonly bool IsFull => Current >= Max;

    public readonly BoundedInt WithMax(int max)
    {
        var current = Current > max ? max : Current;
        return new BoundedInt(current, max);
    }

    public readonly BoundedInt Add(int amount)
    {
        var current = Current + amount;
        if (current > Max) current = Max;
        return new BoundedInt(current, Max);
    }

    public readonly BoundedInt Reduce(int amount)
    {
        var current = Current - amount;
        if (current < 0) current = 0;
        return new BoundedInt(current, Max);
    }

    public override string ToString() => $"{Current}/{Max}";
}
