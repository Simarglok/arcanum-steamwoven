using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1590 (item use 0x462CC0 → the use script 0x441980, ledger IU2, VT8): the AI host's item use of a food an NPC holds over invented rows, no original data. The scene's <see cref="AreaAiScene.ItemUseScript"/> runs the use script with the NPC as the user and the use's target as
/// the extra object; a script that skips the default ends the use (the action points are still charged), one that ran default lets the food be spent, and a scene without a runner goes on as before, which the trace notes.
/// </summary>
[TestClass]
public sealed class AreaAiItemUseScriptTests
{
    private static readonly SessionItemKind FoodKind = new(SessionItemType.Food, 4060, 60, 160);
    private static readonly CellCoord HeroCell = new(14, 20), NpcCell = new(10, 20);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaFieldValue Array(int slot, params long[] values) =>
        new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
    private static AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);

    // A human NPC (type 16) with every primary stat 20, level 1, 20 hit points, at world cell (10, 20).
    private static AreaObjectRecord Npc(int index) => new(Key(index), 0, 16, 28359, "p", "p", "r", "s", AreaScope.Core, null, NpcCell.X, NpcCell.Y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 20), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), Empty(31),
            Field(36, 0), Field(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Field(42, 10), Field(43, 0), Field(44, 0), Field(45, 0), Empty(46), Empty(47),
            Field(57, 0), Field(63, 0), Field(69, 0), Field(71, 0), Field(74, 10), Field(75, 10), Field(81, 0), Field(82, 0), Field(84, 0), Field(85, 0), Field(89, 0),
            Array(92),
        ], true, true, true);

    // A food (type 9) in the NPC's list: no item flags, no spell charges.
    private static AreaObjectRecord Food(int index, bool withScript) => new(Key(index), 0, 9, 4060, "p", "p", "r", "s",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Field(0, 0x60000000), Field(18, 0), Field(19, 0), Field(21, 1), Field(22, 2), Field(25, 0), Field(26, 10), Field(27, 0), Field(28, 0), Field(29, 4),
            Array(30), withScript ? new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(1, [0, 0, 30282])], null) : Empty(31),
            Field(36, 0), Field(38, 1), Field(39, 0), Field(40, 1), Field(41, 0), Field(42, 0x60000000), Field(43, 0), Field(44, 0x60000000), Field(45, 0),
            Field(46, 0), Field(49, 10000), Field(50, 10000), Field(51, 10000), Field(52, 10000), Field(53, 10000), Field(54, 0), Field(55, 0),
        ], true, true, true);

    private static (AreaAiHost Host, string Npc, string Food) Open(Func<string, string, string?, bool>? script, bool withScript = true)
    {
        var identity = new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid()));
        var hero = new SessionHeroDefinition(16089, CharacterRace.Human, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock, AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), null),
            new(new(1), HeroCell, NeighborDirection.XPlus), Hero: hero) { RandomSeed = 1 });
        var session = created.Owner ?? throw new AssertFailedException($"session {created.Failure}");
        var map = new AreaInventoryMap(1,
            new([new SessionSeedHolder(Entity(0), SessionHolderKind.Npc, false, false, [], [Entity(1)])],
                [new SessionSeedItem(Entity(1), new(FoodKind, 1, 0, 0, null, withScript ? [SessionScriptPoint.Use] : []), null)]),
            new[] { Npc(0), Food(1, withScript) }.ToDictionary(row => Entity(row.Key.SourceIndex)));
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var combat = new AreaCombatHost(inventory, session: () => session);
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        for (var x = 0; x < 32; x++) for (var y = 0; y < 32; y++)
            terrain[new(x, y)] = new(new(x, y), 0, CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open, AreaStepTerrainSource.TileName, "TEST");
        var steps = new AreaStepSnapshot(new(AreaStepInputKind.Structural, "arc-data-019-whole-map-original-rules-v1", [], "d"), 1, [(0, 0)], terrain,
            new Dictionary<AreaStepCell, AreaStepObjectEvaluation[]>(), new Dictionary<AreaStepCell, AreaStepWallEdge[]>());
        var scene = new AreaAiScene
        {
            RuntimeMap = 1, Chart = new AreaChart(0, 0, new(0, 0), "TEST"), Steps = steps, Npcs = [new(Entity(0), Npc(0), NpcCell)], Combat = combat, HeroCell = () => HeroCell,
            ItemUseScript = script,
        };
        var host = new AreaAiHost(session, scene, new AreaEventWorld(() => combat, () => null), new AreaCombatWorld(1, () => [], _ => null));
        return new(host, Entity(0), Entity(1));
    }

    [TestMethod]
    public void A_use_script_that_skips_the_default_ends_the_use_with_the_npc_as_user_and_the_target_it_was_given()
    {
        var calls = new List<(string User, string Item, string? Target)>();
        var (host, npc, food) = Open((user, item, target) => { calls.Add((user, item, target)); return false; });
        var used = AiExecutorRules.UseItemNow(host, npc, food, AiSchedulerRules.Pc);
        Assert.AreEqual(AiItemUseStep.ScriptStopped, used.Step);
        Assert.IsFalse(used.Spent, "the script destroys the food itself; the use spends none");
        CollectionAssert.AreEqual(new[] { (npc, food, (string?)AiSchedulerRules.Pc) }, calls);
        StringAssert.Contains(host.TraceOf(npc)?.Item, "use-script");
        StringAssert.Contains(host.TraceOf(npc)?.Item, "skipped");
    }

    [TestMethod]
    public void A_use_script_that_ran_default_lets_the_food_be_used_as_before()
    {
        var calls = 0;
        var (host, npc, food) = Open((_, _, _) => { calls++; return true; });
        var used = AiExecutorRules.UseItemNow(host, npc, food, null);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(AiItemUseStep.Spent, used.Step, "no script stop: the food is spent");
    }

    [TestMethod]
    public void Without_a_runner_or_without_a_use_script_the_use_goes_on_and_the_trace_says_which()
    {
        var (bare, npc, food) = Open(null);
        Assert.AreEqual(AiItemUseStep.Spent, AiExecutorRules.UseItemNow(bare, npc, food, null).Step);
        StringAssert.Contains(bare.TraceOf(npc)?.Item, "use-script");
        StringAssert.Contains(bare.TraceOf(npc)?.Item, "not run");
        var ran = false;
        var (plain, plainNpc, plainFood) = Open((_, _, _) => { ran = true; return false; }, withScript: false);
        Assert.AreEqual(AiItemUseStep.Spent, AiExecutorRules.UseItemNow(plain, plainNpc, plainFood, null).Step);
        Assert.IsFalse(ran, "an item without a use script runs none");
    }
}
