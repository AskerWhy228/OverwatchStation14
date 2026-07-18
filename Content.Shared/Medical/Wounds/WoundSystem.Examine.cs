using System.Linq;
using Content.Shared.HealthExaminable;
using Content.Shared.Medical.Wounds.Components;
using Robust.Shared.Utility;

namespace Content.Shared.Medical.Wounds;

/// <summary>
/// FR-5 / FR-A8: the per-zone wound list is folded into the common health-examine block (alongside the
/// vanilla alive/crit/dead line) rather than living in its own examine section. The vanilla generalised
/// severity line ("looks battered") is suppressed for woundable entities in <see cref="HealthExaminableSystem"/>,
/// so the wounds fully replace it and the same damage is never described twice.
/// </summary>
public sealed partial class WoundSystem
{
    private void InitializeExamine()
    {
        SubscribeLocalEvent<WoundableComponent, HealthBeingExaminedEvent>(OnHealthExamined);
    }

    private void OnHealthExamined(Entity<WoundableComponent> ent, ref HealthBeingExaminedEvent args)
    {
        AppendWoundExamine(ent.Comp, args.Message);
    }

    private void AppendWoundExamine(WoundableComponent comp, FormattedMessage msg)
    {
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

            if (!msg.IsEmpty)
                msg.PushNewline();

            msg.AddMarkupOrThrow(Loc.GetString("wound-examine-part",
                ("part", Loc.GetString(WoundZoneNames.GetLocId(part))),
                ("wounds", string.Join(", ", entries))));
        }
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
