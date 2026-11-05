using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-108 (NPC AI slice I10, part 3e, the NPCs' items): on the installed original (opt-in, <c>ARC_AREA_CONVERSATION_GAME_ROOT</c>
/// and the Crash Site exterior's mobile folder in <c>ARC_AREA_MOBILE</c>) the exterior's college NPC is given an installed healing
/// potion (a food item whose slot-0 spell is on the light-healing list) and hurt; its AI pass's heal step lists the potion, chooses it
/// and uses it through the map's cast host: the potion leaves its list. Numbers only; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class AreaAiItemTests
{
    [TestMethod, TestCategory("RealData")]
    [DataRow(RuleMode.Steamwoven), DataRow(RuleMode.Original)]
    public void On_the_crash_site_a_hurt_npc_given_a_healing_potion_drinks_it_in_its_heal_step(RuleMode mode)
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        var mobile = Environment.GetEnvironmentVariable("ARC_AREA_MOBILE");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(mobile))
            Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT and ARC_AREA_MOBILE are required.");
        string P(string relative) => Path.Combine(root, relative);
        var module = P("modules/Arcanum.dat");
        using var data = AreaOriginalValidator.OpenFiles(module, P("arcanum1.dat"), P("arcanum2.dat"), mobile, P("data/proto"));
        var snapshot = data.LoadStrict();
        var steps = AreaStepInspector.ValidateOriginalData(snapshot, module, P("arcanum2.dat"), P("arcanum3.dat"), P("modules/Arcanum.PATCH0"), mobile, 1);
        var inventory = AreaInventoryMap.Build(snapshot, steps).Map!;
        var barter = AreaBarterData.Load(module).Data ?? throw new AssertFailedException("item prototypes");
        var combat = new AreaCombatHost(new AreaInventoryHost([inventory]), barter.Prototype);
        var table = AreaCompanionData.Open(module).Data?.Table ?? throw new AssertFailedException("AI parameter table");
        var spellData = AreaSpellsPage.LoadData(module).Data ?? throw new AssertFailedException("spell data");
        var records = Enumerable.Range(0, SpellRecord.Count).Select(n => SessionSpellRecords.Translate(spellData.Records[n])).ToArray();
        var map = snapshot.RuntimeMap;

        // The college NPC of the AI cast tests: its own spells are on no healing list, so its healing lists hold only items.
        var npcRow = snapshot.Objects.Where(o => o.Type == 16 && o.WorldX is not null && combat.Entity(map, o.Key) is { } e
                && combat.Build(map, e).Combat is { } c && !LifeRules.IsDead(c.Loaded, c.Facts)
                && combat.Fields(map, e)?.Critter?.SpellTech.Take(SpellBookRules.Colleges).Any(rank => rank > 0) == true)
            .OrderByDescending(o => combat.Build(map, combat.Entity(map, o.Key)!).Combat!.Level).First();
        var npc = combat.Entity(map, npcRow.Key)!;
        var hero = new AreaStepCell(npcRow.WorldX!.Value + 12, npcRow.WorldY!.Value);
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(), hero, map,
            AreaStartingHero.Load(module, AreaStartingHero.Premade[0])).Owner!;
        using var _ = session;
        session.SelectRules(mode);
        Assert.AreEqual(GameSessionFailure.None, session.AttachMapInventory(new SessionMapId(map), inventory.Seed).Failure);

        AreaAiHost? host = null;
        var heroCell = new CellCoord(checked((int)hero.X), checked((int)hero.Y));
        var lineOfFire = () => new AreaLineOfFire(steps, snapshot.Objects, host?.Movers.Moved.ToHashSet() ?? [], new Dictionary<CellCoord, AreaObjectRecord>(),
            _ => false, row => combat.Entity(map, row.Key) is { } e && session.QueryNpcKilled(e), _ => null);
        var spellMap = new AreaSpellCastMap(session, map, steps, snapshot.Objects, combat, () => heroCell, lineOfFire,
            placed: () => host?.Movers.All.ToDictionary(m => m.Key, m => ((long X, long Y)?)(m.Cell.X, m.Cell.Y)) ?? []);
        var npcs = snapshot.Objects.Where(o => o.Type == 16 && o.WorldX is not null && combat.Entity(map, o.Key) is not null)
            .Select(o => new AreaAiNpc(combat.Entity(map, o.Key)!, o, new(checked((int)o.WorldX!.Value), checked((int)o.WorldY!.Value)))).ToArray();
        var round = new AreaCombatWorld(map,
            () => AreaCombatScan.Critters(npcs.Select(n => (n.Entity, n.Row.Key)), heroCell, e => host!.Tile(e), new AreaSectorEntries()),
            e => combat.Build(map, e).Combat is { } c && combat.Fields(map, e) is { } fields
                ? AreaCombatWorld.Facts(c, fields, session.QueryLife(c.Target).View?.Record.State ?? c.Loaded) : null);
        var events = new AreaEventWorld(() => combat, () => null, ai: () => host, round: () => round);
        var caster = new SessionSpellCaster(session, n => n is >= 0 and < SpellRecord.Count ? records[n] : null, spellMap, events,
            id => combat.NpcItemSpells(session, map, id));
        caster.ItemTriggers = new SessionItemTriggers(caster, round);

        // An installed healing potion: food whose slot-0 spell is on the light-healing list.
        var light = caster.AiLists[AiActionList.HealingLight];
        var potion = Enumerable.Range(6029 + 20, 15213 - 6029).Select(n => (Number: n, Fields: barter.Prototype(n)))
            .FirstOrDefault(p => p.Fields is { Object.Type: SessionItemSpells.FoodType, Item: { } item } && item.SpellManaStore != 0 && light.Contains(item.Spells[0]));
        Assert.IsNotNull(potion.Fields, "an installed healing potion");
        var kind = new SessionItemKind(SessionItemType.Food, potion.Number, potion.Fields.Object.Name, potion.Fields.Object.Description);
        var given = session.AddItemToNpc(session.Version, npc, kind, _ => SessionInsertVerdict.Accepted);
        Assert.AreEqual(GameSessionFailure.None, given.Failure);
        var item = given.Item!;

        // Hurt to 60 %: below the light rung's 70 % (and 90 % out of danger), above the medium rung's 55 %.
        var critter = combat.Build(session, map, npc).Combat!;
        var max = session.QueryLife(critter.Target).View?.Status.MaxHitPoints ?? LifeRules.Evaluate(critter.Loaded, critter.Facts).MaxHitPoints;
        Assert.AreEqual(GameSessionDisposition.Changed,
            session.ApplyDamage(session.Version, critter.Target, new LifeDamage { HitPoints = max - max * 6 / 10 }).Disposition);
        var hurt = session.QueryLife(critter.Target).View!.Status.HitPoints;

        var scene = new AreaAiScene
        {
            RuntimeMap = map, Chart = AreaChart.For(snapshot), Steps = steps, Npcs = npcs, Combat = combat, HeroCell = () => heroCell, Parameters = table,
            LineOfFire = lineOfFire, Spells = () => caster,
        };
        host = new AreaAiHost(session, scene, events, round);
        CollectionAssert.Contains(host.Inventory(npc).ToArray(), item, "the NPC's list holds the given potion");
        Assert.IsTrue(host.Item(item) is { ManaStore: not 0, IdentificationExempt: true }, "a food item with spell charges");
        Assert.IsTrue(host.List(npc, AiActionList.HealingLight).Any(entry => entry.Item == item), "the potion is on its light-healing list");
        Assert.AreEqual(0, host.ScheduleTicks());

        var pump = new FrameStep(session, events, spells: () => caster).WithAi(() => host);
        var notices = new List<AreaAiNotice>();
        for (var frame = 0; frame < 4000 && !session.IsItemDestroyed(item); frame++)
        {
            pump.AdvanceGame(8 * 50);
            notices.AddRange(host.DrainNotices().Where(n => n.Npc == npc));
        }
        var trace = host.TraceOf(npc);
        var healed = session.QueryLife(critter.Target).View!.Status.HitPoints;
        Trace($"{mode} potion {potion.Number} spell {potion.Fields.Item!.Spells[0]} hp {hurt}->{healed}/{max}; item {trace?.Item}; perform {trace?.Perform};"
            + $" step {trace?.Step}; cast {trace?.Cast}; log {string.Join(",", caster.Log.Select(e => $"{e.Kind}:{e.Value}"))}");
        Assert.IsTrue(session.IsItemDestroyed(item), "the NPC used the potion up");
        CollectionAssert.DoesNotContain(host.Inventory(npc).ToArray(), item);
        // The potion's slot-0 record fires on Item_Used, so the chooser's choice is a use (pass action 2, goal 39), not a cast.
        var use = notices.SingleOrDefault(n => n.Kind == AreaAiNoticeKind.ItemUse && n.Item == item) ?? throw new AssertFailedException("no item use");
        Assert.AreEqual(npc, use.Target, "the heal step aimed the potion at the NPC itself");
        Assert.AreEqual((SessionItemUseOutcome.Used, SessionItemUseCast.Triggered, true), (use.ItemUse!.Outcome, use.ItemUse.Cast, use.ItemUse.Spent));
        Assert.AreEqual(SessionItemSpellOutcome.Started, use.ItemUse.ItemUsed!.Spells.Single().Outcome, "its Item_Used spell ran");
        Assert.IsTrue(healed > hurt, "the potion healed the NPC");
        // The Original setting's list cache (SP11) froze the light-healing list with the potion; its destruction drops the slot
        // (0x467860 → 0x4CCC00), so the list holds it no more.
        Assert.IsFalse(host.List(npc, AiActionList.HealingLight).Any(entry => entry.Item == item), $"{mode}: the spent potion is listed no more");
    }

    private static void Trace(string line) => Console.WriteLine("ARC-WORLD-108 " + line);
}
