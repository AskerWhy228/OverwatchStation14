using System.Linq;
using Content.Shared.Examine;
using Content.Shared.Medical.Wounds.Components;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared.Medical.Wounds;

/// <summary>
/// FR-5: examine block listing wounds grouped by body zone. All display text is Fluent-localised.
/// </summary>
public sealed partial class WoundSystem
{
    [Dependency] private ExamineSystemShared _examine = default!;

    private void InitializeExamine()
    {
        SubscribeLocalEvent<WoundableComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
    }

    private void OnGetExamineVerbs(Entity<WoundableComponent> ent, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (ent.Comp.Wounds.Count == 0)
            return;

        var message = BuildWoundExamine(ent.Comp);
        if (message.IsEmpty)
            return;

        _examine.AddDetailedExamineVerb(
            args,
            ent.Comp,
            message,
            Loc.GetString("wound-examinable-verb-text"),
            "/Textures/Interface/VerbIcons/smite.svg.192dpi.png",
            Loc.GetString("wound-examinable-verb-message"));
    }

    private FormattedMessage BuildWoundExamine(WoundableComponent comp)
    {
        var msg = new FormattedMessage();
        var first = true;

        // Iterate zones in enum order for a stable, readable layout.
        foreach (WoundBodyPart part in Enum.GetValues(typeof(WoundBodyPart)))
        {
            var onPart = comp.Wounds.Where(w => w.Part == part).ToList();
            if (onPart.Count == 0)
                continue;

            var entries = new List<string>(onPart.Count);
            foreach (var wound in onPart)
            {
                if (!_proto.TryIndex(wound.Type, out var proto))
                    continue;

                var stageName = Loc.GetString(proto.GetStageName(wound.Severity));
                if (wound.Treatment == WoundTreatment.None)
                {
                    entries.Add(stageName);
                }
                else
                {
                    entries.Add(Loc.GetString("wound-examine-entry-treated",
                        ("wound", stageName),
                        ("treatment", Loc.GetString(GetTreatmentLoc(wound.Treatment)))));
                }
            }

            if (entries.Count == 0)
                continue;

            if (!first)
                msg.PushNewline();
            first = false;

            msg.AddMarkupOrThrow(Loc.GetString("wound-examine-part",
                ("part", Loc.GetString(GetPartLoc(part))),
                ("wounds", string.Join(", ", entries))));
        }

        return msg;
    }

    private static string GetPartLoc(WoundBodyPart part)
    {
        return part switch
        {
            WoundBodyPart.Chest => "wound-zone-chest",
            WoundBodyPart.Head => "wound-zone-head",
            WoundBodyPart.LeftArm => "wound-zone-left-arm",
            WoundBodyPart.RightArm => "wound-zone-right-arm",
            WoundBodyPart.LeftHand => "wound-zone-left-hand",
            WoundBodyPart.RightHand => "wound-zone-right-hand",
            WoundBodyPart.LeftLeg => "wound-zone-left-leg",
            WoundBodyPart.RightLeg => "wound-zone-right-leg",
            WoundBodyPart.LeftFoot => "wound-zone-left-foot",
            WoundBodyPart.RightFoot => "wound-zone-right-foot",
            _ => "wound-zone-chest",
        };
    }

    private static string GetTreatmentLoc(WoundTreatment treatment)
    {
        return treatment switch
        {
            WoundTreatment.Bandaged => "wound-treatment-bandaged",
            WoundTreatment.Salved => "wound-treatment-salved",
            WoundTreatment.Sutured => "wound-treatment-sutured",
            _ => "wound-treatment-none",
        };
    }
}
