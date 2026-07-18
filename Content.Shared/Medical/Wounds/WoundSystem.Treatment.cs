using System.Linq;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Medical.Wounds.Components;
using Content.Shared.Popups;
using Content.Shared.Targeting;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical.Wounds;

/// <summary>
/// FR-7: bandage/ointment items that treat wounds via a DoAfter.
/// </summary>
public sealed partial class WoundSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedChargesSystem _charges = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    private void InitializeTreatment()
    {
        SubscribeLocalEvent<WoundTreatmentItemComponent, AfterInteractEvent>(OnTreatmentAfterInteract);
        SubscribeLocalEvent<WoundTreatmentItemComponent, UseInHandEvent>(OnTreatmentUseInHand);
        SubscribeLocalEvent<WoundableComponent, WoundTreatmentDoAfterEvent>(OnTreatmentDoAfter);
    }

    private void OnTreatmentAfterInteract(Entity<WoundTreatmentItemComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (TryStartTreatment(ent, args.User, target))
            args.Handled = true;
    }

    private void OnTreatmentUseInHand(Entity<WoundTreatmentItemComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (TryStartTreatment(ent, args.User, args.User))
            args.Handled = true;
    }

    private bool TryStartTreatment(Entity<WoundTreatmentItemComponent> ent, EntityUid user, EntityUid target)
    {
        if (!TryComp<WoundableComponent>(target, out var woundable))
            return false;

        // FR-A6: treatment is applied to the zone the user has selected, not the worst wound anywhere.
        var zone = GetTreatmentZone(user);

        if (!HasApplicableWoundOnZone(ent.Comp, woundable, zone))
        {
            // Nothing treatable here; point the user at the zones that do carry an applicable wound. No charge spent.
            _popup.PopupClient(BuildNoWoundHereMessage(ent.Comp, woundable), target, user);
            return false;
        }

        if (TryComp<LimitedChargesComponent>(ent, out var charges) && _charges.IsEmpty((ent, charges)))
        {
            _popup.PopupClient(Loc.GetString("wound-treatment-empty"), ent, user);
            return false;
        }

        var isSelf = user == target;
        if (!isSelf && !_interaction.InRangeUnobstructed(user, target, popup: true))
            return false;

        var delay = ent.Comp.Delay;
        if (isSelf)
            delay *= ent.Comp.SelfDelayMultiplier;

        var doAfter = new DoAfterArgs(EntityManager, user, delay, new WoundTreatmentDoAfterEvent(), target, target: target, used: ent)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        };

        return _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnTreatmentDoAfter(Entity<WoundableComponent> ent, ref WoundTreatmentDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (args.Used is not { } used || !TryComp<WoundTreatmentItemComponent>(used, out var item))
            return;

        // Mutation is server-authoritative; clients receive the updated wound list over the network.
        if (_net.IsClient)
        {
            args.Handled = true;
            return;
        }

        // Pick the most severe untreated applicable wound on the user's selected zone (FR-A6).
        var zone = GetTreatmentZone(args.User);
        var index = FindTreatableWound(ent.Comp, item, zone, out var anyApplicableTreated);
        if (index < 0)
        {
            var msg = anyApplicableTreated
                ? Loc.GetString("wound-treatment-already-treated")
                : BuildNoWoundHereMessage(item, ent.Comp);
            _popup.PopupEntity(msg, ent, args.User);
            return;
        }

        if (TryComp<LimitedChargesComponent>(used, out var charges) && !_charges.TryUseCharge((used, charges)))
        {
            _popup.PopupEntity(Loc.GetString("wound-treatment-empty"), used, args.User);
            return;
        }

        TreatWound(ent, index, item.Treatment, item.InstantHeal);

        var popup = ent.Owner == args.User
            ? Loc.GetString("wound-treatment-success-self")
            : Loc.GetString("wound-treatment-success", ("target", Identity.Entity(ent.Owner, EntityManager)));
        _popup.PopupEntity(popup, ent, args.User);

        args.Handled = true;
    }

    /// <summary>The zone a treater is aiming at, defaulting to the chest when they cannot target.</summary>
    private WoundBodyPart GetTreatmentZone(EntityUid user)
    {
        return TryComp<TargetingComponent>(user, out var targeting) ? targeting.Target : WoundBodyPart.Chest;
    }

    private bool HasApplicableWoundOnZone(WoundTreatmentItemComponent item, WoundableComponent comp, WoundBodyPart zone)
    {
        foreach (var wound in comp.Wounds)
        {
            if (wound.Part == zone && item.Treats.Contains(wound.Type))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the index of the most severe untreated wound this item can treat <b>on the given zone</b>, or
    /// -1 if none. <paramref name="anyApplicableTreated"/> reports whether an applicable wound exists on the
    /// zone but is already treated.
    /// </summary>
    private int FindTreatableWound(WoundableComponent comp, WoundTreatmentItemComponent item, WoundBodyPart zone, out bool anyApplicableTreated)
    {
        anyApplicableTreated = false;
        var best = -1;
        var bestSeverity = FixedPoint2.Zero;

        for (var i = 0; i < comp.Wounds.Count; i++)
        {
            var wound = comp.Wounds[i];
            if (wound.Part != zone || !item.Treats.Contains(wound.Type))
                continue;

            if (wound.Treatment != WoundTreatment.None)
            {
                anyApplicableTreated = true;
                continue;
            }

            if (best < 0 || wound.Severity > bestSeverity)
            {
                best = i;
                bestSeverity = wound.Severity;
            }
        }

        return best;
    }

    /// <summary>
    /// Builds the "nothing to treat here" popup, appending a hint listing the zones that do carry an
    /// untreated wound this item can treat (FR-A6).
    /// </summary>
    private string BuildNoWoundHereMessage(WoundTreatmentItemComponent item, WoundableComponent comp)
    {
        var zones = comp.Wounds
            .Where(w => w.Treatment == WoundTreatment.None && item.Treats.Contains(w.Type))
            .Select(w => w.Part)
            .Distinct()
            .Select(part => Loc.GetString(WoundZoneNames.GetLocId(part)))
            .ToList();

        if (zones.Count == 0)
            return Loc.GetString("wound-treatment-none-applicable");

        return Loc.GetString("wound-treatment-none-here", ("zones", string.Join(", ", zones)));
    }

    /// <summary>
    /// Applies a treatment status and an instant severity reduction to a single wound, mirroring the
    /// removed damage back onto the vanilla pool (FR-2).
    /// </summary>
    public void TreatWound(Entity<WoundableComponent> ent, int index, WoundTreatment treatment, FixedPoint2 instantHeal)
    {
        var wounds = ent.Comp.Wounds;
        if (index < 0 || index >= wounds.Count)
            return;

        var wound = wounds[index];
        wound.Treatment = treatment;
        wounds[index] = wound;

        if (instantHeal > 0)
        {
            var mirrored = new DamageSpecifier();
            ReduceSingleWound(ent, ref wounds, index, instantHeal, mirror: true, mirrored);
            if (!mirrored.Empty)
                MirrorToPool(ent, mirrored);
        }

        Dirty(ent);
        UpdateActive(ent);
    }
}
