using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Wounds;
using Content.Shared.Medical.Wounds.Components;
using Content.Shared.Medical.Wounds.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
[TestOf(typeof(WoundSystem))]
[TestOf(typeof(WoundableComponent))]
public sealed class WoundSystemTest : GameTest
{
    private const string WoundTestMob = "WoundTestMob";
    private const string WoundTestMobNoWounds = "WoundTestMobNoWounds";
    private const string TargetTestMob = "WoundTargetTestMob";
    private const string TargetTestProfile = "WoundTargetTestProfile";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {WoundTestMob}
  name: {WoundTestMob}
  components:
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: Woundable
    maxWounds: 2

- type: entity
  id: {WoundTestMobNoWounds}
  name: {WoundTestMobNoWounds}
  components:
  - type: Damageable
  - type: Injurable
    damageContainer: Biological

# Deterministic profile: Head always misses (chance 0 -> deviates to Chest), Chest/LeftArm always hit
# (chance 1). Non-empty miss fallbacks force the accuracy roll to actually run rather than short-circuit.
- type: bodyZoneProfile
  id: {TargetTestProfile}
  fallbackWeights:
    Chest: 1
  zones:
    Head:    {{ hitChanceMelee: 0.0, hitChanceRanged: 0.0, missFallback: [ Chest ], severityMultiplier: 1.5 }}
    Chest:   {{ hitChanceMelee: 1.0, hitChanceRanged: 1.0, missFallback: [ Head ], severityMultiplier: 1.0 }}
    LeftArm: {{ hitChanceMelee: 1.0, hitChanceRanged: 1.0, missFallback: [ Chest ], severityMultiplier: 1.0 }}

- type: entity
  id: {TargetTestMob}
  name: {TargetTestMob}
  components:
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: Woundable
    maxWounds: 6
  - type: BodyZoneProfile
    profile: {TargetTestProfile}
";

    [Test]
    public async Task DamageCreatesWoundAndPoolIsUnchanged()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var damageable = sysMan.GetEntitySystem<DamageableSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(WoundTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);

            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");
            var damage = new DamageSpecifier(blunt, FixedPoint2.New(20));

            damageable.ChangeDamage(uid, damage, ignoreResistances: true);

            // FR-2: the vanilla pool must be unchanged by the wound layer.
            Assert.That(damageable.GetTotalDamage(uid), Is.EqualTo(FixedPoint2.New(20)));

            // A single Bruise wound must exist mirroring the Blunt damage.
            Assert.That(woundable.Wounds, Has.Count.EqualTo(1));
            Assert.That(woundable.Wounds[0].Type.Id, Is.EqualTo("Bruise"));
            Assert.That(woundable.Wounds[0].Severity, Is.EqualTo(FixedPoint2.New(20)));

