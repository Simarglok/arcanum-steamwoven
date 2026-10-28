using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-MAGIC-014 on the installed original (opt-in, <c>ARC_AREA_CONVERSATION_GAME_ROOT</c> and the map's mobile folder in
/// <c>ARC_AREA_MOBILE</c>): the first cast without a window. A pre-made hero learns the first rank-1 college spell whose BEGIN has a
/// Damage component (from the data, no spell named), stands next to the first living NPC of the map, and casts it through the cast
/// host: the invocation check, the run, the casting goal (action points, the pre-animation check), the release frame's BEGIN with
/// its payment, the per-target stage and resistance, and the damage step. Its fatigue drops and the NPC resists or takes damage.
/// Numbers only; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class AreaSpellCastRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_hero_learns_a_damage_spell_and_casts_it_at_an_npc()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        var mobile = Environment.GetEnvironmentVariable("ARC_AREA_MOBILE");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(mobile))
            Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT and ARC_AREA_MOBILE are required.");
        string P(string relative) => Path.Combine(root, relative);
        var module = P("modules/Arcanum.dat");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var data = AreaOriginalValidator.OpenFiles(module, P("arcanum1.dat"), P("arcanum2.dat"), mobile, P("data/proto"));
        var snapshot = data.LoadStrict();
        var steps = AreaStepInspector.ValidateOriginalData(snapshot, module, P("arcanum2.dat"), P("arcanum3.dat"), P("modules/Arcanum.PATCH0"), mobile, 1);
        var inventory = AreaInventoryMap.Build(snapshot, steps).Map!;
        var combat = new AreaCombatHost(new AreaInventoryHost([inventory]));
        Trace($"map loaded in {clock.Elapsed.TotalSeconds:F0} s");
        var spellData = AreaSpellsPage.LoadData(module).Data ?? throw new AssertFailedException("spell data");
        var records = Enumerable.Range(0, SpellRecord.Count).Select(n => SessionSpellRecords.Translate(spellData.Records[n])).ToArray();

        // College spells that damage a critter at BEGIN and cost fatigue.
        bool Damaging(SessionSpellRecord r) => r.Data.Number < SpellRecord.CollegeSpells && r.Data.Cost > 0 && (r.Areas[0].Targets & 0x40) != 0
            && r.Of(SpellRunAction.Begin).Any(c => c.Type == SpellComponentType.Damage && c.Damage!.Maximum > 0);
        // The first living NPC with combat values and a free admitted cell beside it for the hero.
        var occupied = snapshot.Objects.Where(o => o.WorldX is not null).Select(o => (o.WorldX!.Value, o.WorldY!.Value)).ToHashSet();
        (string Entity, AreaObjectRecord Row, AreaStepCell Hero)? Pick()
        {
            foreach (var row in snapshot.Objects.Where(o => o.Type == 16 && o.WorldX is not null))
            {
                if (combat.Entity(snapshot.RuntimeMap, row.Key) is not { } entity || combat.Build(snapshot.RuntimeMap, entity).Combat is not { } npc
                    || LifeRules.IsDead(npc.Loaded, npc.Facts)) continue;
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                {
                    var cell = new AreaStepCell(row.WorldX!.Value + dx, row.WorldY!.Value + dy);
                    if (steps.IsAdmitted(cell) && !occupied.Contains((cell.X, cell.Y))) return (entity, row, cell);
                }
            }
            return null;
        }
        var (entity, target, heroCell) = Pick() ?? throw new AssertFailedException("no NPC to cast at");

        Trace($"target chosen at {clock.Elapsed.TotalSeconds:F0} s");
        // The first pre-made hero that knows such a spell after an experience award (its auto-level scheme may buy one) or has a point
        // left to learn the first rank-1 one.
        GameSessionOwner? session = null;
        SessionSpellRecord? spell = null;
        foreach (var premade in AreaStartingHero.Premade)
        {
            var candidate = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(), heroCell,
                snapshot.RuntimeMap, AreaStartingHero.Load(module, premade)).Owner!;
            Assert.AreEqual(GameSessionDisposition.Changed, candidate.AwardHeroExperience(candidate.Version, 2000).Disposition);
            var book = candidate.QueryHeroSpellBook()!;
            spell = records.FirstOrDefault(r => Damaging(r) && r.Data.Number % SpellRecord.Ranks < book.Ranks[r.Data.Number / SpellRecord.Ranks]);
            if (spell is null && candidate.QueryHeroExperience()!.UnspentPoints > 0
                && records.FirstOrDefault(r => Damaging(r) && r.Data.Number % SpellRecord.Ranks == 0 && book.Ranks[r.Data.Number / SpellRecord.Ranks] == 0) is { } first
                && candidate.SpendHeroPoint(candidate.Version, AdvancementTrack.College, first.Data.Number / SpellRecord.Ranks).Disposition == GameSessionDisposition.Changed)
                spell = first;
            if (spell is not null) { session = candidate; Trace($"hero {premade.Index} casts spell {spell.Data.Number}"); break; }
            candidate.Dispose();
        }
        if (session is null || spell is null) throw new AssertFailedException("no pre-made hero knows or can learn a damaging college spell");
        using var owned = session;
        Assert.IsTrue(spell.Data.Number % SpellRecord.Ranks < session.QueryHeroSpellBook()!.Ranks[spell.Data.Number / SpellRecord.Ranks], "the hero knows the spell");

        var lineOfFire = new AreaLineOfFire(steps, snapshot.Objects, new HashSet<AreaObjectKey>(), new Dictionary<CellCoord, AreaObjectRecord>(),
            _ => false, row => row.Type == 16 && combat.Entity(snapshot.RuntimeMap, row.Key) is { } e && combat.Build(snapshot.RuntimeMap, e).Combat is { } c
                && LifeRules.IsDead(c.Loaded, c.Facts), _ => null);
        var map = new AreaSpellCastMap(session, snapshot.RuntimeMap, steps, snapshot.Objects, combat,
            () => new CellCoord(checked((int)heroCell.X), checked((int)heroCell.Y)), () => lineOfFire);
        var caster = new SessionSpellCaster(session, n => n is >= 0 and < SpellRecord.Count ? records[n] : null, map, new Events(combat, snapshot.RuntimeMap));
        var npcTarget = map.Critter(entity)!.Combat.Target;
        var fatigueBefore = session.QueryHeroLife()!.Status.Fatigue;
        var hitPointsBefore = session.QueryLife(npcTarget).View!.Status.HitPoints;

        var heroFacts = caster.Facts(SessionSpellCaster.Hero)!;
        var targetFacts = caster.Facts(entity)!;
        Trace($"caster ready at {clock.Elapsed.TotalSeconds:F0} s");
        var started = session.StartSpellRun(session.Version, new(new(spell.Data.Number, heroFacts, heroFacts, targetFacts, targetFacts.Location,
            ParentIsLocalPlayer: true)), spell.Run, caster.InvocationWorld(), session.SpellRolls);
        Assert.AreEqual(GameSessionDisposition.Changed, started.Disposition, $"{started.Failure} {started.Creation?.Status} {started.Creation?.Check}");
        var run = started.Creation!.Run!.Id;
        Assert.AreEqual(SpellCastGoalOutcome.Started, caster.StartGoal(run, new NoCombat()).Outcome);
        var released = caster.Release(run) ?? throw new AssertFailedException("the target re-check at the release frame failed");
        Assert.AreEqual(GameSessionDisposition.Changed, released.Disposition, released.Failure.ToString());
        session.AdvanceGameTime(session.QueryClockSnapshot().Token, 60_000, Arcanum.Simulation.Clock.ClockAdvanceReason.OrdinaryAdvance);
        caster.ProcessEvents();

        Trace($"cast done at {clock.Elapsed.TotalSeconds:F0} s");
        var fatigueAfter = session.QueryHeroLife()!.Status.Fatigue;
        var hitPointsAfter = session.QueryLife(npcTarget).View!.Status.HitPoints;
        var damaged = caster.Log.Where(e => e.Kind == SpellCastEventKind.Damaged && e.Object == entity).Sum(e => e.Value);
        var resisted = caster.Log.Any(e => e.Kind == SpellCastEventKind.Resisted && e.Object == entity);
        Console.WriteLine($"spell {spell.Data.Number} cost {spell.Data.Cost}; fatigue {fatigueBefore} -> {fatigueAfter}; target hp {hitPointsBefore} -> {hitPointsAfter};"
            + $" damaged {damaged}, resisted {resisted}; log {string.Join(",", caster.Log.Select(e => $"{e.Kind}:{e.Value}"))}");
        Assert.IsTrue(fatigueAfter < fatigueBefore, "the cast cost fatigue");
        Assert.IsTrue(resisted || damaged > 0 && hitPointsAfter == hitPointsBefore - damaged || hitPointsAfter <= 0,
            "the target resists or takes the damage");
    }

    // A progress line (numbers only).
    private static void Trace(string line) => Console.WriteLine(line);

    private sealed class Events(AreaCombatHost combat, int map) : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId at, string entity) => combat.Facts(at.RuntimeMap == 0 ? map : at.RuntimeMap, entity);
    }

    private sealed class NoCombat : ISessionCombatWorld
    {
        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];
        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }
}
