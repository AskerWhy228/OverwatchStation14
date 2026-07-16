using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds.Prototypes;

/// <summary>
/// Weighted distribution used to pick a random <see cref="WoundBodyPart"/> when a hit does not
/// already specify a targeted zone. Kept in YAML rather than hard-coded (FR-3).
/// </summary>
[Prototype("hitZoneWeights")]
public sealed partial class HitZoneWeightsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Per-zone selection weight. Zones absent from the dictionary are never selected.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<WoundBodyPart, float> Weights { get; private set; } = new();
}
