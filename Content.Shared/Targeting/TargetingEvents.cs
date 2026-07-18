using Content.Shared.Medical.Wounds;
using Robust.Shared.Serialization;

namespace Content.Shared.Targeting;

/// <summary>
/// Raised predictively by the client (from the targeting-doll widget click, or a keybind) to change the
/// player's selected target zone. Validated and applied server-side; the change is predicted so the doll
/// highlights instantly (NFR-A1). Applies to the sender's attached entity only.
/// </summary>
[Serializable, NetSerializable]
public sealed class TargetZoneChangeEvent : EntityEventArgs
{
    public WoundBodyPart Zone;

    public TargetZoneChangeEvent(WoundBodyPart zone)
    {
        Zone = zone;
    }
}
