using Content.Shared.Medical.Wounds;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Stamps the body zone a projectile (or thrown item) was aimed at, captured at the moment of firing
/// (FR-A5). The wound layer reads it on impact so that switching target zones mid-flight cannot change
/// where an in-flight shot lands (acceptance #3). Distinct from the vanilla <c>TargetedProjectileComponent</c>,
/// which homes a projectile onto a target <i>entity</i> rather than recording a body zone.
/// </summary>
[RegisterComponent]
public sealed partial class ProjectileZoneComponent : Component
{
    [DataField]
    public WoundBodyPart Zone;
}
