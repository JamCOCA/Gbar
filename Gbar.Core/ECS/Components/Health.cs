using Gbar.Core.Type.Math;

namespace Gbar.Core.ECS.Components;

public readonly record struct Health(BoundedInt Value)
{
    public int Current => Value.Current;
    public int Max => Value.Max;
    public float Ratio => Value.Ratio;
    public bool IsDead => Value.IsEmpty;
    public bool IsFull => Value.IsFull;

    public readonly Health WithMax(int max) => new(Value.WithMax(max));
    public readonly Health Add(int amount) => new(Value.Add(amount));
    public readonly Health Reduce(int amount) => new(Value.Reduce(amount));

    public override string ToString() => Value.ToString();
}
