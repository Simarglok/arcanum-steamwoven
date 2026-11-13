using Arcanum.Data.Characters;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;
using static Arcanum.Session.Tests.WieldEventScriptTests;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-1684 (ledger IE3-IE6, RM3c; exe read 0x4654F0, 0x465170, 0x464AE0, 0x464C80): the item event scripts of an NPC's best-items wield (the regeneration's RK15 wield and the AI gate's re-wield). Each step runs the removal (wield off 25,
// remove item 28) and the insertion (insert item 21, wield on 24) of the item and of the occupant, the NPC as the triggerer, over the effect list the earlier steps left; a step the occupant's insert check refuses runs both moves. Scripts, numbers
// and entities are invented.
[TestClass]
public sealed class NpcWieldItemScriptTests
{
    private const string Guard = "oid:guard";
    internal const int Charm = 50, Genius = 51, AddCharm = 720, RemoveCharm = 721, AddGenius = 722, Broken = 723, Mark = 724;

    // The charm only touches the charisma (nothing the best-items choice reads); the genius raises the intelligence from 19 to the extraordinary 20.
    private static readonly SessionEffectDefinition CharmEffect = new(Charm, [new(EffectAttributeKind.Stat, 7, EffectChangeOperator.Add, 1)]);
    private static readonly SessionEffectDefinition GeniusEffect = new(Genius, [new(EffectAttributeKind.Stat, 4, EffectChangeOperator.Add, 1)]);

    private static EquipmentItem Armour(ArmorCoverage coverage, int ac) => new()
    {
        Kind = EquipmentItemKind.Armor, Armor = new EquipmentArmor { Coverage = coverage, Sizes = ArmorSizes.Medium, AcAdjustment = ac }, MagickTechComplexity = 40,
    };

