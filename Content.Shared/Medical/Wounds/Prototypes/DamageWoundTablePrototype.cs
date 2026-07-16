using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds.Prototypes;

/// <summary>
/// Maps vanilla damage types onto wound types, with a per-type conversion factor.
/// Damage types with no entry are not converted into wounds and travel the vanilla path untouched
/// (e.g. Poison, Radiation, Bloodloss, Asphyxiation).
/// </summary>
[Prototype("damageWoundTable")]
public sealed partial class DamageWoundTablePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public Dictionary<ProtoId<DamageTypePrototype>, DamageWoundEntry> Entries { get; private set; } = new();
}

/// <summary>
/// A single entry in a <see cref="DamageWoundTablePrototype"/>.
/// </summary>
[DataDefinition]
public partial struct DamageWoundEntry
{
    /// <summary>
    /// The wound type created/merged for this damage type.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<WoundPrototype> Wound;

    /// <summary>
    /// Severity added per point of damage of this type.
    /// </summary>
    [DataField]
    public float SeverityPerDamage = 1.0f;
}
