using Content.Shared.Medical.Wounds.Components;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical.Wounds;

/// <summary>
/// Raised on a body when a new wound is created. Extension point for future phases (bleeding, pain, ...).
/// </summary>
[ByRefEvent]
public readonly record struct WoundAddedEvent(Entity<WoundableComponent> Body, Wound Wound);

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
/// DoAfter for applying a <see cref="WoundTreatmentItemComponent"/> to a target.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class WoundTreatmentDoAfterEvent : Content.Shared.DoAfter.SimpleDoAfterEvent
{
}
