using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-025: the inventory window's drop and use (ledger IW5, IW6) over the session, with invented values and no original data:
/// the icons' frame, the use's gate and facts, the original's item use over a recording world, and the drop of a taken item.</summary>
[TestClass]
public sealed class AreaItemUseTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    // A generic item (type 14) with the given item flags (slot 36) and generic flags (slot 59), with every slot the combat-field reader needs.
    private static AreaObjectRecord Generic(int index, uint itemFlags, uint genericFlags) => new(Key(index), 0, 14, 15199, "p", "p", "r", "s", AreaScope.Core,
        null, 10, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            new AreaFieldValue(30, AreaPresence.Array, AreaValueOrigin.Instance, null, [], null),
            new AreaFieldValue(31, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null),
            Field(36, itemFlags), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 0), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
            Field(59, genericFlags), Field(60, 0), Field(61, 0),
        ], true, true, true);

    private static readonly SessionItemKind GenericKind = new(SessionItemType.Generic, 15199, 6000, 6001);

    [TestMethod]
    public void TheIconsLieRightOfTheGridAndAReleaseOnThemDropsOrUses()
    {
        Assert.AreEqual(new AreaInventoryHit(null, null, AreaInventoryIcon.Drop), AreaInventoryDrag.HitTest(703, 385 - 41));
        Assert.AreEqual(new AreaInventoryHit(null, null, AreaInventoryIcon.Use), AreaInventoryDrag.HitTest(734.9f, 331 - 41 + 31.9f));
        Assert.AreEqual(AreaInventoryHit.None, AreaInventoryDrag.HitTest(703, 331 - 41 - 1), "above the use icon");
        Assert.AreEqual(AreaInventoryHit.None, AreaInventoryDrag.HitTest(702.9f, 385 - 41));

        var held = new AreaInventoryHeld("crate", null, 21);
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.DropToGround, held), AreaInventoryDrag.Release(held, new(null, null, AreaInventoryIcon.Drop)));
        var worn = new AreaInventoryHeld("ring", EquipmentSlot.Ring1, 0);
        Assert.AreEqual(AreaInventoryDropKind.UseIcon, AreaInventoryDrag.Release(worn, new(null, null, AreaInventoryIcon.Use)).Kind);
    }

    [TestMethod]
    public void TheUseGateFollowsTheRightPressAndTheUseIcon()
    {
        var usable = new AreaItemUseFacts("e", AreaItemUseFacts.FoodType, AreaItemUseFacts.CanUseBoxFlag, 0, false, false);
        var right = AreaItemUseSource.RightClick;
        var icon = AreaItemUseSource.UseIcon;
        Assert.AreEqual(AreaItemUseGate.Use, AreaItemUse.Gate(right, usable, worn: false, inRound: false, heroTurn: false));
        Assert.AreEqual(AreaItemUseGate.Use, AreaItemUse.Gate(right, usable, false, inRound: true, heroTurn: true));
        Assert.AreEqual(AreaItemUseGate.NotHeroTurn, AreaItemUse.Gate(right, usable, false, inRound: true, heroTurn: false));
        Assert.AreEqual(AreaItemUseGate.Wearable, AreaItemUse.Gate(right, usable, worn: true, false, false), "a worn item does nothing");
        Assert.AreEqual(AreaItemUseGate.Wearable, AreaItemUse.Gate(right, usable with { Wearable = true }, false, false, false));
        Assert.AreEqual(AreaItemUseGate.NotUsable, AreaItemUse.Gate(right, usable with { ItemFlags = 0 }, false, false, false), "no OIF_CAN_USE_BOX");
        // The use icon has no wear test; an item whose use is a throw takes the map drop's path after the use check.
        Assert.AreEqual(AreaItemUseGate.Use, AreaItemUse.Gate(icon, usable with { Wearable = true }, false, false, false));
        Assert.AreEqual(AreaItemUseGate.Throw, AreaItemUse.Gate(icon, usable with { ItemFlags = AreaItemUseFacts.CanUseBoxFlag | AreaItemUseFacts.UseIsThrowFlag }, false, false, false));
        Assert.AreEqual(AreaItemUseGate.NotUsable, AreaItemUse.Gate(icon, usable with { ItemFlags = AreaItemUseFacts.UseIsThrowFlag }, false, false, false));
        Assert.AreEqual(AreaItemUseGate.Use, AreaItemUse.Gate(right, usable with { ItemFlags = AreaItemUseFacts.CanUseBoxFlag | AreaItemUseFacts.UseIsThrowFlag }, false, false, false),
            "a right click uses it");

        Assert.AreEqual(AiSchedulerRules.Pc, AreaItemUse.Target(right, shift: false));
        Assert.AreEqual(AiSchedulerRules.Pc, AreaItemUse.Target(icon, shift: true));
        Assert.IsNull(AreaItemUse.Target(icon, shift: false));
    }

    [TestMethod]
    public void TheFactsComeFromTheStoredFieldsAndFeedTheOriginalsUse()
    {
        var fields = CombatFieldReader.Read(Generic(1, AreaItemUseFacts.CanUseBoxFlag | AreaItemUseFacts.NeedsTargetFlag, AreaItemUseFacts.LockpickFlag)).Fields!;
        var facts = AreaItemUseFacts.From("picks", fields)!;
        Assert.AreEqual(("picks", 14, true, true, true, false, false, false), (facts.Entity, facts.Type, facts.CanUseBox, facts.NeedsTarget, facts.Lockpick,
            facts.HealingKit, facts.Wearable, facts.UseScript));
        // A generic item with the torch/shield flag has a wear location (EQ6): a right click on it does nothing.
        var torch = AreaItemUseFacts.From("torch", CombatFieldReader.Read(Generic(2, AreaItemUseFacts.CanUseBoxFlag, AreaItemUseFacts.ShieldLocationFlag)).Fields!)!;
        Assert.IsTrue(torch.Wearable);
        Assert.AreEqual(AreaItemUseGate.Wearable, AreaItemUse.Gate(AreaItemUseSource.RightClick, torch, false, false, false));
        var used = AreaItemUse.Used(facts, null, scrollTooHard: false);
        Assert.AreEqual(new AiUsedItem(AiItemKind.Generic, false, true, false, 0, false, true, false), used);
        var kit = AreaItemUse.Used(facts with { GenericFlags = AreaItemUseFacts.HealingKitFlag | AreaItemUseFacts.TrapDeviceFlag, ItemFlags = AreaItemUseFacts.MagicalFlag },
            new SessionItemSpells("picks", SessionSpellCaster.Hero, 14, [5], 3, 0), false);
        Assert.AreEqual((true, true, false, 3, true), (kit.HealingKit, kit.TrapDevice, kit.Lockpick, kit.Charges, kit.Magical));
        Assert.AreEqual(AiItemKind.Scroll, AreaItemUse.Used(facts with { Type = AreaItemUseFacts.ScrollType }, null, true).Kind);
        Assert.IsTrue(AreaItemUse.Used(facts with { Type = AreaItemUseFacts.ScrollType }, null, true).ScrollTooHard);

        // Lockpicks used from the use icon with no target open the targeting cursor; on the hero (a right click) they find no lock.
        var world = new Recorder(used);
        Assert.AreEqual(AiItemUseStep.Targeting, AiExecutorRules.UseItemNow(world, AiSchedulerRules.Pc, "picks", null).Step);
        Assert.AreEqual("target", string.Join(",", world.Calls));
        world = new Recorder(used) { Round = true };
        var onHero = AiExecutorRules.UseItemNow(world, AiSchedulerRules.Pc, "picks", AiSchedulerRules.Pc);
        Assert.AreEqual((AiItemUseStep.NotLockable, true), (onHero.Step, onHero.Charged));
        Assert.AreEqual("script,consume 4", string.Join(",", world.Calls), "the use script, then 4 action points on the hero's turn");
        // Food without magic is spent; with magic its spell runs first.
        world = new Recorder(new AiUsedItem(AiItemKind.Food));
        Assert.AreEqual(AiItemUseStep.Spent, AiExecutorRules.UseItemNow(world, AiSchedulerRules.Pc, "apple", AiSchedulerRules.Pc).Step);
        Assert.AreEqual("script,spend", string.Join(",", world.Calls));
        world = new Recorder(new AiUsedItem(AiItemKind.Food, Magical: true));
        Assert.AreEqual(AiItemUseStep.Magic, AiExecutorRules.UseItemNow(world, AiSchedulerRules.Pc, "apple", AiSchedulerRules.Pc).Step);
        Assert.AreEqual("script,magic pc,spend", string.Join(",", world.Calls));
    }

    [TestMethod]
    public void TheHostListsUsableItemsAndADropPutsATakenItemOnTheHerosTile()
    {
        using var session = HeroSession();
        var host = new AreaInventoryHost([new AreaInventoryMap(1,
            new([], [
                new SessionSeedItem(Entity(1), new(GenericKind, 1, 0, 0, null, []), new CellCoord(10, 20)),
                new SessionSeedItem(Entity(2), new(GenericKind, 1, 0, 0, null, []), new CellCoord(11, 20)),
            ]),
            new[] { Generic(1, AreaItemUseFacts.CanUseBoxFlag, AreaItemUseFacts.HealingKitFlag), Generic(2, 0x20, 0) }.ToDictionary(row => Entity(row.Key.SourceIndex)))]);
        Assert.AreEqual(GameSessionFailure.None, host.Attach(session));
        Assert.AreEqual(GameSessionDisposition.Changed, host.PickUp(session, Entity(1)).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, host.PickUp(session, Entity(2)).Disposition);
        var view = host.View(session, null, 1);
        Assert.IsTrue(view.Pc.Single(line => line.Entity == Entity(1)).Usable);
        Assert.IsFalse(view.Pc.Single(line => line.Entity == Entity(2)).Usable, "no OIF_CAN_USE_BOX");
        Assert.IsNull(host.UseFacts(session, "kit:1", null), "a kit item's facts come from the hero load");

        // The second item carries the no-drop flag (0x20): its drop is refused; the first lands on the actor's tile.
        Assert.AreEqual(GameSessionFailure.ItemNotDroppable,
            session.DropHeroItem(session.Version, Entity(2), host.GridMoveItem(session, Entity(2), null)!).Failure);
        var dropped = session.DropHeroItem(session.Version, Entity(1), host.GridMoveItem(session, Entity(1), null)!, host.ItemScripts(session));
        Assert.AreEqual((GameSessionDisposition.Changed, Entity(1)), (dropped.Disposition, dropped.Item));
        Assert.AreEqual(session.QueryActor().Cell, session.QueryPlacedItems().Single().Cell);
        Assert.IsFalse(host.View(session, null, 1).Pc.Any(line => line.Entity == Entity(1)));
    }

    private static GameSessionOwner HeroSession()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic", KitWear: []);
        return AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
    }

    /// <summary>Records the item use's calls in order; the hero has the turn when a round runs.</summary>
    private sealed class Recorder(AiUsedItem facts) : IItemUseWorld
    {
        public List<string> Calls { get; } = [];
        public bool Round { get; init; }
        public AiUsedItem UsedItem(string user, string item) => facts;
        public bool RunUseScript(string user, string item, string? target) { Calls.Add("script"); return true; }
        public void OpenTargeting(string user, string item) => Calls.Add("target");
        public void UseTrapDevice(string user, string item, string? target) => Calls.Add("trap");
        public void ReadWritten(string user, string item) => Calls.Add("read");
        public bool Lockable(string target) => false;
        public void StartSkillWithItem(string user, string item, string target, int skill) => Calls.Add($"skill {skill}");
        public void UseItemMagic(string user, string item, string? target) => Calls.Add($"magic {target}");
        public void SpendItem(string item) => Calls.Add("spend");
        public bool InRound => Round;
        public string? RoundActor => Round ? AiSchedulerRules.Pc : null;
        public void Consume(string id, int cost) => Calls.Add($"consume {cost}");
    }
}
