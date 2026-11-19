using Arcanum.Data.Scripts;
using Arcanum.Simulation.Movement;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.RemoveItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2881 (follow-up of ARC-SCRIPT-2854, exe 0x467860): a food or scroll the cast host spends (SessionSpellCaster.SpendUsedItem, the spell path's destruction 0x43CCA0) runs the item's remove-item script (28)
// through the caster's item script host, with the holder as the triggerer; the lines it printed are logged for the message window. Over the synthetic seed of InventoryStateTests (the corpse holds torn, an item with a
// remove-item point, here a scroll to the cast host; a hero scroll "relic-scroll" with the same point) and RemoveItemScriptTests' host; script numbers and programs are invented.
[TestClass]
public sealed class SpellSpentItemScriptTests
{
    private sealed class Map : ISpellCastMap
    {
        public SpellTargetObject? Object(string id) => null;
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => [];
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => [];
        public bool TileBlocked(SpellLocation tile) => false;
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => true;
        public int SectorKind(SpellLocation location) => 0;
        public int Direction(SpellLocation from, SpellLocation to) => 0;
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => [];
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => new(0, null);
        public SpellCastCritter? Critter(string entity) => null;
        public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => [];
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;
        public bool DamageObject(string id, AttackDamage damage) => false;
    }

    private sealed class Events : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    // A cast host whose items are the scroll "torn" the corpse holds and the scroll "relic-scroll" the hero holds (the lookup names holder and kind; the session's lists decide what the deletion does).
    private static SessionSpellCaster CasterOf(GameSessionOwner owner, ISessionItemScriptHost? host, int type = SessionItemSpells.ScrollType)
    {
        var caster = new SessionSpellCaster(owner, _ => null, new Map(), new Events(), id => id switch
        {
            "torn" => new SessionItemSpells(id, "corpse", type, [10000, 10000, 10000, 10000, 10000], 1, 0),
            "relic-scroll" => new SessionItemSpells(id, SessionSpellCaster.Hero, SessionItemSpells.ScrollType, [10000, 10000, 10000, 10000, 10000], 1, 0),
            _ => null,
        });
        caster.ItemScripts = () => host;
        return caster;
    }

    private static GameSessionOwner HeroWithScroll()
    {
        var seed = Seed();
        seed = seed with { Items = [.. seed.Items, new("relic-scroll", Facts(SessionItemType.Scroll, 5800, scripts: SessionScriptPoint.RemoveItem), new CellCoord(112, 200))] };
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual("relic-scroll", owner.PickUpItem(owner.Version, "relic-scroll", Accept).Item);
        return owner;
    }

    private static Host ScrollHost(GameSessionOwner owner, params (int Type, (byte Kind, int Value)[] Operands)[] actions)
    {
        var host = new Host(owner);
        host.Slots[("relic-scroll", SessionScriptPoint.RemoveItem)] = RemoveScript;
        host.Programs[RemoveScript] = Program(actions);
        return host;
    }

    [TestMethod]
    public void A_scroll_an_npc_spends_runs_its_remove_item_script_with_the_npc_as_the_triggerer_and_logs_the_lines()
    {
        using var owner = Hero();
        var host = Hosted(owner, C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 4), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault));
        var caster = CasterOf(owner, host);
        Assert.IsTrue(caster.SpendUsedItem("torn"));
        Assert.IsTrue(owner.IsItemDestroyed("torn"));
        Assert.AreEqual(true, ReadFlag(owner, 3), "the run's flag write applied with the deletion");
        var line = caster.Log.Single(e => e.Kind == SpellCastEventKind.ItemScriptLine);
        Assert.AreEqual(($"npc line 4 of {RemoveScript}", RemoveScript, "corpse", 0), (line.Text, line.Detail, line.Object, line.Value), "an NPC triggerer reads its own line variant");
    }

    [TestMethod]
    public void A_food_an_npc_spends_runs_its_remove_item_script_as_a_scroll_does()
    {
        using var owner = Hero();
        var host = Hosted(owner, C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        Assert.IsTrue(CasterOf(owner, host, SessionItemSpells.FoodType).SpendUsedItem("torn"));
        Assert.AreEqual((true, true), (owner.IsItemDestroyed("torn"), ReadFlag(owner, 3)));
    }

    [TestMethod]
    public void Without_an_item_script_host_a_spent_scroll_with_a_remove_item_script_is_refused_not_deleted_with_it_skipped()
    {
        using var owner = Hero();
        var caster = CasterOf(owner, null);
        var revision = owner.Version.Revision;
        Assert.IsFalse(caster.SpendUsedItem("torn"));
        Assert.AreEqual((false, revision, 0), (owner.IsItemDestroyed("torn"), owner.Version.Revision, caster.Log.Count));
    }

    [TestMethod]
    public void A_remove_item_run_the_session_cannot_apply_leaves_the_spent_scroll_where_it_is()
    {
        using var owner = Hero();
        var bad = Hosted(owner, C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        var caster = CasterOf(owner, bad);
        Assert.IsFalse(caster.SpendUsedItem("torn"));
        Assert.IsFalse(owner.IsItemDestroyed("torn"));
        Assert.AreEqual(0, caster.Log.Count);
    }

    [TestMethod]
    public void A_scroll_the_hero_spends_runs_its_remove_item_script_with_the_hero_as_the_triggerer_and_logs_the_lines()
    {
        using var owner = HeroWithScroll();
        var host = ScrollHost(owner, C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 5), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault));
        var caster = CasterOf(owner, host);
        Assert.IsTrue(caster.SpendUsedItem("relic-scroll"));
        Assert.IsTrue(owner.IsItemDestroyed("relic-scroll"));
        Assert.AreEqual(true, ReadFlag(owner, 3));
        var line = caster.Log.Single(e => e.Kind == SpellCastEventKind.ItemScriptLine);
        Assert.AreEqual(($"line 5 of {RemoveScript}", RemoveScript, SessionSpellCaster.Hero), (line.Text, line.Detail, line.Object), "the PC reads the plain line variant");
    }

    [TestMethod]
    public void Without_an_item_script_host_a_hero_scroll_with_a_remove_item_script_is_refused_and_the_destroy_with_a_host_returns_the_lines()
    {
        using var owner = HeroWithScroll();
        var caster = CasterOf(owner, null);
        Assert.IsFalse(caster.SpendUsedItem("relic-scroll"));
        Assert.IsFalse(owner.IsItemDestroyed("relic-scroll"));
        var host = ScrollHost(owner, C(ScrCatalogue.Action.PrintLine, (Number, 6), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault));
        var destroyed = owner.DestroyUsedItemWithLines(owner.Version, "relic-scroll", host);
        Assert.AreEqual((GameSessionDisposition.Changed, "relic-scroll"), (destroyed.Disposition, destroyed.Item));
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 6, 0, $"line 6 of {RemoveScript}") }, destroyed.Messages.ToArray());
    }
}
