using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds.Prototypes;

/// <summary>
/// Defines a type of wound: how its severity maps to discrete stages, how it self-heals,
/// which treatments apply, and (reserved for phase 1) its bleed factor.
/// </summary>
[Prototype("wound")]
public sealed partial class WoundPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Loc key for the generic wound name (used as a fallback if a stage has no name).
    /// </summary>
    [DataField]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>
    /// The canonical vanilla damage type this wound mirrors back to when healed.
    /// See FR-2 (mirroring). For phase 0 this is a single type per wound.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<DamageTypePrototype> DamageType { get; private set; } = default!;

    /// <summary>
    /// Ordered stage thresholds. The active stage is the last entry whose threshold is &lt;= severity.
    /// Must contain at least one entry (threshold 0) and be sorted ascending.
    /// </summary>
    [DataField(required: true)]
    public List<WoundStage> Stages { get; private set; } = new();

    /// <summary>
    /// A wound with severity strictly below this value heals on its own (provided it is treated).
    /// </summary>
    [DataField]
    public FixedPoint2 SelfHealBelow { get; private set; } = FixedPoint2.Zero;

    /// <summary>
    /// Severity removed per self-heal tick.
    /// </summary>
    [DataField]
    public FixedPoint2 SelfHealRate { get; private set; } = FixedPoint2.New(0.5);

    /// <summary>
    /// Which treatment kinds may be applied to this wound.
    /// </summary>
    [DataField]
    public List<WoundTreatment> TreatableBy { get; private set; } = new();

    /// <summary>
    /// BleedRate = Severity * BleedFactor. Populated for phase 1; unused in phase 0.
    /// </summary>
    [DataField]
    public float BleedFactor { get; private set; }

    /// <summary>
    /// Returns the loc key of the stage matching the given severity.
    /// </summary>
    public LocId GetStageName(FixedPoint2 severity)
    {
        var name = Name;
        foreach (var stage in Stages)
        {
            if (severity < stage.Threshold)
                break;
            name = stage.Name;
        }

        return name;
    }
}

/// <summary>
/// A single discrete step of a wound, keyed by a severity threshold.
/// </summary>
[DataDefinition]
public partial struct WoundStage
{
    /// <summary>
    /// Minimum severity for this stage to be active.
    /// </summary>
    [DataField(required: true)]
    public FixedPoint2 Threshold;

    /// <summary>
    /// Loc key for this stage's display name.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;
}
