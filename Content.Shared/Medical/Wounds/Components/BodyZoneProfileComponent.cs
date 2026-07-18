using Content.Shared.Medical.Wounds.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds.Components;

/// <summary>
/// References the <see cref="BodyZoneProfilePrototype"/> for a body (spec §B4.1). The profile lives on the
/// whole target rather than a part because the hit chances are a property of the target, not the zone that
/// happens to get hit. Bodies without this component behave as phase-0 did: every aimed hit is guaranteed,
/// severity multipliers are 1.0, and weighted selection uses the woundable's own weight table.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BodyZoneProfileComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<BodyZoneProfilePrototype> Profile = "DefaultHumanoid";
}
