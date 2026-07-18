using Content.Shared.Medical.Wounds.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds.Components;

/// <summary>
/// Tracks localised wounds for a body. In this fork bodies have no individual limb entities, so all
/// zones are tracked on a single component on the mob; each <see cref="Wound"/> records its own
/// <see cref="WoundBodyPart"/>. Wounds are a parallel accounting layer alongside the vanilla
/// <see cref="Damage.Components.DamageableComponent"/> and never change its balance in phase 0.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WoundableComponent : Component
{
    /// <summary>
    /// All wounds on the body, across every zone.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<Wound> Wounds = new();

    /// <summary>
    /// Maximum number of distinct wounds allowed per zone before merging is forced.
    /// </summary>
    [DataField]
    public int MaxWounds = 6;

    /// <summary>
    /// Multiplier applied to incoming severity (e.g. the head is more fragile).
    /// </summary>
    [DataField]
    public float SeverityMultiplier = 1.0f;

    /// <summary>
    /// Damage-to-wound conversion table for this body.
    /// </summary>
    [DataField]
    public ProtoId<DamageWoundTablePrototype> DamageTable = "DefaultHumanoid";

    /// <summary>
    /// Weighted zone distribution used when a hit does not specify a zone.
    /// </summary>
    [DataField]
    public ProtoId<HitZoneWeightsPrototype> HitZoneWeights = "DefaultHumanoid";
}
