using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds.Components;
using Content.Shared.Medical.Wounds.Prototypes;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.Medical.Wounds;

/// <summary>
/// Phase-0 wound system. Intercepts damage on entities with a <see cref="WoundableComponent"/> and
/// records it as localised wounds in parallel with the vanilla damage pool, without changing balance.
/// Conversion is server-authoritative in phase 0; the wound list is networked to clients for examine.
/// </summary>
public sealed partial class WoundSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;

    /// <summary>Guard against re-processing the damage change we ourselves cause when mirroring.</summary>
    private bool _mirroring;

    private const float HealTickPeriod = 15f;
    private float _healAccumulator;

    /// <summary>Re-treating an existing wound with more than this much added severity rips the dressing off.</summary>
    private static readonly FixedPoint2 TreatmentResetThreshold = FixedPoint2.New(5);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WoundableComponent, DamageChangedEvent>(OnDamageChanged);

        InitializeExamine();
        InitializeTreatment();
    }

    private void OnDamageChanged(Entity<WoundableComponent> ent, ref DamageChangedEvent args)
    {
        // Conversion is server-only in phase 0; clients receive wounds via AutoNetworkedField.
        if (_net.IsClient)
            return;

        // Ignore the reflected change from our own mirroring pass.
        if (_mirroring)
            return;

        if (args.DamageDelta is not { } delta)
            return;

        foreach (var (type, amount) in delta.DamageDict)
        {
            if (amount > 0)
                ApplyDamageType(ent, type, amount, null);
            else if (amount < 0)
                HealDamageType(ent, type, -amount);
        }
    }

    /// <summary>
    /// Converts a single positive damage delta of one type into a wound on the chosen (or random) zone.
    /// </summary>
    public void ApplyDamageType(
        Entity<WoundableComponent> ent,
        ProtoId<DamageTypePrototype> type,
        FixedPoint2 amount,
        WoundBodyPart? zone)
    {
        if (amount <= 0)
            return;

        if (!TryGetEntry(ent.Comp, type, out var entry))
            return; // No mapping: damage stays purely vanilla.

        var part = zone ?? PickZone(ent.Comp);
        if (part is not { } chosen)
            return;

        var severity = amount * entry.SeverityPerDamage * ent.Comp.SeverityMultiplier;
        AddOrMergeWound(ent, entry.Wound, chosen, severity, amount);
    }

    /// <summary>
    /// Distributes an incoming heal (from vanilla chemistry, etc.) proportionally across wounds whose
    /// type maps from the healed damage type. Does not touch the vanilla pool; vanilla already applied it.
    /// </summary>
    private void HealDamageType(Entity<WoundableComponent> ent, ProtoId<DamageTypePrototype> type, FixedPoint2 amount)
    {
        if (!TryGetEntry(ent.Comp, type, out var entry))
            return;

        var toRemove = amount * entry.SeverityPerDamage;
        ReduceWoundsOfType(ent, entry.Wound, toRemove, mirror: false);
    }

    /// <summary>
    /// FR-4 merge logic. Merges into an existing wound of the same type on the same zone, otherwise
    /// creates a new one, honouring the per-zone <see cref="WoundableComponent.MaxWounds"/> limit.
    /// </summary>
    private void AddOrMergeWound(
        Entity<WoundableComponent> ent,
        ProtoId<WoundPrototype> woundType,
        WoundBodyPart part,
        FixedPoint2 severity,
        FixedPoint2 rawDamage)
    {
        if (severity <= 0)
            return;

        var wounds = ent.Comp.Wounds;

        // Same type on same zone -> merge.
        var index = FindWound(wounds, woundType, part);
        if (index >= 0)
        {
            var merged = wounds[index];
            merged.Severity += severity;
            merged.Damage += rawDamage;
            if (severity > TreatmentResetThreshold)
                merged.Treatment = WoundTreatment.None;
            merged.BleedRate = ComputeBleedRate(merged);
            wounds[index] = merged;

            Dirty(ent);
            var changed = new WoundChangedEvent(ent, merged);
            RaiseLocalEvent(ent, ref changed);
            return;
        }

        var onPart = CountWoundsOnPart(wounds, part);
        if (onPart < ent.Comp.MaxWounds)
        {
            var wound = new Wound
            {
                Type = woundType,
                Part = part,
                Severity = severity,
                Damage = rawDamage,
                Treatment = WoundTreatment.None,
            };
            wound.BleedRate = ComputeBleedRate(wound);
            wounds.Add(wound);

            EnsureActive(ent);
            Dirty(ent);
            var added = new WoundAddedEvent(ent, wound);
            RaiseLocalEvent(ent, ref added);
            return;
        }

        // Zone is full: pour severity into the heaviest wound of the same type, else the heaviest overall.
        var target = FindHeaviestOnPart(wounds, part, woundType);
        if (target < 0)
            target = FindHeaviestOnPart(wounds, part, null);
        if (target < 0)
            return;

        var heaviest = wounds[target];
        heaviest.Severity += severity;
        heaviest.Damage += rawDamage;
        if (severity > TreatmentResetThreshold)
            heaviest.Treatment = WoundTreatment.None;
        heaviest.BleedRate = ComputeBleedRate(heaviest);
        wounds[target] = heaviest;

        Dirty(ent);
        var ev = new WoundChangedEvent(ent, heaviest);
        RaiseLocalEvent(ent, ref ev);
    }

    /// <summary>
    /// Reduces severity across all wounds of a given type, proportionally to their current severity.
    /// When <paramref name="mirror"/> is true also reduces the vanilla damage pool by the equivalent
    /// raw damage (used when the reduction originates from wound treatment/self-heal, not vanilla healing).
    /// </summary>
    private void ReduceWoundsOfType(
        Entity<WoundableComponent> ent,
        ProtoId<WoundPrototype> woundType,
        FixedPoint2 severityToRemove,
        bool mirror)
    {
        if (severityToRemove <= 0)
            return;

        var wounds = ent.Comp.Wounds;

        var totalSeverity = FixedPoint2.Zero;
        foreach (var w in wounds)
        {
            if (w.Type == woundType)
                totalSeverity += w.Severity;
        }

        if (totalSeverity <= 0)
            return;

        if (severityToRemove > totalSeverity)
            severityToRemove = totalSeverity;

        var mirroredDamage = new DamageSpecifier();

        for (var i = wounds.Count - 1; i >= 0; i--)
        {
            var wound = wounds[i];
            if (wound.Type != woundType)
                continue;

            var share = severityToRemove * (wound.Severity / totalSeverity);
            ReduceSingleWound(ent, ref wounds, i, share, mirror, mirroredDamage);
        }

        if (mirror && !mirroredDamage.Empty)
            MirrorToPool(ent, mirroredDamage);

        Dirty(ent);
        UpdateActive(ent);
    }

    /// <summary>
    /// Removes <paramref name="severity"/> from the wound at <paramref name="index"/>, removing the wound
    /// entirely if it reaches zero. Accumulates the mirrored raw-damage reduction into <paramref name="mirroredDamage"/>.
    /// </summary>
    private void ReduceSingleWound(
        Entity<WoundableComponent> ent,
        ref List<Wound> wounds,
        int index,
        FixedPoint2 severity,
        bool mirror,
        DamageSpecifier mirroredDamage)
    {
        if (severity <= 0)
            return;

        var wound = wounds[index];
        if (severity > wound.Severity)
            severity = wound.Severity;

        // Proportional slice of the raw damage this wound owns, for exact pool mirroring.
        var damageShare = wound.Severity > 0
            ? wound.Damage * (severity / wound.Severity)
            : wound.Damage;

        if (mirror && _proto.TryIndex(wound.Type, out var proto))
        {
            var key = proto.DamageType;
            mirroredDamage.DamageDict.TryGetValue(key, out var existing);
            mirroredDamage.DamageDict[key] = existing - damageShare;
        }

        wound.Severity -= severity;
        wound.Damage -= damageShare;
        if (wound.Damage < 0)
            wound.Damage = FixedPoint2.Zero;

        if (wound.Severity <= 0)
        {
            wounds.RemoveAt(index);
            var removed = new WoundRemovedEvent(ent, wound);
            RaiseLocalEvent(ent, ref removed);
        }
        else
        {
            wound.BleedRate = ComputeBleedRate(wound);
            wounds[index] = wound;
            var changed = new WoundChangedEvent(ent, wound);
            RaiseLocalEvent(ent, ref changed);
        }
    }

    /// <summary>
    /// Applies the given (negative) damage specifier to the vanilla pool, guarding against re-entrant
    /// wound processing so mirroring never double-counts.
    /// </summary>
    private void MirrorToPool(Entity<WoundableComponent> ent, DamageSpecifier damage)
    {
        _mirroring = true;
        try
        {
            _damageable.TryChangeDamage(ent.Owner, damage, ignoreResistances: true, interruptsDoAfters: false);
        }
        finally
        {
            _mirroring = false;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Self-heal is server-authoritative.
        if (!_net.IsServer)
            return;

        _healAccumulator += frameTime;
        if (_healAccumulator < HealTickPeriod)
            return;

        _healAccumulator -= HealTickPeriod;
        TickSelfHeal();
    }

    /// <summary>
    /// FR-6 self-heal tick. Only iterates entities carrying <see cref="ActiveWoundableComponent"/>.
    /// </summary>
    private void TickSelfHeal()
    {
        var query = EntityQueryEnumerator<ActiveWoundableComponent, WoundableComponent>();
        while (query.MoveNext(out var uid, out _, out var woundable))
        {
            var ent = new Entity<WoundableComponent>(uid, woundable);
            var wounds = woundable.Wounds;
            var mirrored = new DamageSpecifier();
            var dirty = false;

            for (var i = wounds.Count - 1; i >= 0; i--)
            {
                var wound = wounds[i];
                if (wound.Treatment == WoundTreatment.None)
                    continue;

                if (!_proto.TryIndex(wound.Type, out var proto))
                    continue;

                if (wound.Severity >= proto.SelfHealBelow)
                    continue;

                ReduceSingleWound(ent, ref wounds, i, proto.SelfHealRate, mirror: true, mirrored);
                dirty = true;
            }

            if (!mirrored.Empty)
                MirrorToPool(ent, mirrored);

            if (dirty)
            {
                Dirty(ent);
                UpdateActive(ent);
            }
        }
    }

    // ---- helpers ----

    private bool TryGetEntry(WoundableComponent comp, ProtoId<DamageTypePrototype> type, out DamageWoundEntry entry)
    {
        entry = default;
        return _proto.TryIndex(comp.DamageTable, out var table) && table.Entries.TryGetValue(type, out entry);
    }

    private WoundBodyPart? PickZone(WoundableComponent comp)
    {
        if (!_proto.TryIndex(comp.HitZoneWeights, out var weights) || weights.Weights.Count == 0)
            return null;

        var total = weights.Weights.Values.Sum();
        if (total <= 0)
            return null;

        var roll = _random.NextFloat() * total;
        foreach (var (part, weight) in weights.Weights)
        {
            roll -= weight;
            if (roll <= 0)
                return part;
        }

        return weights.Weights.Keys.Last();
    }

    private static FixedPoint2 ComputeBleedRate(Wound wound)
    {
        // Populated for phase 1; read by nothing in phase 0.
        return wound.Severity;
    }

    private static int FindWound(List<Wound> wounds, ProtoId<WoundPrototype> type, WoundBodyPart part)
    {
        for (var i = 0; i < wounds.Count; i++)
        {
            if (wounds[i].Type == type && wounds[i].Part == part)
                return i;
        }

        return -1;
    }

    private static int CountWoundsOnPart(List<Wound> wounds, WoundBodyPart part)
    {
        var count = 0;
        foreach (var w in wounds)
        {
            if (w.Part == part)
                count++;
        }

        return count;
    }

    private static int FindHeaviestOnPart(List<Wound> wounds, WoundBodyPart part, ProtoId<WoundPrototype>? type)
    {
        var best = -1;
        var bestSeverity = FixedPoint2.Zero;
        for (var i = 0; i < wounds.Count; i++)
        {
            var w = wounds[i];
            if (w.Part != part)
                continue;
            if (type != null && w.Type != type.Value)
                continue;
            if (best < 0 || w.Severity > bestSeverity)
            {
                best = i;
                bestSeverity = w.Severity;
            }
        }

        return best;
    }

    private void EnsureActive(Entity<WoundableComponent> ent)
    {
        if (ent.Comp.Wounds.Count > 0)
            EnsureComp<ActiveWoundableComponent>(ent);
    }

    private void UpdateActive(Entity<WoundableComponent> ent)
    {
        if (ent.Comp.Wounds.Count == 0)
            RemComp<ActiveWoundableComponent>(ent);
        else
            EnsureComp<ActiveWoundableComponent>(ent);
    }
}
