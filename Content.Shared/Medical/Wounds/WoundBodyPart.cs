using Robust.Shared.Serialization;

namespace Content.Shared.Medical.Wounds;

/// <summary>
/// Logical body zone a <see cref="Wound"/> is located on.
/// This fork's bodies do not have individual limb entities, so wound localisation
/// is tracked purely as a zone enum on the body's <see cref="Components.WoundableComponent"/>.
/// </summary>
[Serializable, NetSerializable]
public enum WoundBodyPart : byte
{
    Chest,
    Head,
    LeftArm,
    RightArm,
    LeftHand,
    RightHand,
    LeftLeg,
    RightLeg,
    LeftFoot,
    RightFoot,
}
