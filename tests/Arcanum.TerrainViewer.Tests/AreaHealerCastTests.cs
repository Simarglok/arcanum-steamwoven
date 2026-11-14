using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1790 (ledger HLR11, DSV3): the healer talk's spell effect over a cast host, no original data. <see cref="AreaAiHost.HealerCast"/> is the script cast entry with the NPC as the caster and the hero as the
/// target (0x419D50 → 0x455A20 → 0x455AC0): the run starts with the free flag (0x2) exactly when the host's caller says no PC leads the NPC (<c>AreaHealerFacts.NoPcLeader</c>, the dialog's <c>Free</c>), the casting goal
/// plays and the hero's hit points rise. The NPC here is the invented human of the rebind tests' fixture, the spell an invented healing one (no book is needed: the script entry point reads none); the hero is hurt first.
/// </summary>
[TestClass]
public sealed class AreaHealerCastTests
{
    private const int Spell = 60, Cost = 4, Healed = 7;
    private static readonly CellCoord HeroCell = new(58, 12), NpcCell = new(59, 14);

    // An invented healing spell of the healing college's number: BEGIN heals its target by 7 (no save, any target), cost 4 fatigue, no casting animation (the casting goal 26).
    private static SessionSpellRecord Record() => SessionSpellRecords.Translate(new SpellRecord
    {
        Number = Spell, MinimumWillpower = 0, Cost = Cost, MaintainCost = 0, MaintainPeriod = 0, DurationSeconds = 0, DurationStat = -1, DurationStatLevel = 0, DurationStatModifier = 1, DurationTriggerCount = 0,
        ResistStat = -1, ResistModifier = 0, Range = 99, IsTechnology = false, ChargesFromCells = false, SkipsBeginCost = false, Info = SpellInfo.Friendly, Enabled = true, NoResist = false, NoReflect = true,
        CastingAnimation = -1, Missile = -1, CasterEyeCandy = [-1, -1, -1, -1, -1], TargetEyeCandy = [-1, -1, -1, -1, -1], NoStack = 0, CancelsSpellFlags = 0, CancelsEnvironmentSpellFlags = 0,
        DisallowedCasterSpellFlags = 0, DisallowedTargetSpellFlags = 0, DisallowedTargetCritterFlags = 0, ItemTriggers = 0, AiPriorities = [-1, -1, -1, -1, -1, -1, -1, -1, -1, -1], AiDefensiveSecond = 0,
        LinesPresent = [true, true, false, false, false], KeysRead = [],
        Areas = [new(0x4, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1), new(0, 0, 0, 0, -1)],
        Components = [new(SpellAction.Begin, SpellComponentType.Heal, new(0x4, 0, 0, 0, -1), 0, 0, new DamageData(0, Healed, Healed, 0))],
        HasDamage = true, SpellFlagsSet = 0, AreaLine = null, ComponentLines = [],
    });

    private sealed record Open(AreaAiHostRebindTests.Fixture F, AreaAiHost Host, SessionSpellCaster Caster) : IDisposable
    {
        public void Dispose() => F.Dispose();
    }

