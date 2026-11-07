using Arcanum.Data.Magic;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-DEV-259 (B14): the Destroy component (0x451AF0) on an item lying on the ground, as a thrown grenade ends its own BEGIN, and the runs'
// share of the destruction (0x459740). An invented grenade item (a generic item the hero created on the ground), an invented spell record.
[TestClass]
public sealed class SessionSpellDestroyTests
{
    private const string Grenade = "made:1";
    private static readonly SpellLocation Tile = new(6, 5);
    private static readonly SessionItemKind GrenadeKind = new(SessionItemType.Generic, 15172, 7001, 15172);

    // Every step is open: a path of one step a tile.
    private sealed class OpenDrop : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to;)
            {
                var rotation = Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y)));
                steps.Add(rotation);
                at = Step(at, rotation)!.Value;
            }
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    // The hero, and the grenade while the session lists it as lying (the real map reads the placed entry the same way).
    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, new(5, 5), HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Grenade when owner.QueryPlacedItems().Any(p => p.Entity == Grenade) => new(id, 14, Tile, HitPoints: 1),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    // A timed spell (so its run outlives the BEGIN) of the grenade on itself: its BEGIN holds a Destroy component applied to the self target.
    private static SessionSpellRecord Burst() =>
        SessionSpellRecords.Translate(Record(number: 91, beginTargets: 0x1, info: SpellInfo.Friendly, technology: true,
            components: [new(SpellAction.Begin, SpellComponentType.Destroy, new(0x1, 0, 0, 0, -1), 0, 0, null)])
        with { Cost = 0, Range = 99, DurationSeconds = 10, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
            DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0 });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, int Run, int Other) Lying()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var record = Burst();
        var created = owner.CreateGroundItem(owner.Version, GrenadeKind, SessionFixtures.Exterior, new CellCoord(6, 5), new OpenDrop());
        Assert.AreEqual((GameSessionDisposition.Changed, Grenade), (created.Disposition, created.Item));
        var caster = new SessionSpellCaster(owner, n => n == record.Data.Number ? record : null, new Map(owner), new Events());
        var grenade = caster.Facts(Grenade)!;
        int Start()
        {
            var started = owner.StartSpellRun(owner.Version, new(new(record.Data.Number, grenade, grenade, grenade, grenade.Location), Free: true),
                record.Run, caster.InvocationWorld(), owner.SpellRolls);
            Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
            return started.Creation!.Run!.Id;
        }
        return (owner, caster, Start(), Start());
    }

    [TestMethod]
    public void The_destroy_component_deletes_a_lying_item_and_ends_the_other_runs_it_is_the_parent_of_but_not_the_run_that_destroyed_it()
    {
        var (owner, caster, run, other) = Lying();
        using var _ = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, caster.Act(new(SessionSpellRunActionKind.Begin, run)).Disposition);
        Assert.IsTrue(owner.IsItemDestroyed(Grenade), "the item is deleted");
        Assert.AreEqual(0, owner.QueryPlacedItems().Count, "it no longer lies on the ground");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady), "a Destroy component on a lying item is done");
        Assert.IsNotNull(owner.QuerySpellRuns()[run], "the run that destroyed it is the processed one (the guard 0x5B0BA4): left alone, it goes on");
        Assert.IsNull(owner.QuerySpellRuns()[other], "a run of the item that never began ends by event and is freed");
    }

    [TestMethod]
    public void A_destroy_on_an_item_that_is_gone_or_not_lying_is_left_as_it_is_and_a_resisted_run_spares_an_unsummoned_item()
    {
        var (owner, caster, run, _) = Lying();
        using var _ = owner;
        var target = new SpellRunTarget(new(Grenade, 14), Tile);
        // A run with a resistance margin destroys only a summoned object (0x451AF0): the item stays.
        var resisted = owner.QuerySpellRuns()[run]! with { Margin = 5 };
        caster.Apply(resisted, 0, target);
        Assert.IsFalse(owner.IsItemDestroyed(Grenade));
        Assert.AreEqual(1, owner.QueryPlacedItems().Count);
        // Without a margin it is destroyed; a second Destroy meets an object no longer found and does nothing.
        caster.Apply(owner.QuerySpellRuns()[run]!, 0, target);
        Assert.IsTrue(owner.IsItemDestroyed(Grenade));
        var revision = owner.Version.Revision;
        caster.Apply(owner.QuerySpellRuns()[run]!, 0, target);
        Assert.AreEqual(revision, owner.Version.Revision, "an object already destroyed is left as it is");
        Assert.IsFalse(caster.Log.Any(e => e.Kind == SpellCastEventKind.ComponentNotReady));
        // The session refuses to destroy an item that is not lying on the ground.
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.DestroyLyingItem(owner.Version, Grenade).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.DestroyLyingItem(owner.Version, "made:9").Failure);
    }
}
