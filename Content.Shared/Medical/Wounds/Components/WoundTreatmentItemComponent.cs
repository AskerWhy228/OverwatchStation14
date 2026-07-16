using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds.Components;

/// <summary>
/// An item (bandage, ointment, ...) that treats wounds. Applying it to a target picks the most severe
/// applicable wound, sets its treatment status and instantly removes a flat amount of severity.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WoundTreatmentItemComponent : Component
{
    /// <summary>
    /// Wound types this item can treat.
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<WoundPrototype>> Treats = new();

    /// <summary>
    /// The treatment status this item applies.
    /// </summary>
    [DataField]
    public WoundTreatment Treatment = WoundTreatment.Bandaged;

    /// <summary>
    /// Severity instantly removed on application.
    /// </summary>
    [DataField]
    public FixedPoint2 InstantHeal = FixedPoint2.New(3);

    /// <summary>
    /// DoAfter delay when treating another entity.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Multiplier applied to <see cref="Delay"/> when treating yourself.
    /// </summary>
    [DataField]
    public float SelfDelayMultiplier = 2f;
}
