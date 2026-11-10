using Arcanum.Scripting.GlobalFlags;
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
}
