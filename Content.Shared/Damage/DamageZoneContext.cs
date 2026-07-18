using Content.Shared.Medical.Wounds;

namespace Content.Shared.Damage;

/// <summary>
/// Optional context describing which body zone an application of damage should land on, and how.
/// It rides along with the damage from the source (melee swing, projectile impact, surgery, ...) down to
/// the wound layer, rather than the wound layer reaching back into the attacker's state. See the wound
/// spec §B1: the zone travels <i>with</i> the damage, because damage does not always have an attacker,
/// the target zone may have changed between a shot and its impact, and the interceptor may not hold a
/// reference to the source.
/// </summary>
public sealed class DamageZoneContext
{
    /// <summary>
    /// The aimed zone. <c>null</c> means the source is not a precise/targeted one (explosion, atmos,
    /// falling, reflected damage, ...) and the wound layer should fall back to its weight table.
    /// </summary>
    public WoundBodyPart? Zone;

    /// <summary>
    /// When true the zone is used verbatim with no accuracy roll (surgery, scripted damage). See §B3.
    /// </summary>
    public bool Forced;

    /// <summary>
    /// Whether the accuracy roll should use the ranged hit chance rather than the melee one. Bullets and
    /// thrown items set this; melee leaves it false. Not part of the spec's minimal struct, but the wound
    /// layer needs it to know which of the profile's two hit chances to roll against.
    /// </summary>
    public bool Ranged;

    public DamageZoneContext()
    {
    }

    public DamageZoneContext(WoundBodyPart? zone, bool forced = false, bool ranged = false)
    {
        Zone = zone;
        Forced = forced;
        Ranged = ranged;
    }
}