            // ActiveWoundable marker must be present while wounds exist.
            Assert.That(entMan.HasComponent<ActiveWoundableComponent>(uid), Is.True);
        });
    }

    [Test]
    public async Task RepeatedSameZoneDamageMergesIntoOneWound()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var wounds = sysMan.GetEntitySystem<WoundSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(WoundTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);
            var ent = new Entity<WoundableComponent>(uid, woundable);

            for (var i = 0; i < 5; i++)
                wounds.ApplyDamageType(ent, "Blunt", FixedPoint2.New(4), WoundBodyPart.Chest);

            // FR-4: five hits to one zone become a single growing wound, not five entries.
            Assert.That(woundable.Wounds, Has.Count.EqualTo(1));
            Assert.That(woundable.Wounds[0].Severity, Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(woundable.Wounds[0].Part, Is.EqualTo(WoundBodyPart.Chest));
        });
    }

    [Test]
    public async Task ZoneWoundLimitCoalescesSameSourceButNeverAcrossSources()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var wounds = sysMan.GetEntitySystem<WoundSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(WoundTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);
            var ent = new Entity<WoundableComponent>(uid, woundable);

            // Repeated damage of the same vanilla type always coalesces into one wound.
            wounds.ApplyDamageType(ent, "Blunt", FixedPoint2.New(10), WoundBodyPart.Chest);
            wounds.ApplyDamageType(ent, "Blunt", FixedPoint2.New(10), WoundBodyPart.Chest);
            Assert.That(woundable.Wounds.Count(w => w.Part == WoundBodyPart.Chest), Is.EqualTo(1));

            // Second distinct source fills the zone (maxWounds is 2 for this mob).
            wounds.ApplyDamageType(ent, "Slash", FixedPoint2.New(10), WoundBodyPart.Chest);
            Assert.That(woundable.Wounds.Count(w => w.Part == WoundBodyPart.Chest), Is.EqualTo(2));

            // Zone is now full. A third distinct source must NOT be merged into an existing wound of a different
            // source (that would mirror its healing onto the wrong vanilla pool). The cap is honoured instead by
            // leaving the surplus as pure vanilla damage, so no mis-attributed Puncture wound appears.
            wounds.ApplyDamageType(ent, "Piercing", FixedPoint2.New(10), WoundBodyPart.Chest);

            var onChest = woundable.Wounds.Where(w => w.Part == WoundBodyPart.Chest).ToList();
            Assert.That(onChest, Has.Count.EqualTo(2));
            Assert.That(onChest.Select(w => w.SourceType.Id), Is.EquivalentTo(new[] { "Blunt", "Slash" }));
            Assert.That(onChest.Any(w => w.Type.Id == "Puncture"), Is.False);
        });
    }

    [Test]
    public async Task DirectDamageSetReconcilesWounds()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var damageable = sysMan.GetEntitySystem<DamageableSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(WoundTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);
            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");

            damageable.ChangeDamage(uid, new DamageSpecifier(blunt, FixedPoint2.New(20)), ignoreResistances: true);
            Assert.That(woundable.Wounds, Has.Count.EqualTo(1));

            // Rejuvenate / admin heal set the pool directly with no per-type delta. The wound layer must follow
            // the pool down to zero instead of keeping phantom wounds forever.
            damageable.SetAllDamage(uid, FixedPoint2.Zero);

            Assert.That(damageable.GetTotalDamage(uid), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(woundable.Wounds, Is.Empty);
            Assert.That(entMan.HasComponent<ActiveWoundableComponent>(uid), Is.False);
        });
    }

    [Test]
    public async Task WoundMirrorsBackOntoItsOwnSourceType()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var damageable = sysMan.GetEntitySystem<DamageableSystem>();
        var wounds = sysMan.GetEntitySystem<WoundSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(WoundTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);
            var damage = entMan.GetComponent<DamageableComponent>(uid);
            var ent = new Entity<WoundableComponent>(uid, woundable);

            var shock = protoMan.Index<DamageTypePrototype>("Shock");
            var heat = protoMan.Index<DamageTypePrototype>("Heat");

            // Both map to the Burn wound, whose canonical type is Heat - but they must stay distinct wounds and
            // each must mirror onto the exact type that caused it, or a Shock burn would heal the Heat pool.
            damageable.ChangeDamage(uid, new DamageSpecifier(shock, FixedPoint2.New(10)), ignoreResistances: true);
            damageable.ChangeDamage(uid, new DamageSpecifier(heat, FixedPoint2.New(10)), ignoreResistances: true);

            Assert.That(woundable.Wounds.Count(w => w.Type.Id == "Burn"), Is.EqualTo(2));

            // DamageableComponent.Damage is access-locked; read the pool through the public API instead.
            var poolBefore = damageable.GetPositiveDamage((uid, damage));
            poolBefore.DamageDict.TryGetValue("Shock", out var shockPoolBefore);
            poolBefore.DamageDict.TryGetValue("Heat", out var heatPoolBefore);

            var shockBurn = woundable.Wounds.FindIndex(w => w.Type.Id == "Burn" && w.SourceType.Id == "Shock");
            Assert.That(shockBurn, Is.GreaterThanOrEqualTo(0));

            wounds.TreatWound(ent, shockBurn, WoundTreatment.Salved, FixedPoint2.New(5));

            // Only the Shock pool moved; the Heat pool the naive (mirror-to-canonical-type) implementation would
            // have drained instead is untouched. This is the crux of the many-to-one mapping fix.
            var poolAfter = damageable.GetPositiveDamage((uid, damage));
            poolAfter.DamageDict.TryGetValue("Heat", out var heatPoolAfter);
            poolAfter.DamageDict.TryGetValue("Shock", out var shockPoolAfter);
            Assert.That(heatPoolAfter, Is.EqualTo(heatPoolBefore));
            Assert.That(shockPoolAfter, Is.LessThan(shockPoolBefore));
        });
    }

    [Test]
    public async Task VanillaHealingReducesWounds()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var damageable = sysMan.GetEntitySystem<DamageableSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(WoundTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);
            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");

            damageable.ChangeDamage(uid, new DamageSpecifier(blunt, FixedPoint2.New(20)), ignoreResistances: true);
            Assert.That(woundable.Wounds, Has.Count.EqualTo(1));

            // Vanilla chemistry-style healing (negative delta) must pull the matching wound down with it.
            damageable.ChangeDamage(uid, new DamageSpecifier(blunt, FixedPoint2.New(-10)), ignoreResistances: true);

            Assert.That(damageable.GetTotalDamage(uid), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(woundable.Wounds, Has.Count.EqualTo(1));
            Assert.That(woundable.Wounds[0].Severity, Is.EqualTo(FixedPoint2.New(10)));
        });
    }

    [Test]
    public async Task TreatingWoundMirrorsHealingOntoPool()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var damageable = sysMan.GetEntitySystem<DamageableSystem>();
        var wounds = sysMan.GetEntitySystem<WoundSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(WoundTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);
            var ent = new Entity<WoundableComponent>(uid, woundable);

            wounds.ApplyDamageType(ent, "Slash", FixedPoint2.New(20), WoundBodyPart.Chest);
            // Wounds are a parallel layer; ApplyDamageType alone does not touch the pool.
            Assert.That(damageable.GetTotalDamage(uid), Is.EqualTo(FixedPoint2.Zero));

            // Simulate the pool actually carrying the damage, as it would after a real hit.
            var slash = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Slash");
            damageable.ChangeDamage(uid, new DamageSpecifier(slash, FixedPoint2.New(20)), ignoreResistances: true);

            var beforeTreat = damageable.GetTotalDamage(uid);

            // FR-2/FR-7: treating the wound must reduce the vanilla pool by the healed equivalent.
            var index = woundable.Wounds.FindIndex(w => w.Type.Id == "Cut");
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            wounds.TreatWound(ent, index, WoundTreatment.Bandaged, FixedPoint2.New(3));

            Assert.That(woundable.Wounds.First(w => w.Type.Id == "Cut").Treatment, Is.EqualTo(WoundTreatment.Bandaged));
            Assert.That(damageable.GetTotalDamage(uid), Is.EqualTo(beforeTreat - FixedPoint2.New(3)));
        });
    }

    [Test]
    public async Task StageNameFollowsSeverity()
    {
        var pair = Pair;
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var cut = protoMan.Index<WoundPrototype>("Cut");
            Assert.Multiple(() =>
            {
                Assert.That(cut.GetStageName(FixedPoint2.New(5)).ToString(), Is.EqualTo("wound-cut-stage-1"));
                Assert.That(cut.GetStageName(FixedPoint2.New(15)).ToString(), Is.EqualTo("wound-cut-stage-2"));
                Assert.That(cut.GetStageName(FixedPoint2.New(30)).ToString(), Is.EqualTo("wound-cut-stage-3"));
                Assert.That(cut.GetStageName(FixedPoint2.New(50)).ToString(), Is.EqualTo("wound-cut-stage-4"));
            });
        });
    }

    [Test]
    public async Task EntityWithoutWoundableTakesDamageNormally()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var damageable = sysMan.GetEntitySystem<DamageableSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(WoundTestMobNoWounds, map.MapCoords);
            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");

            // No WoundableComponent: damage must travel the pure vanilla path without error.
            damageable.ChangeDamage(uid, new DamageSpecifier(blunt, FixedPoint2.New(15)), ignoreResistances: true);

            Assert.That(damageable.GetTotalDamage(uid), Is.EqualTo(FixedPoint2.New(15)));
            Assert.That(entMan.HasComponent<WoundableComponent>(uid), Is.False);
        });
    }

    [Test]
    public async Task ForcedZoneIgnoresRollAndMultipliesSeverityNotPool()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var damageable = sysMan.GetEntitySystem<DamageableSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(TargetTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);
            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");

            // §B3/§B5: a Forced context lands on the exact zone even though Head's hit chance is 0.0, and the
            // per-zone severity multiplier (1.5) scales severity only - the vanilla pool is untouched.
            damageable.ChangeDamage(uid, new DamageSpecifier(blunt, FixedPoint2.New(10)), ignoreResistances: true,
                zoneContext: new DamageZoneContext(WoundBodyPart.Head, forced: true));

            Assert.That(damageable.GetTotalDamage(uid), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(woundable.Wounds, Has.Count.EqualTo(1));
            Assert.That(woundable.Wounds[0].Part, Is.EqualTo(WoundBodyPart.Head));
            Assert.That(woundable.Wounds[0].Severity, Is.EqualTo(FixedPoint2.New(15)));
        });
    }

    [Test]
    public async Task AimedZoneOverridesWeights()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var damageable = sysMan.GetEntitySystem<DamageableSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(TargetTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);
            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");

            // §B2: an aimed (non-Forced) context that passes its roll wins over the weight table (which would
            // only ever pick Chest here). LeftArm has hit chance 1.0.
            damageable.ChangeDamage(uid, new DamageSpecifier(blunt, FixedPoint2.New(10)), ignoreResistances: true,
                zoneContext: new DamageZoneContext(WoundBodyPart.LeftArm));

            Assert.That(woundable.Wounds, Has.Count.EqualTo(1));
            Assert.That(woundable.Wounds[0].Part, Is.EqualTo(WoundBodyPart.LeftArm));
        });
    }

    [Test]
    public async Task MissedAimDeviatesToFallbackZone()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var damageable = sysMan.GetEntitySystem<DamageableSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(TargetTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);
            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");

            // §B2: Head's hit chance is 0.0, so the aimed hit always misses and deviates to its fallback (Chest).
            damageable.ChangeDamage(uid, new DamageSpecifier(blunt, FixedPoint2.New(10)), ignoreResistances: true,
                zoneContext: new DamageZoneContext(WoundBodyPart.Head));

            Assert.That(woundable.Wounds, Has.Count.EqualTo(1));
            Assert.That(woundable.Wounds[0].Part, Is.EqualTo(WoundBodyPart.Chest));
        });
    }

    [Test]
    public async Task NonTargetedDamageUsesWeightTable()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var sysMan = server.ResolveDependency<IEntitySystemManager>();
        var damageable = sysMan.GetEntitySystem<DamageableSystem>();

        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(TargetTestMob, map.MapCoords);
            var woundable = entMan.GetComponent<WoundableComponent>(uid);
            var blunt = protoMan.Index<DamageTypePrototype>("Blunt");

            // §B2: damage with no zone context falls back to the profile's weight table, which only lists Chest.
            damageable.ChangeDamage(uid, new DamageSpecifier(blunt, FixedPoint2.New(10)), ignoreResistances: true);

            Assert.That(woundable.Wounds, Has.Count.EqualTo(1));
            Assert.That(woundable.Wounds[0].Part, Is.EqualTo(WoundBodyPart.Chest));
        });
    }
}
