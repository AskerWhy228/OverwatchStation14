using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds.Prototypes;

/// <summary>
/// Merged per-body zone profile (spec §B2). A single prototype per body type carrying:
/// the fallback weight table for non-targeted damage, the targeting hit chances and miss fallbacks for
/// aimed damage, the per-zone severity multiplier used to balance headshots (§B4.2), and the display cap
/// used to map wound severity onto the doll's damage tint (FR-A7).
///
/// This supersedes the standalone <see cref="HitZoneWeightsPrototype"/>: a body that carries a
/// <see cref="Components.BodyZoneProfileComponent"/> uses this profile's <see cref="FallbackWeights"/>
/// for weighted (non-targeted) zone selection. Bodies without the component fall back to the weights on
/// their <see cref="Components.WoundableComponent"/>, preserving phase-0 behaviour.
/// </summary>
[Prototype("bodyZoneProfile")]
public sealed partial class BodyZoneProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Severity at which a zone's damage tint reaches full brightness on the targeting doll (FR-A7).
    /// </summary>
    [DataField]
    public float DisplayCap = 50f;

    /// <summary>
    /// Weighted zone distribution for damage that carries no targeting context (traps, falls, damage with
    /// no attacker). This is the phase-0 <c>hitZoneWeights</c> table folded into the profile (§B2).
    /// Zones absent from the dictionary are never selected.
    /// </summary>
    [DataField]
    public Dictionary<WoundBodyPart, float> FallbackWeights = new();

    /// <summary>
    /// Optional specialised weight table for falling damage (§B3 - legs/feet weighted heavier). Falls back
    /// to <see cref="FallbackWeights"/> when unset.
    /// </summary>
    [DataField]
    public Dictionary<WoundBodyPart, float>? FallingWeights;

    /// <summary>
    /// Per-zone targeting data (hit chances, miss fallbacks, severity multiplier). Zones absent here are
    /// treated as an always-hit zone with a 1.0 severity multiplier and no miss fallback.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<WoundBodyPart, BodyZoneTargetInfo> Zones = new();

    public BodyZoneTargetInfo GetZone(WoundBodyPart part)
    {
        return Zones.TryGetValue(part, out var info) ? info : new BodyZoneTargetInfo();
    }
}

/// <summary>
/// Targeting data for a single zone within a <see cref="BodyZoneProfilePrototype"/>.
/// </summary>
[DataDefinition]
public partial struct BodyZoneTargetInfo
{
    /// <summary>Chance a melee hit aimed at this zone actually lands on it. See FR-A4.</summary>
    [DataField]
    public float HitChanceMelee = 1f;

    /// <summary>Chance a ranged hit aimed at this zone actually lands on it. See FR-A4.</summary>
    [DataField]
    public float HitChanceRanged = 1f;

    /// <summary>
    /// Zones the hit deviates to on a failed accuracy roll, picked equiprobably. An empty list means the
    /// zone is always hit (guaranteed - the accuracy roll is skipped entirely).
    /// </summary>
    [DataField]
    public List<WoundBodyPart> MissFallback = new();

    /// <summary>
    /// Multiplier applied to wound severity landed on this zone (§B4.2). The head is >1 (fragile), hands
    /// and feet are &lt;1. Applied to severity only, never to the mirrored raw damage, so it cannot change
    /// vanilla TTK while wound mirroring is still active (§B5).
    /// </summary>
    [DataField]
    public float SeverityMultiplier = 1f;

    public BodyZoneTargetInfo()
    {
    }
}
