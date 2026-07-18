using Content.Client.Gameplay;
using Content.Client.Targeting.Widgets;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared.Medical.Wounds;
using Content.Shared.Medical.Wounds.Components;
using Content.Shared.Medical.Wounds.Prototypes;
using Content.Shared.Targeting;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Prototypes;

namespace Content.Client.Targeting;

/// <summary>
/// Drives the <see cref="TargetingDoll"/> from the local player's state: paints the selected zone and the
/// per-zone damage tint, wires clicks to a predicted target change, and hides the doll for entities that
/// cannot target (ghosts, mobs without a <see cref="TargetingComponent"/>) - FR-A2.
/// </summary>
public sealed class TargetingDollUIController : UIController, IOnStateEntered<GameplayState>, IOnSystemChanged<TargetingDollSystem>
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private TargetingDoll? Doll => UIManager.GetActiveUIWidgetOrNull<TargetingDoll>();

    public override void Initialize()
    {
        base.Initialize();

        var gameplayStateLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += OnScreenLoad;
        gameplayStateLoad.OnScreenUnload += OnScreenUnload;
    }

    private void OnScreenLoad()
    {
        var doll = Doll;
        if (doll != null)
            doll.OnZoneSelected += OnZoneSelected;

        RefreshDoll();
    }

    private void OnScreenUnload()
    {
        var doll = Doll;
        if (doll != null)
            doll.OnZoneSelected -= OnZoneSelected;
    }

    public void OnSystemLoaded(TargetingDollSystem system)
    {
        system.DollUpdated += RefreshDoll;
    }

    public void OnSystemUnloaded(TargetingDollSystem system)
    {
        system.DollUpdated -= RefreshDoll;
    }

    public void OnStateEntered(GameplayState state)
    {
        RefreshDoll();
    }

    private void OnZoneSelected(WoundBodyPart zone)
    {
        // Predicted so the highlight moves instantly; the server validates and reconciles (NFR-A1).
        _entManager.RaisePredictiveEvent(new TargetZoneChangeEvent(zone));
    }

    private void RefreshDoll()
    {
        var doll = Doll;
        if (doll == null)
            return;

        if (_player.LocalEntity is not { } uid || !_entManager.TryGetComponent<TargetingComponent>(uid, out var targeting))
        {
            doll.Visible = false;
            return;
        }

        doll.Visible = true;
        doll.SetSelected(targeting.Target);

        // Sum this body's own wound severity per zone for the damage tint (FR-A7).
        var severity = new Dictionary<WoundBodyPart, float>();
        if (_entManager.TryGetComponent<WoundableComponent>(uid, out var woundable))
        {
            foreach (var wound in woundable.Wounds)
            {
                severity.TryGetValue(wound.Part, out var acc);
                severity[wound.Part] = acc + wound.Severity.Float();
            }
        }

        var displayCap = 50f;
        BodyZoneProfilePrototype? profile = null;
        if (_entManager.TryGetComponent<BodyZoneProfileComponent>(uid, out var profComp)
            && _proto.TryIndex(profComp.Profile, out profile))
        {
            displayCap = profile.DisplayCap;
        }

        foreach (var part in Enum.GetValues<WoundBodyPart>())
        {
            severity.TryGetValue(part, out var sev);
            doll.SetZoneDamage(part, sev, displayCap);
            doll.SetZoneTooltip(part, BuildTooltip(part, profile));
        }
    }

    private string BuildTooltip(WoundBodyPart part, BodyZoneProfilePrototype? profile)
    {
        var name = Loc.GetString(WoundZoneNames.GetLocId(part));
        if (profile == null)
            return name;

        var chance = (int) MathF.Round(profile.GetZone(part).HitChanceMelee * 100f);
        return Loc.GetString("targeting-doll-tooltip", ("zone", name), ("chance", chance));
    }
}
