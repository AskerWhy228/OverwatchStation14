namespace Content.Shared.Medical.Wounds;

/// <summary>
/// Single source of truth mapping <see cref="WoundBodyPart"/> to its Fluent loc id, shared by the wound
/// examine block and the targeting UI so zone names never drift between them.
/// </summary>
public static class WoundZoneNames
{
    public static string GetLocId(WoundBodyPart part)
    {
        return part switch
        {
            WoundBodyPart.Chest => "wound-zone-chest",
            WoundBodyPart.Head => "wound-zone-head",
            WoundBodyPart.LeftArm => "wound-zone-left-arm",
            WoundBodyPart.RightArm => "wound-zone-right-arm",
            WoundBodyPart.LeftHand => "wound-zone-left-hand",
            WoundBodyPart.RightHand => "wound-zone-right-hand",
            WoundBodyPart.LeftLeg => "wound-zone-left-leg",
            WoundBodyPart.RightLeg => "wound-zone-right-leg",
            WoundBodyPart.LeftFoot => "wound-zone-left-foot",
            WoundBodyPart.RightFoot => "wound-zone-right-foot",
            _ => "wound-zone-chest",
        };
    }
}
