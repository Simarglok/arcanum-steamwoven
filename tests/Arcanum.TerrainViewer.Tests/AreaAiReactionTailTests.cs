using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1521 (VBG10, VB2-12): the tail of the reaction to an attack (0x4AA620). A surrendered NPC that keeps its surrender to another source runs the flee goal away from the new attacker (the
/// flee starter 0x434980), and every NPC that is not dead enters a running turn-based round at once (0x4B7E00), not at the next round's rescan. Invented NPCs (the rebind tests' fixture), no original data.
/// </summary>
[TestClass]
public sealed class AreaAiReactionTailTests
{
    private static readonly CellCoord Far = new(2, 2), Near = new(60, 14);

    // A (far from B, so the target choice keeps the hero) and B near the hero on the map's one sector.
    private static (AreaAiHostRebindTests.Fixture F, AreaAiHost Host) Open()
    {
        var f = AreaAiHostRebindTests.Open();
        return (f, f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [new(f.A, f.RowA, Far), new(f.B, f.RowB, Near)])));
    }

    private static void Surrender(AreaAiHostRebindTests.Fixture f, string npc, SessionCombatant to)
    {
        var set = f.Session.SetDangerState(f.Session.Version, new SessionDangerRequest { Entity = npc, State = DangerState.Surrender, Target = to });
        Assert.AreEqual(DangerState.Surrender, f.Session.QueryDanger(npc).State, $"setup: {set.Disposition} {set.Failure}");
    }

    // A round of the hero and A only, so B is not on its list.
    private static AreaCombatWorld RoundOfHeroAndA(AreaAiHostRebindTests.Fixture f) => new(1, () =>
        [new(SessionCombatant.Hero, new CellCoord(58, 12), 0, null), new(SessionCombatant.Critter(f.A), Far, 0, null)],
        entity => new SessionCombatantFacts { Speed = 7, Life = new LifeFacts() });

    private static bool Flees(AreaAiHost host, string npc) => host.RunOf(npc) is { } run && run.Bottom.Goal == GoalType.Flee;

    [TestMethod]
    public void A_surrendered_npc_attacked_by_a_new_attacker_it_keeps_its_surrender_over_runs_the_flee_goal_away_from_it()
    {
        var (f, host) = Open();
        using var _ = f;
        Surrender(f, f.B, SessionCombatant.Hero);

        host.SpellAttacked(f.A, f.B);

        Assert.AreEqual(AttackReactionStep.FleeGoal.ToString(), host.TraceOf(f.B)?.Reaction?.Split(' ')[0], "the reaction was the flee goal: " + host.TraceOf(f.B)?.Reaction);
        Assert.IsTrue(Flees(host, f.B), "the flee goal runs: " + host.TraceOf(f.B)?.Perform);
        Assert.AreEqual(f.A, host.RunOf(f.B)!.Bottom.Target, "away from the new attacker");
        Assert.AreEqual(DangerState.Surrender, f.Session.QueryDanger(f.B).State, "the danger state is untouched: the flee goal is only a run");
    }

    [TestMethod]
    public void A_surrendered_npc_that_surrendered_to_the_attacker_weighs_and_starts_no_flee_goal()
    {
        var (f, host) = Open();
        using var _ = f;
        Surrender(f, f.B, SessionCombatant.Critter(f.A));

        host.SpellAttacked(f.A, f.B);

        Assert.AreEqual(AttackReactionStep.Weigh.ToString(), host.TraceOf(f.B)?.Reaction?.Split(' ')[0], "it surrendered to the attacker: weigh: " + host.TraceOf(f.B)?.Reaction);
        Assert.IsFalse(Flees(host, f.B), "the reaction weighed (the setter's): no flee run from the reaction");
    }

    // ARC-UI-2079 (ledger TB28): the fleeing line (0x413EA0) of the surrendered NPC that runs from a new attacker goes to the speech display with the attacker as its listener.
    [TestMethod]
    public void A_surrendered_npc_that_runs_from_a_new_attacker_barks_its_fleeing_line_to_that_attacker()
    {
        var (f, host) = Open();
        using var _ = f;
        var barks = new List<(string Speaker, LoyaltyLines Lines, string? Listener)>();
        host.AiBarks = (speaker, lines, listener) => barks.Add((speaker, lines, listener));
        Surrender(f, f.B, SessionCombatant.Hero);

        host.SpellAttacked(f.A, f.B);

        Assert.AreEqual(1, barks.Count, "one bark: " + string.Join(";", barks));
        Assert.AreEqual((f.B, AreaAiHost.FleeBarkLines, f.A), barks[0], "the NPC speaks to the attacker");
        Assert.AreEqual(new LoyaltyLines(3000, 3099), barks[0].Lines);
    }

    [TestMethod]
    public void A_surrendered_npc_that_weighs_again_barks_nothing_it_was_already_in_the_state_and_a_host_without_barks_is_silent()
    {
        var (f, host) = Open();
        using var _ = f;
        var barks = new List<string>();
        host.AiBarks = (speaker, _, _) => barks.Add(speaker);
        Surrender(f, f.B, SessionCombatant.Critter(f.A));

        host.SpellAttacked(f.A, f.B);
        Assert.AreEqual(0, barks.Count, "the setter's line is for entering a fleeing or surrendered state only");

        host.AiBarks = null;
        Surrender(f, f.B, SessionCombatant.Hero);
        host.SpellAttacked(f.A, f.B);
    }

    [TestMethod]
    public void An_attacked_npc_joins_a_running_round_it_is_not_in_and_the_current_subturn_stays()
    {
        var (f, host) = Open();
        using var _ = f;
        var begun = f.Session.StartCombatRound(f.Session.Version, RoundOfHeroAndA(f));
        Assert.AreEqual(GameSessionDisposition.Changed, begun.Disposition);
        var round = f.Session.QueryCombatRound()!;
        var (actor, points) = (round.Actor, round.ActionPoints);
        CollectionAssert.DoesNotContain(round.Participants.ToList(), SessionCombatant.Critter(f.B));

        host.HeroAttacked(f.B);

        var after = f.Session.QueryCombatRound()!;
        CollectionAssert.Contains(after.Participants.ToList(), SessionCombatant.Critter(f.B), "the NPC joined the round at once: " + host.TraceOf(f.B)?.Reaction);
        Assert.AreEqual(SessionCombatant.Hero, after.Participants[^1], "the hero's party is last after the re-sort");
        Assert.AreEqual((actor, points), (after.Actor, after.ActionPoints), "whose subturn it is does not change");
        StringAssert.EndsWith(host.TraceOf(f.B)?.Reaction, "joined the round");

        var count = after.Participants.Count;
        host.HeroAttacked(f.B);
        Assert.AreEqual(count, f.Session.QueryCombatRound()!.Participants.Count, "an NPC already in the list is not added twice");
    }

    [TestMethod]
    public void With_no_round_running_the_reaction_enters_none()
    {
        var (f, host) = Open();
        using var _ = f;

        host.HeroAttacked(f.B);

        Assert.IsNull(f.Session.QueryCombatRound());
        Assert.IsFalse(host.TraceOf(f.B)?.Reaction?.EndsWith("joined the round") ?? false);
    }

    [TestMethod]
    public void A_dead_victim_ends_the_attack_event_before_the_reaction_and_does_not_enter_the_round()
    {
        var (f, host) = Open();
        using var _ = f;
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.StartCombatRound(f.Session.Version, RoundOfHeroAndA(f)).Disposition);
        var target = f.Combat.Build(f.Session, f.B).Combat?.Target ?? throw new AssertFailedException("the NPC has combat values");
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ApplyDamage(f.Session.Version, target, new LifeDamage { HitPoints = 10_000, PlainDeath = true }).Disposition);

        host.SpellAttacked(f.A, f.B);

        // The attack event stops at a dead victim before the reaction is reached; the reaction's own dead step (0x4AA620 returns first) is pinned in HostilityRulesTests.
        Assert.IsNull(host.TraceOf(f.B)?.Reaction, "no reaction ran for a dead victim");
        CollectionAssert.DoesNotContain(f.Session.QueryCombatRound()!.Participants.ToList(), SessionCombatant.Critter(f.B));
    }
}
