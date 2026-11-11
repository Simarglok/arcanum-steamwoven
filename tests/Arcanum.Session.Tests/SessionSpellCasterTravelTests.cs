using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionSpellRecordsTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-058 (MT9b, ledger MX1-MX6; 0x44F7A0): the runs a hero's change to another map ends. Entities and values are invented.
[TestClass]
public sealed class SessionSpellCasterTravelTests
{
    private const string Target = "oid:target", Stays = "oid:stays";
    private const string Hero = SessionSpellCaster.Hero;
    private const int DispelNumber = 94, PlainNumber = 95, TechnologyNumber = 96;
    private static readonly SpellLocation HeroTile = new(5, 5), TargetTile = new(8, 5);

    private sealed class Map(GameSessionOwner owner, SpellCastCritter critter) : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => id switch
        {
            Hero => new(id, SpellTargetObject.Pc, HeroTile, HitPoints: owner.QueryHeroLife()!.Status.HitPoints),
            Target or Stays => new(id, SpellTargetObject.Npc, TargetTile, HitPoints: 5),
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
        public SpellCastCritter? Critter(string entity) => entity is Target or Stays ? critter : null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class NoCombat : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }

    private static SpellCastCritter TargetCritter(int critterFlags2 = 0)
    {
        var fields = AttackStateTests.Npc(stats: AttackStateTests.Stats(willpower: 9), critterFlags: 0);
        return new(SessionCritterCombat.From(Target, fields).Combat!, 0, 0, 0, 0, critterFlags2);
    }

    // A maintained spell of one BEGIN component on the object; the maintenance keeps the run alive after its BEGIN, so the guard of the processed run is seen.
    private static SessionSpellRecord Spell(int number, SpellComponentType type, SpellComponentData? data, bool technology = false) =>
        SessionSpellRecords.Translate(Record(number: number, beginTargets: 0x4, info: SpellInfo.Neutral, technology: technology,
            components: [new(SpellAction.Begin, type, new(0x4, 0, 0, 0, -1), 0, 0, data)])
            with { Cost = 10, MaintainCost = 3, MaintainPeriod = 10, Range = 99, DurationSeconds = 0, DurationStat = -1, DurationTriggerCount = 0, ResistStat = -1, NoStack = 0, NoReflect = true,
                DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, CancelsSpellFlags = 0, SpellFlagsSet = 0,
                Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)] });

    private static readonly Dictionary<int, SessionSpellRecord> Records = new[] { Spell(DispelNumber, SpellComponentType.Dispel, null), Spell(PlainNumber, SpellComponentType.NoOp, null),
        Spell(TechnologyNumber, SpellComponentType.NoOp, null, technology: true) }.ToDictionary(r => r.Data.Number);

    private static (GameSessionOwner Owner, SessionSpellCaster Caster) Session(int critterFlags2 = 0)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        return (owner, new SessionSpellCaster(owner, n => Records.GetValueOrDefault(n), new Map(owner, TargetCritter(critterFlags2)), new Events()));
    }

    // A run of a spell that never began: the ending by event frees it at once (0x4571E0), so it is seen gone.
    private static int Idle(GameSessionOwner owner, SessionSpellCaster caster, int number, string parent, string source, string target)
    {
        var record = Records[number];
        var who = caster.Facts(parent)!;
        var by = caster.Facts(source)!;
        var at = caster.Facts(target)!;
        var started = owner.StartSpellRun(owner.Version, new(new(number, who, by, at, at.Location), Free: true), record.Run, caster.InvocationWorld(), owner.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        return started.Creation!.Run!.Id;
    }


    private static string Trail(SessionSpellCaster caster) => string.Join(" | ", caster.Log.Select(e => $"{e.Kind}:{e.Value:X}:{e.Object}:{e.Detail}"));

    [TestMethod]
    public void A_change_of_map_ends_the_runs_that_name_an_object_that_stays_behind_and_keeps_the_others()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var onHero = Idle(owner, caster, PlainNumber, Hero, Hero, Hero);
        var onFollower = Idle(owner, caster, PlainNumber, Hero, Hero, Target);
        var onStranger = Idle(owner, caster, PlainNumber, Hero, Hero, Stays);
        var fromStranger = Idle(owner, caster, PlainNumber, Hero, Stays, Hero);
        var ended = caster.EndRunsLeavingMap([Target]);
        CollectionAssert.AreEqual(new[] { onStranger, fromStranger }, ended.ToArray(), "in slot order");
        Assert.IsNotNull(owner.QuerySpellRuns()[onHero], "the hero's own run goes with the hero");
        Assert.IsNotNull(owner.QuerySpellRuns()[onFollower], "a run on a follower that travels goes with it");
        Assert.IsNull(owner.QuerySpellRuns()[onStranger]);
        Assert.IsNull(owner.QuerySpellRuns()[fromStranger]);
    }

    [TestMethod]
    public void A_maintained_run_that_began_ends_through_its_forced_end_and_takes_its_ticks_and_bar_slot()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        var run = Idle(owner, caster, PlainNumber, Hero, Hero, Stays);
        caster.Act(new(SessionSpellRunActionKind.Begin, run));
        Assert.IsNotNull(owner.QuerySpellRuns()[run], Trail(caster));
        var ticks = owner.QueryEvents().Count(e => e.Kind == SessionEventKinds.SpellRun && e.Payload.Values[1] == run);
        CollectionAssert.AreEqual(new[] { run }, caster.EndRunsLeavingMap([]).ToArray());
        Assert.IsNull(owner.QuerySpellRuns()[run], "the forced END frees the run");
        Assert.AreEqual(0, owner.QueryEvents().Count(e => e.Kind == SessionEventKinds.SpellRun && e.Payload.Values[1] == run), $"its {ticks} ticks are removed");
    }

    [TestMethod]
    public void A_change_of_map_with_no_runs_or_only_travelling_ones_ends_nothing()
    {
        var (owner, caster) = Session();
        using var _ = owner;
        Assert.AreEqual(0, caster.EndRunsLeavingMap([]).Count);
        var run = Idle(owner, caster, PlainNumber, Hero, Hero, Target);
        Assert.AreEqual(0, caster.EndRunsLeavingMap([Target]).Count);
        Assert.IsNotNull(owner.QuerySpellRuns()[run]);
    }
}
