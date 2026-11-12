using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-MAGIC-1146 (ledger FW1–FW3; exe read 0x45ED70, 0x45ECE0): the Combat-Focus Wipe event queued at an NPC's death. Ten game minutes on it clears the dead NPC's focus once the focus is 30 or more tiles away (the larger of the two
/// coordinate differences) and queues itself again while it is nearer; a revived critter or one with no focus ends it. Also the cancel of the body decay (0x45EC80) a revival makes. Invented numbers only.
/// </summary>
[TestClass]
public sealed class FocusWipeStateTests
{
    private const int Prototype = 28359;
    private const string Orc = "oid:orc";
    private static readonly CellCoord Body = new(101, 202);
    private static readonly LifeFacts Plain = new() { HitPointPoints = 10, FatiguePoints = 10 };

    private sealed class World(Dictionary<string, CellCoord> cells) : ISessionBodyWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => Plain;
        public CellCoord? BodyCell(SessionMapId map, string entity) => cells.TryGetValue(entity, out var cell) ? cell : null;
    }

    private static SessionLifeTarget Target(string entity) => SessionLifeTarget.Critter(entity, Plain, LifeState.Unharmed);

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static string Critter(GameSessionOwner owner) => owner.CreateObject(owner.Version, SessionCreatedKind.Critter, Prototype, Exterior, Body).Object!.Entity;

    private static World Placed(GameSessionOwner owner, string critter, CellCoord? orc = null)
    {
        var cells = new Dictionary<string, CellCoord> { [critter] = Body };
        if (orc is { } at) cells[Orc] = at;
        return new World(cells);
    }

    [TestMethod]
    public void TheDeathOfAnNpcQueuesTheWipeTenGameMinutesOn()
    {
        using var owner = Create();
        var critter = Critter(owner);
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Target(critter), SessionSpellCaster.Hero).Disposition);
        CollectionAssert.AreEqual(new[] { (critter, Exterior, now + 600_000) }, owner.QueryFocusWipes().ToArray());
        Assert.AreEqual(SessionCombatant.Hero, owner.QueryDanger(critter).Focus, "the death wrote the killer as the focus");
        // A critter that does not die queues none, and the hero's death queues none.
        Assert.AreEqual(1, owner.QueryFocusWipes().Count);
    }

    [TestMethod]
    public void AKillerThirtyOrMoreTilesAwayIsForgottenAndANearerOneIsWatchedAgain()
    {
        using var owner = Create();
        var critter = Critter(owner);
        owner.Kill(owner.Version, Target(critter), SessionSpellCaster.Hero);
        var hero = owner.QueryActor().Cell;
        // Not due a millisecond early.
        Advance(owner, 599_999);
        owner.ProcessEvents(Placed(owner, critter));
        Assert.AreEqual(SessionCombatant.Hero, owner.QueryDanger(critter).Focus);
        // The hero is the focus; put the body 29 tiles from it (the larger coordinate difference): the event queues itself again.
        var near = new CellCoord(hero.X + 29, hero.Y + 5);
        Advance(owner, 1);
        var nowNear = owner.QueryClockSnapshot().GameTimestampMs;
        owner.ProcessEvents(new World(new() { [critter] = near }));
        Assert.AreEqual(SessionCombatant.Hero, owner.QueryDanger(critter).Focus, "29 tiles away: the focus stays");
        CollectionAssert.AreEqual(new[] { (critter, Exterior, nowNear + 600_000) }, owner.QueryFocusWipes().ToArray(), "and the event waits another ten minutes");
        // 30 tiles away: the focus is cleared and the event is gone.
        Advance(owner, 600_000);
        var far = new CellCoord(hero.X, hero.Y - 30);
        var version = owner.Version;
        owner.ProcessEvents(new World(new() { [critter] = far }));
        Assert.IsNull(owner.QueryDanger(critter).Focus);
        Assert.AreEqual(0, owner.QueryFocusWipes().Count);
        Assert.IsTrue(owner.Version.Revision > version.Revision);
    }

    [TestMethod]
    public void ACritterKillerIsMeasuredFromItsCellAndAKillerTheWorldCannotPlaceIsForgotten()
    {
        using var owner = Create();
        var critter = Critter(owner);
        owner.Kill(owner.Version, Target(critter), Orc);
        Assert.AreEqual(SessionCombatant.Critter(Orc), owner.QueryDanger(critter).Focus);
        Advance(owner, 600_000);
        owner.ProcessEvents(Placed(owner, critter, orc: new CellCoord(Body.X + 5, Body.Y - 29)));
        Assert.AreEqual(SessionCombatant.Critter(Orc), owner.QueryDanger(critter).Focus, "29 tiles: still watched");
        Advance(owner, 600_000);
        owner.ProcessEvents(Placed(owner, critter));
        Assert.IsNull(owner.QueryDanger(critter).Focus, "the orc is on no cell of this map: the reference is stale and goes");
    }

    [TestMethod]
    public void ARevivedCritterAndOneWithNoFocusEndTheEvent()
    {
        using var owner = Create();
        var critter = Critter(owner);
        owner.Kill(owner.Version, Target(critter));
        Assert.IsNull(owner.QueryDanger(critter).Focus, "no killer: nothing to forget");
        Advance(owner, 600_000);
        owner.ProcessEvents(Placed(owner, critter));
        Assert.AreEqual(0, owner.QueryFocusWipes().Count, "consumed without a new one");
        // A second NPC is healed back to life before the event: it finds a living critter and does nothing.
        var second = Critter(owner);
        owner.Kill(owner.Version, Target(second), SessionSpellCaster.Hero);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Heal(owner.Version, Target(second), new LifeHeal { Resurrect = true, Full = true }, () => true).Disposition);
        Advance(owner, 600_000);
        owner.ProcessEvents(new World(new() { [second] = new CellCoord(5000, 5000) }));
        Assert.AreEqual(SessionCombatant.Hero, owner.QueryDanger(second).Focus, "a living critter keeps its focus");
        Assert.AreEqual(0, owner.QueryFocusWipes().Count);
    }

    [TestMethod]
    public void TheEventIsSavedRestoredAndValidated()
    {
        using var owner = Create();
        var critter = Critter(owner);
        owner.Kill(owner.Version, Target(critter), SessionSpellCaster.Hero);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"kind\":\"combat.focus.wipe\"");
        using var back = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryFocusWipes().ToArray(), back.QueryFocusWipes().ToArray());
        Advance(back, 600_000);
        back.ProcessEvents(new World(new() { [critter] = new CellCoord(back.QueryActor().Cell.X + 40, back.QueryActor().Cell.Y) }));
        Assert.IsNull(back.QueryDanger(critter).Focus, "the restored session fires it the same");
        // A saved event naming a critter with no life record, or with another delay, is refused.
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        var ghost = text.Replace("\"entity\":\"" + critter + "\"}],\"values\":[" + now + ",600000]", "\"entity\":\"oid:ghost:1\"}],\"values\":[" + now + ",600000]");
        Assert.AreNotEqual(text, ghost);
        Assert.AreEqual(GameSessionFailure.InvalidEvents, GameSessionOwner.Restore(Decode(ghost), RestoreOptions()).Failure);
        var delay = text.Replace("\"values\":[" + now + ",600000]", "\"values\":[" + now + ",600001]");
        Assert.AreNotEqual(text, delay);
        Assert.AreNotEqual(GameSessionFailure.None, GameSessionOwner.Restore(Decode(delay), RestoreOptions()).Failure);
    }

    [TestMethod]
    public void ARevivalCancelsTheBodyDecayEvent()
    {
        using var owner = Create();
        var critter = Critter(owner);
        owner.Kill(owner.Version, Target(critter));
        Assert.AreEqual(1, owner.QueryBodyDecays().Count);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Heal(owner.Version, Target(critter), new LifeHeal { Resurrect = true, Full = true }, () => true).Disposition);
        Assert.AreEqual(0, owner.QueryBodyDecays().Count, "0x45EC80: a body healed back to life no longer decays");
        // A refused raise (the script's veto) leaves the dead body's event.
        owner.Kill(owner.Version, Target(critter));
        Assert.AreEqual(1, owner.QueryBodyDecays().Count);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.Heal(owner.Version, Target(critter), new LifeHeal { Resurrect = true, Full = true }, () => false).Disposition);
        Assert.AreEqual(1, owner.QueryBodyDecays().Count);
    }
}
