namespace Gbar.Core.ECS.Components;

public record struct Stats(
    int Strength,
    int Agility,
    int Intelligence,
    int Attack,
    int Defense,
    float MoveSpeed
);
