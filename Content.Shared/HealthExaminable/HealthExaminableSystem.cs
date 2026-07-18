using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Medical.Wounds.Components;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared.HealthExaminable;

public sealed partial class HealthExaminableSystem : EntitySystem
{
    [Dependency] private ExamineSystemShared _examineSystem = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HealthExaminableComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
    }

    private void OnGetExamineVerbs(EntityUid uid, HealthExaminableComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage))
            return;

        var detailsRange = _examineSystem.IsInDetailsRange(args.User, uid);

        var verb = new ExamineVerb()
        {
            Act = () =>
            {
                var markup = CreateMarkup(uid, component, damage);
                _examineSystem.SendExamineTooltip(args.User, uid, markup, false, false);
            },
            Text = Loc.GetString("health-examinable-verb-text"),
            Category = VerbCategory.Examine,
            Disabled = !detailsRange,
            Message = detailsRange ? null : Loc.GetString("health-examinable-verb-disabled"),
            Icon = new SpriteSpecifier.Texture(new ("/Textures/Interface/VerbIcons/rejuvenate.svg.192dpi.png"))
        };

        args.Verbs.Add(verb);
    }

    public FormattedMessage CreateMarkup(EntityUid uid, HealthExaminableComponent component, DamageableComponent damage)
    {
        var msg = new FormattedMessage();

        // FR-A8: entities with localised wounds suppress the vanilla generalised severity line ("looks
        // battered") entirely - the per-zone wound list appended below (via HealthBeingExaminedEvent)
        // replaces it, so the same damage is never described twice. Mobs without a wound layer keep the
        // vanilla lines unchanged.
        var woundable = HasComp<WoundableComponent>(uid);

        if (!woundable)
        {
            var first = true;
            var damageSpecifier = _damageable.GetAllDamage((uid, damage));
            foreach (var type in component.ExaminableTypes)
            {
                if (!damageSpecifier.DamageDict.TryGetValue(type, out var dmg))
                    continue;

                if (dmg == FixedPoint2.Zero)
                    continue;

                FixedPoint2 closest = FixedPoint2.Zero;

                string chosenLocStr = string.Empty;
                foreach (var threshold in component.Thresholds)
                {
                    var str = $"health-examinable-{component.LocPrefix}-{type}-{threshold}";
                    var tempLocStr = Loc.GetString($"health-examinable-{component.LocPrefix}-{type}-{threshold}", ("target", Identity.Entity(uid, EntityManager)));

                    // i.e., this string doesn't exist, because theres nothing for that threshold
                    if (tempLocStr == str)
                        continue;

                    if (dmg > threshold && threshold > closest)
                    {
                        chosenLocStr = tempLocStr;
                        closest = threshold;
                    }
                }

                if (closest == FixedPoint2.Zero)
                    continue;

                if (!first)
                {
                    msg.PushNewline();
                }
                else
                {
                    first = false;
                }
                msg.AddMarkupOrThrow(chosenLocStr);
            }

            if (msg.IsEmpty)
            {
                msg.AddMarkupOrThrow(Loc.GetString($"health-examinable-{component.LocPrefix}-none"));
            }
        }

        // Anything else want to add on to this? Wounds (per zone) and bleeding hook in here.
        RaiseLocalEvent(uid, new HealthBeingExaminedEvent(msg), true);

        // A woundable mob with genuinely nothing to report gets a wound-flavoured "healthy" line instead of
        // the (now-skipped) vanilla one.
        if (woundable && msg.IsEmpty)
        {
            msg.AddMarkupOrThrow(Loc.GetString("wound-examine-none"));
        }

        return msg;
    }
}

/// <summary>
///     A class raised on an entity whose health is being examined
///     in order to add special text that is not handled by the
///     damage thresholds.
/// </summary>
public sealed class HealthBeingExaminedEvent
{
    public FormattedMessage Message;

    public HealthBeingExaminedEvent(FormattedMessage message)
    {
        Message = message;
    }
}
