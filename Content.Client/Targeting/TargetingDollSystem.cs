using Content.Shared.Medical.Wounds.Components;
using Content.Shared.Targeting;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client.Targeting;

/// <summary>
/// Client-only glue that tells the targeting-doll UI when the local player's selection or wounds change,
/// so the doll updates event-driven rather than polling every frame (FR-A7). Purely a change notifier;
/// the controller reads the actual state and repaints the widget.
/// </summary>
public sealed partial class TargetingDollSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>Raised whenever the doll should repaint (selection changed, wounds changed, body changed).</summary>
    public event Action? DollUpdated;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TargetingComponent, AfterAutoHandleStateEvent>(OnTargetingState);
        SubscribeLocalEvent<TargetingComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<TargetingComponent, LocalPlayerDetachedEvent>(OnDetached);
        SubscribeLocalEvent<WoundableComponent, AfterAutoHandleStateEvent>(OnWoundableState);
    }

    private void OnTargetingState(Entity<TargetingComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (IsLocal(ent))
            DollUpdated?.Invoke();
    }

    private void OnWoundableState(Entity<WoundableComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (IsLocal(ent))
            DollUpdated?.Invoke();
    }

    private void OnAttached(Entity<TargetingComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        DollUpdated?.Invoke();
    }

    private void OnDetached(Entity<TargetingComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        DollUpdated?.Invoke();
    }

    private bool IsLocal(EntityUid uid)
    {
        return _player.LocalEntity == uid;
    }
}
