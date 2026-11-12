using Arcanum.Data.Characters;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1234 (ledger TA1, TA14, TA17): the run's object list (+0x130) through the cast host. An invented Congeal-Time-like record: its BEGIN area has the list bit and a radius (so the gather builds
// the run's list), its BEGIN component adds an effect to each target, and its END area is the list alone (so the END reaches exactly the stored objects) with a component that removes the effect.
[TestClass]
public sealed class SessionSpellRunListTests
{
    private const string Near = "oid:near", Far = "oid:far", Npc = "npc-a";
    private const int Number = 57, Effect = 62;
    private const ulong ListBit = 1UL << 63;

    private sealed class Map(GameSessionOwner owner) : ISpellCastMap
    {
        /// <summary>Whether the radius gather sees the two victims (false once they have walked away).</summary>
        public bool InRange { get; set; } = true;

        public SpellTargetObject? Object(string id) => id switch
        {
            SessionSpellCaster.Hero => new(id, SpellTargetObject.Pc, new(5, 5), HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Npc => new(id, SpellTargetObject.Npc, new(7, 5), HitPoints: 10),
            Near => new(id, SpellTargetObject.Npc, new(8, 5), HitPoints: 10),
            Far => new(id, SpellTargetObject.Npc, new(9, 6), HitPoints: 10),
            _ => null,
        };
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) =>
            InRange && (typeMask & 0x8) != 0 ? [Object(Near)!, Object(Far)!] : [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => entity is Npc or Near or Far ? new(SessionCritterCombat.From(entity, AttackStateTests.Npc(Thinker())).Combat!, 0, 0, 0, 0, 0) : null;

        private static int[] Thinker() { var stats = AttackStateTests.Stats(); stats[4] = 10; return stats; }
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static SpellComponent Part(SpellAction action, ulong area, SpellComponentData data) => new(action, SpellComponentType.Effect, new(area, 0, 0, 0, -1), 0, 0, data);

    // Spell 57 as Congeal Time is read (installed record 76): BEGIN gathers the critters in a radius by the list bit, END names the list only.
    private static SessionSpellRecord Congeal() =>
        SessionSpellRecords.Translate(Record(number: Number, beginTargets: 0, info: SpellInfo.Aggressive, components:
            [Part(SpellAction.Begin, 0x4, new EffectData(Effect, true, 1, 6, 0)), Part(SpellAction.End, 0, new EffectData(Effect, false, 1, 6, 0))])
        with { Cost = 0, MaintainCost = 0, MinimumWillpower = 0, MaintainPeriod = 0, Range = 99, DurationSeconds = 30, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0,
            DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = 0,
            Areas = [new(ListBit | 0x10000054UL, 0, 0, 9, -1), new(ListBit, 0, 0, 0, -1), new(ListBit, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static (GameSessionOwner Owner, SessionSpellCaster Caster, Map Map) Setup()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        var map = new Map(owner);
        var record = Congeal();
        var caster = new SessionSpellCaster(owner, n => n == Number ? record : null, map, new Events())
        {
            EffectDefinitions = n => n == Effect ? new SessionEffectDefinition(Effect, [new EffectChange(EffectAttributeKind.Stat, 11, EffectChangeOperator.Add, -5)]) : null,
        };
        return (owner, caster, map);
    }

    private static int Cast(GameSessionOwner owner, SessionSpellCaster caster)
    {
        var record = Congeal();
        var by = caster.Facts(SessionSpellCaster.Hero)!;
        var source = caster.Facts(Npc)!;
        var to = caster.Facts(Near)!;
        var started = owner.StartSpellRun(owner.Version, new(new(Number, by, source, to, to.Location), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        caster.Act(new(SessionSpellRunActionKind.Begin, run));
        return run;
    }

    private static int Count(GameSessionOwner owner, string npc) => owner.QueryNpcEffects(new Map(owner).Critter(npc)!.Combat.Target)?.Count(Effect) ?? 0;

    [TestMethod]
    public void The_begin_builds_the_runs_object_list_from_its_gather_and_the_end_takes_the_effect_off_every_listed_object()
    {
        var (owner, caster, _) = Setup();
        using var _ = owner;
        var run = Cast(owner, caster);
        Assert.IsFalse(caster.Log.Any(e => e.Kind is SpellCastEventKind.ComponentNotReady or SpellCastEventKind.EffectNotSaved), string.Join(",", caster.Log.Select(e => e.Kind)));
        Assert.AreEqual((1, 1), (Count(owner, Near), Count(owner, Far)), "BEGIN's gather reached both critters of the radius");
        CollectionAssert.AreEqual(new[] { Far, Near }, owner.QuerySpellRuns()[run]!.ObjectList.Items.Select(o => o.Id).ToArray(), "the gathered objects, newest first (the target is gathered first)");
        caster.Act(new(SessionSpellRunActionKind.Interrupt, run));
        Assert.AreEqual((0, 0), (Count(owner, Near), Count(owner, Far)), "the END's list area found both, so the effect is off both");
        Assert.IsNull(owner.QuerySpellRuns()[run], "the run is freed after its END");
    }

    [TestMethod]
    public void The_end_reads_the_stored_list_not_a_new_gather_and_the_list_is_built_once()
    {
        var (owner, caster, map) = Setup();
        using var _ = owner;
        var run = Cast(owner, caster);
        var stored = owner.QuerySpellRuns()[run]!.ObjectList;
        map.InRange = false;
        caster.Act(new(SessionSpellRunActionKind.Interrupt, run));
        Assert.AreEqual((0, 0), (Count(owner, Near), Count(owner, Far)), "the critters left the radius, but the END still reaches them through the stored list");
        Assert.AreEqual(2, stored.Count);
    }

    [TestMethod]
    public void The_durations_own_end_takes_the_effect_off_the_listed_objects_too()
    {
        var (owner, caster, map) = Setup();
        using var _ = owner;
        var run = Cast(owner, caster);
        map.InRange = false;
        for (var i = 0; i < 40 && owner.QuerySpellRuns()[run] is not null; i++)
        {
            Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 60_000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
            caster.ProcessEvents();
        }
        Assert.IsNull(owner.QuerySpellRuns()[run], "the duration ran out and the run ended");
        Assert.AreEqual((0, 0), (Count(owner, Near), Count(owner, Far)), "its END reached both critters through the stored list");
    }

    [TestMethod]
    public void A_run_begun_with_nothing_in_range_stores_only_what_its_gather_found()
    {
        // The list is the run's: the gather found only the target, so only the target is stored (TA14: the gathered objects, newest first).
        var (owner, caster, map) = Setup();
        using var _ = owner;
        map.InRange = false;
        var run = Cast(owner, caster);
        Assert.AreEqual(1, Count(owner, Near), "only the target itself was gathered");
        CollectionAssert.AreEqual(new[] { Near }, owner.QuerySpellRuns()[run]!.ObjectList.Items.Select(o => o.Id).ToArray());
        caster.Act(new(SessionSpellRunActionKind.Interrupt, run));
        Assert.AreEqual(0, Count(owner, Near));
    }
}