    // The fixture's NPC B stands next to the hero's neighbourhood; the cast host answers the scene (the viewer's shape), the hero is hurt by 20 hit points.
    private static Open Create(bool led = false, bool talking = false)
    {
        var f = AreaAiHostRebindTests.Open();
        var steps = AreaAiHostRebindTests.Sector(0, 0);
        AreaAiHost? host = null;
        var spellMap = new AreaSpellCastMap(f.Session, 1, steps, [f.RowA, f.RowB], f.Combat, () => HeroCell, () => null,
            placed: () => host?.Movers.All.ToDictionary(m => m.Key, m => ((long X, long Y)?)(m.Cell.X, m.Cell.Y)) ?? []);
        var events = new AreaEventWorld(() => f.Combat, () => null, ai: () => host);
        var caster = new SessionSpellCaster(f.Session, n => n == Spell ? Record() : null, spellMap, events);
        var scene = f.Scene(0, steps, [new(f.B, f.RowB, NpcCell)]) with { Spells = () => caster, ConversationPartner = talking ? () => f.B : null };
        host = f.Build(scene);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ApplyDamage(f.Session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 20 }).Disposition, "the hero is hurt");
        if (led) Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ForceJoinParty(f.Session.Version, f.B).Disposition, "the NPC follows the hero");
        return new(f, host, caster);
    }

    private static int HeroHitPoints(Open o) => o.F.Session.QueryHeroLife()!.Status.HitPoints;

    // Steps the host until the NPC's cast ended (the casting goal's run), at most 200 frames of 50 ms; the notices given.
    private static List<AreaAiNotice> Play(Open o, ref long now)
    {
        var notices = new List<AreaAiNotice>();
        for (var frame = 0; frame < 200 && (frame == 0 || o.Host.RunOf(o.F.B) is not null); frame++)
        {
            o.Host.Frames().AdvanceHost(50); now += 50;
            notices.AddRange(o.Host.DrainNotices());
        }
        return notices;
    }

    [TestMethod]
    public void A_leaderless_healer_casts_free_and_the_heros_hit_points_rise()
    {
        using var o = Create();
        var facts = o.Host.HealerFacts(o.F.B)!.Value;
        Assert.IsTrue(facts.NoPcLeader && !facts.LedByPc, "no PC leads the NPC (0x45DDA0 answers 0): the dialog asks the cast free");
        var before = HeroHitPoints(o);
        Assert.IsTrue(o.Host.HealerCast(o.F.B, Spell, free: facts.NoPcLeader), "the cast host starts it");
        var goal = o.Host.RunOf(o.F.B)?.Bottom ?? throw new AssertFailedException("no run: " + o.Host.TraceOf(o.F.B)?.Cast);
        Assert.AreEqual((GoalType.CastSpell, AiSchedulerRules.Pc), (goal.Goal, goal.Target), "the casting goal on the hero: " + o.Host.TraceOf(o.F.B)?.Cast);
        var run = o.F.Session.QuerySpellRuns()[goal.SpellRun ?? -1] ?? throw new AssertFailedException("no magic run");
        Assert.AreEqual((Spell, AiSchedulerRules.Pc), (run.Spell, run.Target?.Id), "the run is the healing spell aimed at the hero");
        Assert.IsTrue((run.Flags & SpellRunFlags.Free) != 0, "AC1: the flag word has the free flag 0x2");
        Assert.AreEqual((SpellRunFlags)0, run.Flags & SpellRunFlags.Unresistable, "the healer's cast asks no unresistable flag");

        var now = 0L;
        Play(o, ref now);
        Assert.IsNull(o.Host.RunOf(o.F.B), "the casting goal ended");
        Assert.AreEqual(before + Healed, HeroHitPoints(o), "the hero's hit points rose by the spell's heal: " + string.Join(",", o.Caster.Log.Select(e => $"{e.Kind}:{e.Value}")));
        Assert.IsFalse(o.Caster.Log.Any(e => e.Kind == SpellCastEventKind.FatiguePaid), "a free cast pays nothing");
    }

    [TestMethod]
    public void A_healer_a_pc_leads_casts_at_the_normal_cost_and_the_heros_hit_points_rise()
    {
        using var o = Create(led: true);
        var facts = o.Host.HealerFacts(o.F.B)!.Value;
        Assert.IsTrue(facts.LedByPc && !facts.NoPcLeader, "the hero leads the NPC: the dialog serves at once and asks no free flag");
        var before = HeroHitPoints(o);
        Assert.IsTrue(o.Host.HealerCast(o.F.B, Spell, free: facts.NoPcLeader));
        var goal = o.Host.RunOf(o.F.B)?.Bottom ?? throw new AssertFailedException("no run: " + o.Host.TraceOf(o.F.B)?.Cast);
        var run = o.F.Session.QuerySpellRuns()[goal.SpellRun ?? -1] ?? throw new AssertFailedException("no magic run");
        Assert.AreEqual((SpellRunFlags)0, run.Flags & SpellRunFlags.Free, "AC1: no free flag for a follower of a PC");

        var now = 0L;
        Play(o, ref now);
        Assert.AreEqual(before + Healed, HeroHitPoints(o), "the hero's hit points rose: " + string.Join(",", o.Caster.Log.Select(e => $"{e.Kind}:{e.Value}")));
        Assert.IsTrue(o.Caster.Log.Any(e => e.Kind == SpellCastEventKind.FatiguePaid), "the follower paid the cast as any cast does");
    }

    [TestMethod]
    public void The_cast_lands_while_the_conversation_holds_the_partner()
    {
        // The talk's gate holds the speaker's AI pass (GT138: the host answers the speaker as the hero's conversation partner); the casting goal of the run the talk started plays on.
        using var o = Create(talking: true);
        Assert.AreEqual(AiConversation.Pc, o.Host.Conversation(o.F.B), "the speaker is the hero's partner: the gate holds it");
        var before = HeroHitPoints(o);
        Assert.IsTrue(o.Host.HealerCast(o.F.B, Spell, free: true));
        var now = 0L;
        Play(o, ref now);
        Assert.AreEqual(before + Healed, HeroHitPoints(o), "the heal landed while the partner was held");
        Assert.AreEqual(AiConversation.Pc, o.Host.Conversation(o.F.B), "the talk is still open");
    }
}
