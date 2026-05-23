using Gbar.Core.Type;

namespace Gbar.Core.ECS.Components;

public readonly record struct Mana(BoundedInt Value)
{
    public int Current => Value.Current;
    public int Max => Value.Max;
    public float Ratio => Value.Ratio;
    public bool IsEmpty => Value.IsEmpty;
    public bool IsFull => Value.IsFull;

    public readonly Mana WithMax(int max) => new(Value.WithMax(max));
    public readonly Mana Add(int amount) => new(Value.Add(amount));
    public readonly Mana Reduce(int amount) => new(Value.Reduce(amount));

    public override string ToString() => Value.ToString();
}
