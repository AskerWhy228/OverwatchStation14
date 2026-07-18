using Content.Shared.Medical.Wounds.Components;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical.Wounds;

/// <summary>
/// Raised on a body when a new wound is created. Extension point for future phases (bleeding, pain, ...).
/// <paramref name="WasTargeted"/> records whether the wound came from an aimed hit (§B4.3), for future
/// combat analytics / admin attack logs ("N aimed head hits").
/// </summary>
[ByRefEvent]
public readonly record struct WoundAddedEvent(Entity<WoundableComponent> Body, Wound Wound, bool WasTargeted = false);

/// <summary>
/// Raised on a body when an existing wound's severity or treatment changes.
/// </summary>
[ByRefEvent]
public readonly record struct WoundChangedEvent(Entity<WoundableComponent> Body, Wound Wound);

/// <summary>
/// Raised on a body when a wound is fully healed and removed.
/// </summary>
[ByRefEvent]
public readonly record struct WoundRemovedEvent(Entity<WoundableComponent> Body, Wound Wound);

/// <summary>
/// Raised on a target while resolving an aimed hit, so content can modify the per-zone accuracy chance
/// (FR-A4 modifiers: sprinting reduces it, aiming raises it, ...). The wound system itself forces the
/// chance to 1.0 for downed/stunned targets before raising this. <see cref="Chance"/> is clamped to
/// 0..1 after the event.
/// </summary>
[ByRefEvent]
public record struct GetBodyZoneHitChanceEvent(WoundBodyPart Zone, bool Ranged, float Chance);

/// <summary>
/// DoAfter for applying a <see cref="WoundTreatmentItemComponent"/> to a target.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class WoundTreatmentDoAfterEvent : Content.Shared.DoAfter.SimpleDoAfterEvent
{
}
