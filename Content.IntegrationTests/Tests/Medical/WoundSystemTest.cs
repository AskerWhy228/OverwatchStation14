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
    public async Task ZoneWoundLimitIsRespected()
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

            // maxWounds is 2 for this test mob. Three distinct wound types on one zone must collapse to 2.
            wounds.ApplyDamageType(ent, "Blunt", FixedPoint2.New(10), WoundBodyPart.Chest);
            wounds.ApplyDamageType(ent, "Slash", FixedPoint2.New(10), WoundBodyPart.Chest);
            wounds.ApplyDamageType(ent, "Piercing", FixedPoint2.New(10), WoundBodyPart.Chest);

            Assert.That(woundable.Wounds.Count(w => w.Part == WoundBodyPart.Chest), Is.EqualTo(2));

            // No severity is lost when the third wound overflows into an existing one.
            var totalSeverity = woundable.Wounds.Aggregate(FixedPoint2.Zero, (acc, w) => acc + w.Severity);
            Assert.That(totalSeverity, Is.EqualTo(FixedPoint2.New(30)));
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
}
