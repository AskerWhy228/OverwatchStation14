using Content.Shared.Input;
using Content.Shared.Medical.Wounds;
using Content.Shared.Popups;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared.Targeting;

/// <summary>
/// Shared logic for the body-zone targeting selection (Part A mechanics). Runs on both client and server:
/// keybinds are registered here (like the knockdown toggle) so the input is predicted on the client and
/// authoritative on the server, and the widget click arrives as a predicted <see cref="TargetZoneChangeEvent"/>.
/// The drawn doll is a pure consumer of this state and can be added or rolled back independently (§B6).
/// </summary>
public sealed partial class TargetingSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Zone order used by the cycle keybind (doll layout order, top to bottom).</summary>
    private static readonly WoundBodyPart[] CycleOrder =
    {
        WoundBodyPart.Head,
        WoundBodyPart.Chest,
        WoundBodyPart.LeftArm,
        WoundBodyPart.RightArm,
        WoundBodyPart.LeftHand,
        WoundBodyPart.RightHand,
        WoundBodyPart.LeftLeg,
        WoundBodyPart.RightLeg,
        WoundBodyPart.LeftFoot,
        WoundBodyPart.RightFoot,
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAllEvent<TargetZoneChangeEvent>(OnZoneChangeRequest);

        // Direct zone keybinds + a cycle key for players without a numpad (FR-A3). Unbound by default to
        // avoid clashing with the numpad camera/zoom binds (acceptance #6); rebindable via the controls menu.
        var builder = CommandBinds.Builder;
        foreach (var (function, zone) in ZoneKeyBinds())
        {
            var captured = zone;
            builder.Bind(function, InputCmdHandler.FromDelegate(session => HandleZoneKey(session, captured), handle: false));
        }

        builder
            .Bind(ContentKeyFunctions.TargetCycle, InputCmdHandler.FromDelegate(s => Cycle(s, 1), handle: false))
            .Bind(ContentKeyFunctions.TargetCycleReverse, InputCmdHandler.FromDelegate(s => Cycle(s, -1), handle: false))
            .Register<TargetingSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<TargetingSystem>();
    }

    private static IEnumerable<(BoundKeyFunction, WoundBodyPart)> ZoneKeyBinds()
    {
        yield return (ContentKeyFunctions.TargetHead, WoundBodyPart.Head);
        yield return (ContentKeyFunctions.TargetChest, WoundBodyPart.Chest);
        yield return (ContentKeyFunctions.TargetLeftArm, WoundBodyPart.LeftArm);
        yield return (ContentKeyFunctions.TargetRightArm, WoundBodyPart.RightArm);
        yield return (ContentKeyFunctions.TargetLeftHand, WoundBodyPart.LeftHand);
        yield return (ContentKeyFunctions.TargetRightHand, WoundBodyPart.RightHand);
        yield return (ContentKeyFunctions.TargetLeftLeg, WoundBodyPart.LeftLeg);
        yield return (ContentKeyFunctions.TargetRightLeg, WoundBodyPart.RightLeg);
        yield return (ContentKeyFunctions.TargetLeftFoot, WoundBodyPart.LeftFoot);
        yield return (ContentKeyFunctions.TargetRightFoot, WoundBodyPart.RightFoot);
    }

    private void HandleZoneKey(ICommonSession? session, WoundBodyPart zone)
    {
        if (session?.AttachedEntity is not { } uid)
            return;

        if (TryComp<TargetingComponent>(uid, out var comp) && SetTarget((uid, comp), zone))
            PopupSelected(uid, zone);
    }

    private void Cycle(ICommonSession? session, int direction)
    {
        if (session?.AttachedEntity is not { } uid || !TryComp<TargetingComponent>(uid, out var comp))
            return;

        var index = Array.IndexOf(CycleOrder, comp.Target);
        if (index < 0)
            index = 0;

        index = (index + direction + CycleOrder.Length) % CycleOrder.Length;
        var next = CycleOrder[index];

        if (SetTarget((uid, comp), next))
            PopupSelected(uid, next);
    }

    private void PopupSelected(EntityUid uid, WoundBodyPart zone)
    {
        _popup.PopupClient(
            Loc.GetString("targeting-popup-selected", ("zone", Loc.GetString(WoundZoneNames.GetLocId(zone)))),
            uid,
            uid);
    }

    private void OnZoneChangeRequest(TargetZoneChangeEvent msg, EntitySessionEventArgs args)
    {
        // The widget click only ever retargets the clicker's own body.
        if (args.SenderSession.AttachedEntity is not { } uid)
            return;

        SetTarget(uid, msg.Zone);
    }

    /// <summary>Sets the selected zone, dirtying the component. Returns whether it actually changed.</summary>
    public bool SetTarget(Entity<TargetingComponent?> ent, WoundBodyPart zone)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        if (!Enum.IsDefined(zone) || ent.Comp.Target == zone)
            return false;

        ent.Comp.Target = zone;
        Dirty(ent);
        return true;
    }

    /// <summary>Reads the selected zone, defaulting to <see cref="WoundBodyPart.Chest"/> when absent.</summary>
    public WoundBodyPart GetTarget(EntityUid uid, TargetingComponent? comp = null)
    {
        return Resolve(uid, ref comp, false) ? comp.Target : WoundBodyPart.Chest;
    }
}
