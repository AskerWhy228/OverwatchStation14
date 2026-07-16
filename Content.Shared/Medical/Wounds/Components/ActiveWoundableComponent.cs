namespace Content.Shared.Medical.Wounds.Components;

/// <summary>
/// Marker added to a <see cref="WoundableComponent"/> entity while it has at least one wound, and
/// removed once the wound list empties. The self-heal tick iterates only over entities with this
/// component so that healthy mobs cost zero iterations (FR-6 optimisation).
/// </summary>
[RegisterComponent]
public sealed partial class ActiveWoundableComponent : Component
{
}
