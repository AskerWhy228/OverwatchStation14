using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical.Wounds;

/// <summary>
/// A single localised injury on a body zone. Wounds are stored as structs inside a list on
/// <see cref="Components.WoundableComponent"/> rather than as entities: a body can carry up to
/// <c>MaxWounds</c> wounds per zone across many zones and mobs, and an entity-per-wound model would
/// wreck both performance and networking.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public partial struct Wound
{
    /// <summary>
    /// The wound type prototype (defines stages, self-heal behaviour, treatments, etc.).
    /// </summary>
    [DataField(required: true)]
    public ProtoId<WoundPrototype> Type;

    /// <summary>
    /// The zone this wound sits on.
    /// </summary>
    [DataField]
    public WoundBodyPart Part;

    /// <summary>
    /// Accumulated severity of the wound. Drives the discrete stage via the prototype thresholds.
    /// Always &gt;= 0.
    /// </summary>
    [DataField]
    public FixedPoint2 Severity;

    /// <summary>
    /// The amount of raw vanilla damage this wound is responsible for in the body's
    /// <see cref="Damage.Components.DamageableComponent"/>. Used to mirror wound healing back onto the
    /// vanilla damage pool exactly, independent of any severity multipliers. See phase-0 mirroring (FR-2).
    /// </summary>
    [DataField]
    public FixedPoint2 Damage;

    /// <summary>
    /// Current treatment applied to the wound.
    /// </summary>
    [DataField]
    public WoundTreatment Treatment;

    /// <summary>
    /// Bleed rate this wound would produce. Populated in phase 0 but read by nothing yet
    /// (reserved for the future BleedingSystem).
    /// </summary>
    [DataField]
    public FixedPoint2 BleedRate;

    /// <summary>
    /// Whether this is an internal wound. Always false in phase 0.
    /// </summary>
    [DataField]
    public bool Internal;
}

/// <summary>
/// The treatment currently applied to a <see cref="Wound"/>.
/// </summary>
[Serializable, NetSerializable]
public enum WoundTreatment : byte
{
    None,
    Bandaged,
    Salved,
    Sutured,
}
