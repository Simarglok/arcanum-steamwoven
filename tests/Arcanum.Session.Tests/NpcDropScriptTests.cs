using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2853 (exe read, ledger PI1, VP6, TQ19; 0x461AB0 → 0x441980): the drop script (point 5) of an item that an NPC drops through a spell's weapon block (SpellWeaponDrop, the
// step an attack's critical and arm hit use too): the host's DropScript runs it with the holder as the triggerer and the item as the attachee after the distance is drawn and before the
// no-drop check; skip default refuses the drop and the item stays. Values are invented.
[TestClass]
public sealed class NpcDropScriptTests
{
    private const string Golem = "golem", Sword = "golem-sword";
    private static readonly CellCoord GolemCell = new(101, 200);
    private static readonly ItemCondition Sturdy = new(1000, 0, 0, false);

    private static SessionCritterCombat GolemCombat()
    {
        var fields = AttackStateTests.Npc(material: 0, hpAdjustment: 5000, critterFlags: 0);
        return SessionCritterCombat.From(Golem, fields with { Critter = fields.Critter! with { CriticalHitChart = 0 } }).Combat!;
    }

    private static GameSessionOwner Owner(params int[] swordScripts)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var seed = new SessionMapInventorySeed(
            [new(Golem, SessionHolderKind.Npc, false, false, [], [Sword])],
            [new SessionSeedItem(Sword, InventoryStateTests.Facts(SessionItemType.Weapon, 2901, (int)EquipmentSlot.Weapon, 0, null, swordScripts), null)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    private static Dictionary<EquipmentSlot, SessionWearItem> Worn(bool noDrop = false) => new() { [EquipmentSlot.Weapon] = new(Sword, Sturdy) { NoDrop = noDrop } };

    private static bool InList(GameSessionOwner owner) => owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == Golem).Items.Any(i => i.Entity == Sword);

    private static DangerFlags LookFor(GameSessionOwner owner) => owner.QueryDanger(Golem).Flags & (DangerFlags.LookForWeapon | DangerFlags.LookForArmor);

    [TestMethod]
    public void AnItemWithADropScriptStaysUnappliedWhenNoHostRunsIt()
    {
        using var owner = Owner(5);
        var steps = owner.SpellWeaponDrop(GolemCombat().Target, Worn(), GolemCell, null, out var failure);
        Assert.AreEqual(GameSessionFailure.None, failure);
        Assert.AreEqual(SessionCriticalStepOutcome.ScriptUnsupported, steps.Single().Outcome);
        Assert.IsTrue(InList(owner));
        Assert.AreEqual(DangerFlags.None, LookFor(owner));
    }

    [TestMethod]
    public void RunDefaultOfTheDropScriptLetsTheNpcDropItWithTheOriginalsRoles()
    {
        using var owner = Owner(5);
        var asked = new List<SessionDropScriptRequest>();
        owner.DropScript = request => { asked.Add(request); return SessionDropScriptAnswer.RunDefault; };
        var steps = owner.SpellWeaponDrop(GolemCombat().Target, Worn(), GolemCell, null, out var failure);
        Assert.AreEqual(GameSessionFailure.None, failure);
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, steps.Single().Outcome);
        Assert.IsFalse(InList(owner));
        Assert.AreEqual(DangerFlags.LookForWeapon, LookFor(owner), "the drop's tail still runs");
        var request = asked.Single();
        Assert.AreEqual((Sword, Golem, Exterior.RuntimeMap, Exterior.RuntimeMap), (request.Item, request.Holder, request.ItemMap, request.Map));
    }

    [TestMethod]
    public void SkipDefaultOfTheDropScriptRefusesTheDropAndTheItemStays()
    {
        using var owner = Owner(5);
        owner.DropScript = _ => SessionDropScriptAnswer.SkipDefault;
        var steps = owner.SpellWeaponDrop(GolemCombat().Target, Worn(), GolemCell, null, out var failure);
        Assert.AreEqual(GameSessionFailure.None, failure);
        Assert.AreEqual(SessionCriticalStepOutcome.Refused, steps.Single().Outcome);
        Assert.IsTrue(InList(owner));
        Assert.AreEqual(DangerFlags.None, LookFor(owner), "a refused drop sets no look-for bit");
    }

    [TestMethod]
    public void ADropScriptTheHostCannotApplyLeavesTheDropUnapplied()
    {
        foreach (var host in new Func<SessionDropScriptRequest, SessionDropScriptAnswer>[]
        {
            _ => SessionDropScriptAnswer.NotModelled, _ => throw new InvalidOperationException("a host refusal"),
        })
        {
            using var owner = Owner(5);
            owner.DropScript = host;
            var steps = owner.SpellWeaponDrop(GolemCombat().Target, Worn(), GolemCell, null, out var failure);
            Assert.AreEqual(GameSessionFailure.None, failure);
            Assert.AreEqual(SessionCriticalStepOutcome.ScriptUnsupported, steps.Single().Outcome);
            Assert.IsTrue(InList(owner));
        }
    }

    [TestMethod]
    public void AnItemWithoutADropScriptAsksNoHostAndANoDropItemIsStillRefusedAfterTheScript()
    {
        using var plain = Owner();
        plain.DropScript = _ => throw new AssertFailedException("no drop script, nothing to ask");
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, plain.SpellWeaponDrop(GolemCombat().Target, Worn(), GolemCell, null, out _).Single().Outcome);
        // The script is the gate before the no-drop check: a script that runs default still meets the flag.
        using var owner = Owner(5);
        var asked = 0;
        owner.DropScript = _ => { asked++; return SessionDropScriptAnswer.RunDefault; };
        Assert.AreEqual(SessionCriticalStepOutcome.Refused, owner.SpellWeaponDrop(GolemCombat().Target, Worn(noDrop: true), GolemCell, null, out _).Single().Outcome);
        Assert.AreEqual(1, asked);
        Assert.IsTrue(InList(owner));
    }

    [TestMethod]
    public void AScriptThatWritesTheSessionDoesNotStaleTheDrop()
    {
        using var owner = Owner(5);
        owner.DropScript = _ =>
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity("somebody", "hp"), 7).Disposition);
            return SessionDropScriptAnswer.RunDefault;
        };
        var steps = owner.SpellWeaponDrop(GolemCombat().Target, Worn(), GolemCell, null, out var failure);
        Assert.AreEqual(GameSessionFailure.None, failure);
        Assert.AreEqual(SessionCriticalStepOutcome.Dropped, steps.Single().Outcome);
        Assert.IsFalse(InList(owner));
    }
}
