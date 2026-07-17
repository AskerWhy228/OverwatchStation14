using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
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

    /// <summary>Scratch buffer so the self-heal tick never mutates the query it is iterating over.</summary>
    private readonly List<EntityUid> _healQueue = new();

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

        // A null or empty delta means the pool was set directly (SetDamage / SetAllDamage / ClearAllDamage,
        // e.g. Rejuvenate) rather than dealt/healed. Those paths bypass the per-type delta entirely, so the
        // wound layer would otherwise never learn the pool was zeroed and would keep phantom wounds forever.
        // Reconcile the wounds down to whatever the pool now holds.
        if (args.DamageDelta is not { } delta || delta.DamageDict.Count == 0)
        {
            ReconcileWithPool(ent);
            return;
        }

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
        AddOrMergeWound(ent, entry.Wound, type, chosen, severity, amount);
    }

    /// <summary>
    /// Distributes an incoming heal (from vanilla chemistry, etc.) proportionally across wounds whose
    /// type maps from the healed damage type. Does not touch the vanilla pool; vanilla already applied it.
    /// </summary>
    private void HealDamageType(Entity<WoundableComponent> ent, ProtoId<DamageTypePrototype> type, FixedPoint2 amount)
    {
        if (!TryGetEntry(ent.Comp, type, out var entry))
            return;

        // Mirror the exact conversion used on the way in (including SeverityMultiplier) so a full vanilla heal
        // removes exactly the severity a full vanilla hit added; an asymmetric factor drifts wounds vs. pool.
        var toRemove = amount * entry.SeverityPerDamage * ent.Comp.SeverityMultiplier;
        // Reduce wounds that actually came from this vanilla type, not every wound sharing a prototype: Shock,
        // Cold and Heat all map to Burn, so keying on the prototype would let healing one type eat another's wounds.
        ReduceWoundsOfSourceType(ent, type, toRemove, mirror: false);
    }

    /// <summary>
    /// FR-4 merge logic. Merges into an existing wound of the same type on the same zone, otherwise
    /// creates a new one, honouring the per-zone <see cref="WoundableComponent.MaxWounds"/> limit.
    /// </summary>
    private void AddOrMergeWound(
        Entity<WoundableComponent> ent,
        ProtoId<WoundPrototype> woundType,
        ProtoId<DamageTypePrototype> sourceType,
        WoundBodyPart part,
        FixedPoint2 severity,
        FixedPoint2 rawDamage)
    {
        if (severity <= 0)
            return;

        var wounds = ent.Comp.Wounds;

        // Same wound type from the same source on the same zone -> merge. Source type is part of the key so a
        // Shock burn and a Heat burn stay separate wounds and each mirrors back onto its own vanilla pool.
        var index = FindWound(wounds, woundType, sourceType, part);
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

        // Zone saturated with distinct wounds. We deliberately do NOT merge this new source into an existing
        // wound of a different source: the merged wound would then mirror its healing onto the wrong vanilla
        // pool. Instead the surplus damage simply stays purely vanilla and is not recorded as a wound. The pool
        // is authoritative, so vanilla healing and reconciliation still handle that damage safely without a
        // backing wound - it just isn't localised. This keeps the wound list bounded by MaxWounds per zone.
        var onPart = CountWoundsOnPart(wounds, part);
        if (onPart >= ent.Comp.MaxWounds)
            return;

        var wound = new Wound
        {
            Type = woundType,
            SourceType = sourceType,
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
    }

    /// <summary>
    /// Reduces severity across all wounds of a given type, proportionally to their current severity.
    /// When <paramref name="mirror"/> is true also reduces the vanilla damage pool by the equivalent
    /// raw damage (used when the reduction originates from wound treatment/self-heal, not vanilla healing).
    /// </summary>
    private void ReduceWoundsOfSourceType(
        Entity<WoundableComponent> ent,
        ProtoId<DamageTypePrototype> sourceType,
        FixedPoint2 severityToRemove,
        bool mirror)
    {
        if (severityToRemove <= 0)
            return;

        var wounds = ent.Comp.Wounds;

        var totalSeverity = FixedPoint2.Zero;
        foreach (var w in wounds)
        {
            if (w.SourceType == sourceType)
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
            if (wound.SourceType != sourceType)
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

        if (mirror)
        {
            // Mirror onto the exact vanilla type that produced this wound, so a Shock burn heals Shock and not Heat.
            var key = wound.SourceType;
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
        // Snapshot the active set first: healing removes ActiveWoundableComponent (via UpdateActive) and mirrors
        // damage, which raises DamageChangedEvent and can trigger further structural changes. Mutating the
        // component being enumerated mid-iteration is undefined behaviour, so we iterate a plain list instead.
        _healQueue.Clear();
        var query = EntityQueryEnumerator<ActiveWoundableComponent, WoundableComponent>();
        while (query.MoveNext(out var uid, out _, out _))
            _healQueue.Add(uid);

        foreach (var uid in _healQueue)
        {
            if (!TryComp<WoundableComponent>(uid, out var woundable))
                continue;

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

    private static int FindWound(List<Wound> wounds, ProtoId<WoundPrototype> type, ProtoId<DamageTypePrototype> sourceType, WoundBodyPart part)
    {
        for (var i = 0; i < wounds.Count; i++)
        {
            if (wounds[i].Type == type && wounds[i].SourceType == sourceType && wounds[i].Part == part)
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

    /// <summary>
    /// Trims wounds back down to whatever the vanilla pool now holds per damage type. Used when the pool is set
    /// directly (SetDamage / SetAllDamage / ClearAllDamage, e.g. Rejuvenate) instead of dealt or healed: those
    /// paths carry no per-type delta, so without this the wound layer would keep phantom wounds after a full heal.
    /// Never touches the pool (it is already authoritative) and only ever removes severity, never adds.
    /// </summary>
    private void ReconcileWithPool(Entity<WoundableComponent> ent)
    {
        var wounds = ent.Comp.Wounds;
        if (wounds.Count == 0)
            return;

        if (!TryComp<DamageableComponent>(ent, out var damageable))
            return;

        // Sum the raw damage the wounds currently account for, per vanilla source type.
        var tracked = new Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2>();
        foreach (var w in wounds)
        {
            tracked.TryGetValue(w.SourceType, out var acc);
            tracked[w.SourceType] = acc + w.Damage;
        }

        var dirty = false;
        foreach (var (type, trackedDamage) in tracked)
        {
            if (trackedDamage <= 0)
                continue;

            var pool = damageable.Damage.DamageDict.GetValueOrDefault(type);
            if (pool >= trackedDamage)
                continue; // Pool still covers these wounds; leave them alone.

            // Pool dropped below what the wounds claim: scale every wound of this source type down to fit.
            var scale = pool <= 0 ? FixedPoint2.Zero : pool / trackedDamage;
            for (var i = wounds.Count - 1; i >= 0; i--)
            {
                var wound = wounds[i];
                if (wound.SourceType != type)
                    continue;

                if (scale <= 0)
                {
                    wounds.RemoveAt(i);
                    var removed = new WoundRemovedEvent(ent, wound);
                    RaiseLocalEvent(ent, ref removed);
                    dirty = true;
                    continue;
                }

                wound.Severity *= scale;
                wound.Damage *= scale;
                if (wound.Severity <= 0)
                {
                    wounds.RemoveAt(i);
                    var removed = new WoundRemovedEvent(ent, wound);
                    RaiseLocalEvent(ent, ref removed);
                }
                else
                {
                    wound.BleedRate = ComputeBleedRate(wound);
                    wounds[i] = wound;
                    var changed = new WoundChangedEvent(ent, wound);
                    RaiseLocalEvent(ent, ref changed);
                }

                dirty = true;
            }
        }

        if (dirty)
        {
            Dirty(ent);
            UpdateActive(ent);
        }
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