    internal sealed class Host : ISessionItemScriptHost
    {
        public Dictionary<(string, int), int> Slots { get; } = [];
        public Dictionary<int, ScrProgram> Programs { get; } = new()
        {
            [AddCharm] = Program(Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(Charm), N(5)))),
            [RemoveCharm] = Program(Do(C(ScrCatalogue.Action.RemoveEffect, (Triggerer, 0), N(Charm)))),
            [AddGenius] = Program(Do(C(ScrCatalogue.Action.AddEffect, (Triggerer, 0), N(Genius), N(5)))),
            // Keeps a count in the item's own slot (a run's change the session applies on any entity).
            [Mark] = Program(Do(C(ScrCatalogue.Action.Assign, (Counter, 0), N(7)))),
            // Adds an effect to the item instead of the wearer: not an effect the session can apply.
            [Broken] = Program(Do(C(ScrCatalogue.Action.AddEffect, (Attachee, 0), N(Charm), N(5)))),
        };
        public Dictionary<int, SessionEffectDefinition> Effects { get; } = new() { [Charm] = CharmEffect, [Genius] = GeniusEffect };

        public ScrSlot? Slot(ScrObjectRef attachee, int point) => Slots.TryGetValue((attachee.Code, point), out var script) ? new ScrSlot(script, 0, 0) : null;
        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => throw new InvalidOperationException("the session reads flags itself");
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, "line", "test");
        public SessionPrintedLine PrintedLineForNpc(int script, int line) => new(SessionPrintedLineStatus.Shown, "npc line", "test");
        public SessionEffectDefinition? EffectDefinition(int number) => Effects.TryGetValue(number, out var rule) ? rule : null;
    }

    private sealed class World : ISessionRestockWorld
    {
        public Host Scripts { get; } = new();
        public bool GivesScripts { get; set; } = true;
        public SessionInsertCheck Insert { get; set; } = _ => SessionInsertVerdict.Accepted;
        // Intelligence 19: one more makes it extraordinary, which the choice reads.
        public int[] Stored { get; } = [.. AttackStateTests.Stats()];
        public SessionLifeTarget Target { get; } = SessionCritterCombat.From(Guard, AttackStateTests.Npc()).Combat!.Target;
        public Dictionary<string, SessionWearableItem?> Wear { get; } = [];

        public World() { Stored[4] = 19; }

        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
        public RestockHolder? RestockFacts(SessionMapId map, string entity) => null;
        public string? RestockMerchant(SessionMapId map, string container) => null;
        public bool Persistent(SessionMapId map, string item) => false;
        public SessionRestockSet RestockSet(int source) => new(SessionRestockSetStatus.Unsupported, null);
        public SessionRestockItem? RestockItem(int basicPrototype) => null;
        public SessionItemKind RestockPileKind => new(SessionItemType.Gold, 9056, 3200, 33200);
        public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => Insert;
        public SessionNpcWielder? RestockWielder(SessionMapId map, string npc) =>
            new(new EquipmentWearer(), new EquipmentChooser { MeleeLevel = 10, BowLevel = 10 }, item => Wear.GetValueOrDefault(item.Entity), (_, _) => true);
        public SessionNpcWieldScripts? NpcWieldScripts(SessionMapId map, string npc) => GivesScripts ? new(Scripts, Target, Stored) : null;

        // The armour's worth is its armour class here (the better one is chosen).
        public void Wearable(string entity, int name, ArmorCoverage coverage, int ac, params int[] points) =>
            Wear[entity] = new(Facts(SessionItemType.Armor, name).Kind, points, false, Armour(coverage, ac)) { Worth = ac * 10, IdentifiedFlag = true };
    }

    // The guard holds the items given (entity, name, slot it wears them in or 0), all its own.
    private static GameSessionOwner Open(params (string Entity, int Name, int Location)[] items)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        var seed = items.Select(item => new SessionSeedItem(item.Entity, Facts(SessionItemType.Armor, item.Name, item.Location), null)).ToArray();
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior,
            new SessionMapInventorySeed([new(Guard, SessionHolderKind.Npc, false, false, [], items.Select(item => item.Entity).ToArray())], seed)).Failure);
        return owner;
    }

    private static int Charms(GameSessionOwner owner, World world) => owner.QueryNpcEffects(world.Target)!.Entries.Count(e => e.Number == Charm && e.Cause == EffectCause.Item);

    private static (EquipmentSlot, string)[] Worn(GameSessionOwner owner) => owner.QueryNpcWorn(Guard)!.Worn.Select(worn => (worn.Slot, worn.Item)).ToArray();

    [TestMethod]
    public void AScriptedHelmetIsWieldedAndItsWieldOnRunsWithTheNpcAsTriggerer()
    {
        using var owner = Open(("helm", 3800, 0));
        var world = new World();
        world.Wearable("helm", 3800, ArmorCoverage.Helmet, 5, SessionScriptPoint.WieldOn);
        world.Scripts.Slots[("helm", SessionScriptPoint.WieldOn)] = AddCharm;
        var result = owner.RewieldNpc(owner.Version, Guard, allSlots: true, world);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Helmet, "helm") }, Worn(owner));
        Assert.AreEqual(1, Charms(owner, world), "wield on added the charm to the NPC's list");
        Assert.AreEqual("helm", result.Steps.Single().Wielded);
    }

    [TestMethod]
    public void WithoutAScriptContextTheScriptedHelmetIsLeftInThePackAsBefore()
    {
        using var owner = Open(("helm", 3800, 0));
        var world = new World { GivesScripts = false };
        world.Wearable("helm", 3800, ArmorCoverage.Helmet, 5, SessionScriptPoint.WieldOn);
        world.Scripts.Slots[("helm", SessionScriptPoint.WieldOn)] = AddCharm;
        var result = owner.RewieldNpc(owner.Version, Guard, allSlots: true, world);
        Assert.AreEqual(GameSessionDisposition.NoChange, result.Disposition);
        var step = result.Steps.Single();
        Assert.AreEqual(("helm", GameSessionFailure.ItemScriptUnsupported), (step.Unsupported, step.Failure));
        Assert.AreEqual(0, Charms(owner, world));
    }

    [TestMethod]
    public void ABetterHelmetTakesTheScriptedOneOffAndItsWieldOffRuns()
    {
        using var owner = Open(("helm", 3800, 0), ("crown", 3801, 0));
        var world = new World();
        world.Wearable("helm", 3800, ArmorCoverage.Helmet, 5, SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff);
        world.Scripts.Slots[("helm", SessionScriptPoint.WieldOn)] = AddCharm;
        world.Scripts.Slots[("helm", SessionScriptPoint.WieldOff)] = RemoveCharm;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RewieldNpc(owner.Version, Guard, allSlots: true, world).Disposition);
        Assert.AreEqual(1, Charms(owner, world));

        // The crown is worth more and has no scripts: the helmet is taken off (wield off removes the charm) and the crown put on.
        world.Wearable("crown", 3801, ArmorCoverage.Helmet, 9);
        var result = owner.RewieldNpc(owner.Version, Guard, allSlots: true, world);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Helmet, "crown") }, Worn(owner));
        Assert.AreEqual(("helm", "crown"), (result.Steps.Single().TakenOff, result.Steps.Single().Wielded));
        Assert.AreEqual(0, Charms(owner, world), "the occupant's wield off ran");
    }

    [TestMethod]
    public void AnOccupantTheInsertCheckRefusesGoesBackAndTheScriptsOfBothMovesHaveRun()
    {
        using var owner = Open(("helm", 3800, 0), ("crown", 3801, 0));
        var world = new World();
        world.Wearable("helm", 3800, ArmorCoverage.Helmet, 5, SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff);
        world.Scripts.Slots[("helm", SessionScriptPoint.WieldOn)] = AddCharm;
        world.Scripts.Slots[("helm", SessionScriptPoint.WieldOff)] = RemoveCharm;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RewieldNpc(owner.Version, Guard, allSlots: true, world).Disposition);
        var version = owner.Version;

        // The pack has no room for the helmet: the crown stays in the pack, the helmet was unwielded (charm gone) and wielded again (charm back): the session changed although no worn item did.
        world.Wearable("crown", 3801, ArmorCoverage.Helmet, 9);
        world.Insert = _ => SessionInsertVerdict.NoRoom;
        var result = owner.RewieldNpc(owner.Version, Guard, allSlots: true, world);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Helmet, "helm") }, Worn(owner));
        Assert.AreEqual(SessionInsertVerdict.NoRoom, result.Steps.Single().Verdict);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition, "the scripts' changes are the session's");
        Assert.AreNotEqual(version, owner.Version);
        Assert.AreEqual(1, Charms(owner, world));
    }

    [TestMethod]
    public void AScriptTheSessionCannotApplyLeavesThatItemInThePackAndTheOthersAreWielded()
    {
        using var owner = Open(("helm", 3800, 0), ("boots", 3802, 0));
        var world = new World();
        world.Wearable("helm", 3800, ArmorCoverage.Helmet, 5, SessionScriptPoint.WieldOn);
        world.Wearable("boots", 3802, ArmorCoverage.Boots, 2);
        world.Scripts.Slots[("helm", SessionScriptPoint.WieldOn)] = Broken;
        var result = owner.RewieldNpc(owner.Version, Guard, allSlots: true, world);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Boots, "boots") }, Worn(owner));
        Assert.AreEqual(("helm", GameSessionFailure.ItemScriptUnsupported), (result.Steps.First().Unsupported, result.Steps.First().Failure));
        Assert.AreEqual(0, Charms(owner, world));
    }

    [TestMethod]
    public void AScriptThatMovesWhatALaterChoiceReadsIsNotRunAndTheItemStaysInThePack()
    {
        // The helmet's wield on makes the NPC's intelligence extraordinary (19 to 20), which the choices of the later slots read: a plan the session cannot make exactly, so the helmet is not wielded (as before the scripts).
        using var owner = Open(("helm", 3800, 0), ("boots", 3802, 0));
        var world = new World();
        world.Wearable("helm", 3800, ArmorCoverage.Helmet, 5, SessionScriptPoint.WieldOn);
        world.Wearable("boots", 3802, ArmorCoverage.Boots, 2);
        world.Scripts.Slots[("helm", SessionScriptPoint.WieldOn)] = AddGenius;
        var result = owner.RewieldNpc(owner.Version, Guard, allSlots: true, world);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Boots, "boots") }, Worn(owner));
        Assert.AreEqual("helm", result.Steps.First().Unsupported);
        Assert.AreEqual(0, owner.QueryNpcEffects(world.Target)!.Entries.Count(e => e.Number == Genius));
    }

    [TestMethod]
    public void AnItemWithNoMoveScriptNeedsNoScriptContext()
    {
        using var owner = Open(("boots", 3802, 0));
        var world = new World { GivesScripts = false };
        world.Wearable("boots", 3802, ArmorCoverage.Boots, 2);
        var result = owner.RewieldNpc(owner.Version, Guard, allSlots: true, world);
        Assert.AreEqual(GameSessionDisposition.Changed, result.Disposition);
        CollectionAssert.AreEqual(new[] { (EquipmentSlot.Boots, "boots") }, Worn(owner));
    }
}
