using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-312: the dying script (attachment point 12) of the kill 0x45DA20 as the session's life step asks it: after the hit points are written, before the death processing; skip default stops the processing. Entity ids and facts are invented.
[TestClass]
public sealed class DyingScriptTests
{
    private const ulong Seed = 0x0123456789ABCDEFUL;
    private static readonly LifeFacts Wolf = new();

    private static SessionLifeTarget Npc(string entity = "oid:wolf") => SessionLifeTarget.Critter(entity, Wolf, default);

    private static GameSessionOwner Seeded()
    {
        var result = GameSessionOwner.Create(Descriptor() with { RandomSeed = Seed });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static LifeDamage Lethal => new() { HitPoints = 1_000 };

    [TestMethod]
    public void A_death_without_a_dying_script_host_is_processed_as_before()
    {
        using var owner = Seeded();
        var killed = owner.ApplyDamage(owner.Version, Npc(), Lethal);
        Assert.IsTrue(killed.Outcome!.Events.HasFlag(LifeEvents.Died));
        Assert.IsFalse(killed.DeathStopped);
        Assert.IsNotNull(owner.QueryLifeRecord("oid:wolf")!.Value.DeathTimeMs);
    }

    [TestMethod]
    public void The_dying_script_is_asked_after_the_hit_points_and_before_the_death_time_with_the_killer()
    {
        using var owner = Seeded();
        var asked = new List<(SessionDyingRequest Request, LifeStatus? Status, long? DeathTime)>();
        owner.DyingScript = request =>
        {
            asked.Add((request, owner.QueryLife(Npc()).View?.Status, owner.QueryLifeRecord("oid:wolf")!.Value.DeathTimeMs));
            return true;
        };
        var killed = owner.ApplyDamage(owner.Version, Npc(), Lethal, killer: SessionSpellCaster.Hero);
        Assert.AreEqual(1, asked.Count);
        Assert.AreEqual(new SessionDyingRequest("oid:wolf", owner.QueryActor().Map.RuntimeMap, SessionSpellCaster.Hero), asked[0].Request);
        Assert.IsTrue(asked[0].Status!.HitPoints <= 0, "the hit points are written when the script runs");
        Assert.IsNull(asked[0].DeathTime, "the death time is stamped after the script");
        Assert.IsFalse(killed.DeathStopped);
        Assert.IsNotNull(owner.QueryLifeRecord("oid:wolf")!.Value.DeathTimeMs, "run default: the death processing went on");
    }

    [TestMethod]
    public void Skip_default_stops_the_death_processing_and_leaves_the_hit_points_written()
    {
        using var owner = Seeded();
        owner.DyingScript = _ => false;
        var killed = owner.ApplyDamage(owner.Version, Npc(), Lethal, killer: "oid:orc");
        Assert.IsTrue(killed.DeathStopped);
        Assert.AreEqual(GameSessionDisposition.Changed, killed.Disposition);
        Assert.IsTrue(killed.Outcome!.Events.HasFlag(LifeEvents.Died), "the step still says what it did to the hit points");
        Assert.IsTrue(owner.QueryLife(Npc()).View!.Status.HitPoints <= 0, "the hit points stay written");
        Assert.IsNull(owner.QueryLifeRecord("oid:wolf")!.Value.DeathTimeMs, "no death time: the kill ended before it");
    }

    [TestMethod]
    public void The_killer_comes_from_the_damage_parties_when_none_is_named_and_a_damage_with_no_source_has_none()
    {
        using var owner = Seeded();
        var killers = new List<string?>();
        owner.DyingScript = request => { killers.Add(request.Killer); return true; };
        owner.ApplyDamage(owner.Version, Npc("oid:a"), Lethal, new SessionDamageParties("oid:parent", "oid:source"));
        owner.ApplyDamage(owner.Version, Npc("oid:b"), Lethal, new SessionDamageParties(null, null));
        owner.Kill(owner.Version, Npc("oid:c"));
        owner.Kill(owner.Version, Npc("oid:d"), "oid:killer");
        CollectionAssert.AreEqual(new string?[] { "oid:source", null, null, "oid:killer" }, killers);
    }

    [TestMethod]
    public void Only_a_death_asks_the_script_and_the_hero_and_a_second_kill_of_a_dead_npc_do_not()
    {
        using var owner = Seeded();
        var asked = 0;
        owner.DyingScript = _ => { asked++; return true; };
        owner.ApplyDamage(owner.Version, Npc(), new LifeDamage { HitPoints = 3 });
        Assert.AreEqual(0, asked, "a hit that does not kill");
        owner.ApplyDamage(owner.Version, Npc(), Lethal);
        Assert.AreEqual(1, asked);
        owner.Kill(owner.Version, Npc());
        Assert.AreEqual(1, asked, "a dead NPC dies once");
        owner.Kill(owner.Version, SessionLifeTarget.Hero);
        Assert.AreEqual(1, asked, "the hero has no dying script here");
    }

    [TestMethod]
    public void A_dying_script_may_write_the_session_and_the_step_still_commits_one_consistent_version()
    {
        using var owner = Seeded();
        long? inside = null;
        owner.DyingScript = _ =>
        {
            inside = owner.Version.Revision;
            // The script's own store: a global flag (the fixture's domain is small; the installed death scripts set 2365).
            var flags = owner.Flags;
            Assert.AreEqual(GlobalFlagWriteDisposition.Applied,
                flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(3), true)).Disposition);
            return false;
        };
        var before = owner.Version.Revision;
        var killed = owner.Kill(owner.Version, Npc());
        Assert.AreEqual(before, inside, "the script sees the version before the step's commit");
        Assert.AreEqual(owner.Version, killed.Version);
        Assert.IsTrue(owner.Version.Revision > before, "the step's own commit comes after the script ran");
        Assert.IsTrue(owner.Flags.Read(new(new(Guid.NewGuid()), owner.FlagVersion, new FlagId(3))).Value == true, "the script's write is kept");
    }

    [TestMethod]
    public void A_host_that_throws_counts_as_run_default_and_the_step_still_commits()
    {
        using var owner = Seeded();
        owner.DyingScript = _ => throw new InvalidOperationException("host failure");
        var killed = owner.Kill(owner.Version, Npc());
        Assert.IsFalse(killed.DeathStopped);
        Assert.AreEqual(owner.Version, killed.Version);
        Assert.IsNotNull(owner.QueryLifeRecord("oid:wolf")!.Value.DeathTimeMs);
    }
    // ARC-AI-1134 (DY3, DY7): the kill's focus step and the host's kill notification after the death processing.
    [TestMethod]
    public void A_killed_npc_has_the_killer_as_combat_focus_whatever_its_state_and_a_killless_damage_clears_it()
    {
        using var owner = Seeded();
        owner.ApplyDamage(owner.Version, Npc("oid:a"), Lethal, killer: SessionSpellCaster.Hero);
        Assert.AreEqual(SessionCombatant.Hero, owner.QueryDanger("oid:a").Focus, "the hero killed it");
        Assert.AreEqual(DangerState.None, owner.QueryDanger("oid:a").State, "the focus handle only; the state read is the AI's");
        owner.ApplyDamage(owner.Version, Npc("oid:b"), Lethal, killer: "oid:orc");
        Assert.AreEqual(SessionCombatant.Critter("oid:orc"), owner.QueryDanger("oid:b").Focus);
        Assert.AreEqual(SessionCombatant.Critter("oid:src"), Focused(owner, "oid:c", "oid:src").Focus, "the damage parties name the killer");
        Assert.IsNull(Focused(owner, "oid:d", null).Focus, "no source: the focus becomes none");
        // A killer-less kill clears a focus the NPC had.
        var set = owner.SetDangerState(owner.Version, new SessionDangerRequest { Entity = "oid:e", State = DangerState.CombatFocus, Target = SessionCombatant.Critter("oid:orc") });
        Assert.AreEqual(GameSessionFailure.None, set.Failure);
        Assert.AreEqual(SessionCombatant.Critter("oid:orc"), owner.QueryDanger("oid:e").Focus);
        owner.ApplyDamage(owner.Version, Npc("oid:e"), Lethal);
        Assert.IsNull(owner.QueryDanger("oid:e").Focus);
    }

    private static SessionDanger Focused(GameSessionOwner owner, string entity, string? killer)
    {
        owner.ApplyDamage(owner.Version, Npc(entity), Lethal, new SessionDamageParties(null, killer));
        return owner.QueryDanger(entity);
    }

    [TestMethod]
    public void The_kill_notice_follows_the_committed_death_and_the_host_may_write_the_session()
    {
        using var owner = Seeded();
        var told = new List<SessionKillNoticeRequest>();
        owner.KillNotice = request =>
        {
            told.Add(request);
            Assert.IsNotNull(owner.QueryLifeRecord(request.Victim)!.Value.DeathTimeMs, "the death time is stamped before the notice");
            Assert.AreEqual(SessionCombatant.Hero, owner.QueryDanger(request.Victim).Focus, "the focus is written before the notice");
            var applied = owner.ApplyKillNotification(owner.Version, new SessionKillNotificationRequest
            {
                Victim = SessionCombatant.Critter(request.Victim), Plan = new KillNotificationPlan(new StopFleeingStep(false, false), ["oid:mate"]),
            });
            Assert.AreEqual(GameSessionFailure.None, applied.Failure, "the version the host sees is current");
        };
        var killed = owner.ApplyDamage(owner.Version, Npc(), Lethal, killer: SessionSpellCaster.Hero);
        Assert.AreEqual(new SessionKillNoticeRequest("oid:wolf", owner.QueryActor().Map.RuntimeMap, SessionSpellCaster.Hero), told.Single());
        Assert.AreEqual(owner.Version, killed.Version, "the result names the revision after the host's write");
        Assert.IsTrue(owner.QueryDanger("oid:mate").Flags.HasFlag(DangerFlags.BackingOff), "the plan's back-off was written");
    }

    [TestMethod]
    public void The_kill_notice_is_told_for_an_npc_death_only_and_not_after_skip_default()
    {
        using var owner = Seeded();
        var told = 0;
        owner.KillNotice = _ => told++;
        owner.ApplyDamage(owner.Version, Npc(), new LifeDamage { HitPoints = 3 });
        Assert.AreEqual(0, told, "a hit that does not kill");
        owner.Kill(owner.Version, SessionLifeTarget.Hero);
        Assert.AreEqual(0, told, "the hero's death has no notification");
        owner.DyingScript = request => request.Victim != "oid:stopped";
        owner.Kill(owner.Version, Npc("oid:stopped"));
        Assert.AreEqual(0, told, "skip default stops the whole processing");
        Assert.IsNull(owner.QueryDanger("oid:stopped").Focus, "no focus step either");
        owner.Kill(owner.Version, Npc("oid:wolf"));
        Assert.AreEqual(1, told);
        owner.Kill(owner.Version, Npc("oid:wolf"));
        Assert.AreEqual(1, told, "a dead NPC dies once");
    }

    [TestMethod]
    public void A_kill_notice_host_that_throws_does_not_undo_the_death()
    {
        using var owner = Seeded();
        owner.KillNotice = _ => throw new KeyNotFoundException("host failure");
        var killed = owner.Kill(owner.Version, Npc());
        Assert.AreEqual(GameSessionDisposition.Changed, killed.Disposition);
        Assert.AreEqual(owner.Version, killed.Version);
        Assert.IsNotNull(owner.QueryLifeRecord("oid:wolf")!.Value.DeathTimeMs);
    }
}
