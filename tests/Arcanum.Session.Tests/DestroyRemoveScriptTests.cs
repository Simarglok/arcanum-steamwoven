using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2880 (ledger IE3; exe read 0x43CCA0 → 0x4639E0 → 0x467860): the items a critter's or container's destroy takes out (object flag 0x400000) go through the removal, which runs their remove-item script (point 28)
// with the dying object as the triggerer, result ignored; the lines the run printed reach the host. Invented script numbers and items.
[TestClass]
public sealed class DestroyRemoveScriptTests
{
    private const int RemoveScript = 705;
    private static readonly CellCoord Spot = new(101, 202);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private sealed class Host(GameSessionOwner session, params string[] scripted) : ISessionItemScriptHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = [];
        public List<string> Asked { get; } = [];

        public ScrSlot? Slot(ScrObjectRef attachee, int point)
        {
            Asked.Add($"slot {attachee.Code} {point}");
            return point == SessionScriptPoint.RemoveItem && scripted.Contains(attachee.Code) ? new ScrSlot(RemoveScript, 0, 0) : null;
        }

        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => session.Flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), session.FlagVersion, new FlagId(index))).Value ?? false;
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, $"line {line} of {script}", "test");
        public SessionPrintedLine PrintedLineForNpc(int script, int line) => new(SessionPrintedLineStatus.Shown, $"npc line {line} of {script}", "test");

        public string[] RemoveAsks => Asked.Where(ask => ask.EndsWith(" 28", StringComparison.Ordinal)).Select(ask => ask.Split(' ')[1]).ToArray();
    }

    // The holder's list: bread (no flag, with a point 28), relic and amulet (flag, point 28), glass (flag, no point), in that order.
    private static GameSessionOwner With(SessionHolderKind kind)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        var plain = InventoryStateTests.Facts(SessionItemType.Generic, 5600);
        var scripted = plain with { ScriptPoints = [SessionScriptPoint.RemoveItem] };
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new SessionSeedHolder("holder", kind, false, false, [], ["bread", "relic", "amulet", "glass"])],
            [new SessionSeedItem("bread", scripted, null), new SessionSeedItem("relic", scripted with { Invulnerable = true }, null),
             new SessionSeedItem("amulet", scripted with { Invulnerable = true }, null), new SessionSeedItem("glass", plain with { Invulnerable = true }, null)])).Failure);
        return owner;
    }

    private static Host Hosted(GameSessionOwner owner, params (int Type, (byte Kind, int Value)[] Operands)[] actions)
    {
        var host = new Host(owner, "bread", "relic", "amulet");
        host.Programs[RemoveScript] = Program(actions);
        return host;
    }

    private static (int Type, (byte Kind, int Value)[] Operands)[] FlagAndPrint() =>
        [C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 2), (Number, 0)), C(ScrCatalogue.Action.ReturnSkipDefault)];

    private static long? Changed(GameSessionOwner owner, string entity, string property) => owner.QueryMapChange(Exterior, MapChangeKey.ForEntity(entity, property));

    [TestMethod]
    public void A_destroyed_npcs_taken_out_items_run_their_remove_item_script_in_the_walk_order_with_the_npc_as_the_triggerer()
    {
        using var owner = With(SessionHolderKind.Npc);
        var host = Hosted(owner, FlagAndPrint());
        var result = owner.DestroyMapNpc(owner.Version, Exterior, "holder", Spot, scripts: host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 2L), (result.Disposition, result.Failure, owner.Version.Revision), "the destroy and the drops");
        CollectionAssert.AreEqual(new[] { "amulet", "relic" }, host.RemoveAsks, "the list is walked from its last item: the glass has no point, the bread no flag");
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 2, 0, $"npc line 2 of {RemoveScript}"), new SessionScriptMessage(RemoveScript, 2, 0, $"npc line 2 of {RemoveScript}") }, result.Messages.ToArray(), "an NPC triggerer reads its own line variant");
        Assert.AreEqual(true, ReadFlag(owner, 3), "the runs' flag write applied with the destroy");
        Assert.AreEqual(1L, Changed(owner, "holder", GameSessionOwner.ObjectDestroyedProperty));
        foreach (var lying in new[] { "relic", "amulet", "glass" }) Assert.IsNotNull(Changed(owner, lying, GameSessionOwner.ItemDroppedProperty), lying + " lies on the ground");
        Assert.IsNull(Changed(owner, "bread", GameSessionOwner.ItemDroppedProperty), "an item without the flag stays in the destroyed list, with no run");
        Assert.IsFalse(owner.IsItemDestroyed("relic"), "the script's answer (skip default) is ignored: the item is dropped, not deleted");
    }

    [TestMethod]
    public void A_destroyed_containers_taken_out_items_run_their_script_with_the_container_as_the_triggerer()
    {
        using var owner = With(SessionHolderKind.Container);
        var host = Hosted(owner, FlagAndPrint());
        Assert.IsNull(owner.DestroyMapContainerRefusal(Exterior, "holder", Spot, junkPile: false, host));
        Assert.AreEqual((0L, (bool?)false), (owner.Version.Revision, ReadFlag(owner, 3)), "the dry run applies nothing");
        var result = owner.DestroyMapContainer(owner.Version, Exterior, "holder", Spot, junkPile: false, host);
        Assert.AreEqual((GameSessionDisposition.Changed, 2L), (result.Disposition, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 2, 0, $"line 2 of {RemoveScript}"), new SessionScriptMessage(RemoveScript, 2, 0, $"line 2 of {RemoveScript}") }, result.Messages.ToArray(), "a container is no NPC: the plain line variant");
        Assert.AreEqual(true, ReadFlag(owner, 3));
        Assert.IsNotNull(Changed(owner, "relic", GameSessionOwner.ItemDroppedProperty));
        Assert.IsNull(Changed(owner, "bread", GameSessionOwner.ItemDroppedProperty));
    }

    [TestMethod]
    public void Without_a_script_host_an_invulnerable_item_with_a_remove_item_script_refuses_the_destroy_whole()
    {
        foreach (var kind in new[] { SessionHolderKind.Npc, SessionHolderKind.Container })
        {
            using var owner = With(kind);
            var refusal = kind == SessionHolderKind.Npc ? owner.DestroyMapNpcRefusal(Exterior, "holder", Spot) : owner.DestroyMapContainerRefusal(Exterior, "holder", Spot, junkPile: false);
            Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, refusal);
            var refused = kind == SessionHolderKind.Npc ? owner.DestroyMapNpc(owner.Version, Exterior, "holder", Spot) : owner.DestroyMapContainer(owner.Version, Exterior, "holder", Spot, junkPile: false);
            Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L), (refused.Disposition, refused.Failure, owner.Version.Revision));
            Assert.IsNull(Changed(owner, "holder", GameSessionOwner.ObjectDestroyedProperty), "nothing is destroyed on a guess");
            Assert.IsNull(Changed(owner, "relic", GameSessionOwner.ItemDroppedProperty));
        }
    }

    [TestMethod]
    public void An_item_without_the_flag_or_the_point_needs_no_script_host()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        var plain = InventoryStateTests.Facts(SessionItemType.Generic, 5600);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new SessionSeedHolder("holder", SessionHolderKind.Npc, false, false, [], ["bread", "glass"])],
            [new SessionSeedItem("bread", plain with { ScriptPoints = [SessionScriptPoint.RemoveItem] }, null), new SessionSeedItem("glass", plain with { Invulnerable = true }, null)])).Failure);
        var result = owner.DestroyMapNpc(owner.Version, Exterior, "holder", Spot);
        Assert.AreEqual((GameSessionDisposition.Changed, 0), (result.Disposition, result.Messages.Count));
        Assert.IsNotNull(Changed(owner, "glass", GameSessionOwner.ItemDroppedProperty));
    }

    [TestMethod]
    public void A_remove_item_run_the_session_cannot_apply_refuses_the_destroy_before_any_effect()
    {
        using var owner = With(SessionHolderKind.Npc);
        var bad = Hosted(owner, C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.AddEffect, ((byte)0, 0), (Number, 50), (Number, 5)), C(ScrCatalogue.Action.ReturnRunDefault));
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.DestroyMapNpcRefusal(Exterior, "holder", Spot, bad));
        var refused = owner.DestroyMapNpc(owner.Version, Exterior, "holder", Spot, scripts: bad);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0L, (bool?)false), (refused.Disposition, refused.Failure, owner.Version.Revision, ReadFlag(owner, 3)));
        Assert.IsNull(Changed(owner, "holder", GameSessionOwner.ObjectDestroyedProperty));
        Assert.IsNull(Changed(owner, "relic", GameSessionOwner.ItemDroppedProperty));
        var good = Hosted(owner, FlagAndPrint());
        Assert.IsNull(owner.DestroyMapNpcRefusal(Exterior, "holder", Spot, good));
    }

    [TestMethod]
    public void The_destroy_of_a_destroyed_npc_runs_nothing_again()
    {
        using var owner = With(SessionHolderKind.Npc);
        var host = Hosted(owner, FlagAndPrint());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyMapNpc(owner.Version, Exterior, "holder", Spot, scripts: host).Disposition);
        host.Asked.Clear();
        var again = owner.DestroyMapNpc(owner.Version, Exterior, "holder", Spot, scripts: host);
        Assert.AreEqual((GameSessionDisposition.NoChange, 0, 0), (again.Disposition, again.Messages.Count, host.RemoveAsks.Length));
    }

    [TestMethod]
    public void The_used_up_items_remove_item_lines_come_with_its_result()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() }).Owner!;
        using var _ = owner;
        var food = InventoryStateTests.Facts(SessionItemType.Food, 3600) with { ScriptPoints = [SessionScriptPoint.RemoveItem] };
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([], [new SessionSeedItem("nectar", food, new CellCoord(101, 200))])).Failure);
        var host = new Host(owner, "nectar");
        host.Programs[RemoveScript] = Program(C(ScrCatalogue.Action.PrintLine, (Number, 7), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "nectar", Accept).Disposition);
        var used = owner.DestroyUsedItemWithLines(owner.Version, "nectar", host);
        Assert.AreEqual(GameSessionDisposition.Changed, used.Disposition);
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(RemoveScript, 7, 0, $"line 7 of {RemoveScript}") }, used.Messages.ToArray(), "the hero is the triggerer: the plain line variant");
        Assert.IsTrue(owner.IsItemDestroyed("nectar"));
    }
}
