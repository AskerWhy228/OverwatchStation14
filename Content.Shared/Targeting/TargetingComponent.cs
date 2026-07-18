using Content.Shared.Medical.Wounds;
using Robust.Shared.GameStates;

namespace Content.Shared.Targeting;

/// <summary>
/// Holds the body zone a mob is currently aiming at, for melee/ranged/medical actions (FR-A1). Lives on
/// game humanoids and any mob that can attack precisely. The value survives hand/item swaps and defaults
/// back to <see cref="WoundBodyPart.Chest"/> on a fresh body (respawn/clone) simply because that is the
/// default of a freshly spawned component.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class TargetingComponent : Component
{
    /// <summary>The currently selected target zone.</summary>
    [DataField, AutoNetworkedField]
    public WoundBodyPart Target = WoundBodyPart.Chest;
}
