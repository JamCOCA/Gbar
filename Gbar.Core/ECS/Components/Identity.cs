using Gbar.Core.Type.Entity;


namespace Gbar.Core.ECS.Components;
public readonly record struct Identity(
    ulong NetworkId,
    EntityKind Kind,
    int TemplateId,
    int TeamId,
    ulong OwnerNetworkId,
    string? CustomName
)
{
    public readonly bool HasOwner => OwnerNetworkId != 0;

    public readonly string DisplayName => CustomName ?? $"[{Kind}_{TemplateId}]";

    public readonly bool IsPlayer => Kind == EntityKind.Player;

    public readonly bool IsMonster => Kind == EntityKind.Monster;

    public readonly bool IsOwnedBy(ulong ownerNetworkId) => OwnerNetworkId == ownerNetworkId;

    public readonly Identity WithTeam(int teamId) => this with { TeamId = teamId };

    public readonly Identity WithOwner(ulong ownerNetworkId) => this with { OwnerNetworkId = ownerNetworkId };

    public readonly Identity WithName(string? name) => this with { CustomName = name };
}
