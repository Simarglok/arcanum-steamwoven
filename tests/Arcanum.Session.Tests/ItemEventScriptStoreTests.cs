using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-DEV-152 (MIG8 leftover of ARC-DEV-100): an item event's run reads and writes the global variables and the hero's variables and
// flags (value kinds 1, 5, 6), as the move's global flags: a later run of the move sees the earlier runs' writes, the commit applies
// them with the move in one revision, a declined move keeps them, and a session without a hero drops the PC's (SA14). Seed of
// InventoryStateTests; script numbers and programs are invented.
[TestClass]
public sealed class ItemEventScriptStoreTests
{
    private const int GetScript = 610, RemoveScript = 612, InsertScript = 613;
    private const byte GlobalVariable = 1, PcVariable = 5, PcFlag = 6, Player = 21;

    private sealed class Host : ISessionItemScriptHost
    {
        public Dictionary<(string, int), int> Slots { get; } = [];
        public Dictionary<int, ScrProgram> Programs { get; } = [];

        public ScrSlot? Slot(ScrObjectRef attachee, int point) =>
            Slots.TryGetValue((attachee.Code, point), out var script) ? new ScrSlot(script, 0, 0) : null;
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
    }

    private static (byte, int) G(int index) => (GlobalVariable, index);
    private static (byte, int) N(int value) => (Number, value);
    private static (byte, int) Pc(byte kind, int index) => (kind, (Player << 16) | index);
    private static (int, (byte, int)[]) Assign((byte, int) store, (byte, int) value) => C(ScrCatalogue.Action.Assign, store, value);

    // "torn" (held by the corpse) carries remove-item and insert-item scripts, "charm" (on the ground) a get script.
    private static GameSessionOwner Seeded(bool hero)
    {
        var owner = hero ? HeroStateTests.CreateWith(HeroStateTests.HalfElf()) : Create();
        var seed = InventoryStateTests.Seed();
        var items = seed.Items.Select(item => item.Entity switch
        {
            "torn" => item with { Facts = item.Facts with { ScriptPoints = [SessionScriptPoint.RemoveItem, SessionScriptPoint.InsertItem] } },
            "charm" => item with { Facts = item.Facts with { ScriptPoints = [SessionScriptPoint.Get] } },
            _ => item,
        }).ToArray();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed with { Items = items }).Disposition);
        return owner;
    }

    private static Host Loot()
    {
        var host = new Host();
        host.Slots[("torn", SessionScriptPoint.RemoveItem)] = RemoveScript;
        host.Slots[("torn", SessionScriptPoint.InsertItem)] = InsertScript;
        // The remove-item script writes one cell of each store; the insert-item script copies each into the next cell, so the copies
        // hold the values only if it saw the writes before either was applied.
        // The remove-item script also sets global flag 3, so the move commits a flag and the stores together.
        host.Programs[RemoveScript] = Program(Assign(G(5), N(-3)), Assign(Pc(PcVariable, 9), N(11)), Assign(Pc(PcFlag, 33), N(1)),
            C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnRunDefault));
        // G(7) copies the PC variable into a global one: it shows what a later run read of the PC cell.
        host.Programs[InsertScript] = Program(Assign(G(6), G(5)), Assign(Pc(PcVariable, 10), Pc(PcVariable, 9)), Assign(Pc(PcFlag, 34), Pc(PcFlag, 33)),
            Assign(G(7), Pc(PcVariable, 9)), C(ScrCatalogue.Action.ReturnRunDefault));
        return host;
    }

    [TestMethod]
    public void A_loot_s_runs_read_each_other_s_store_writes_and_the_move_applies_them_in_one_revision()
    {
        using var owner = Seeded(hero: true);
        var revision = owner.Version.Revision;
        var looted = owner.LootItem(owner.Version, "corpse", "torn", _ => SessionInsertVerdict.Accepted, Loot());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "torn"), (looted.Disposition, looted.Failure, looted.Item));
        Assert.AreEqual(ScrOutcome.RunDefault, looted.InsertScript!.Outcome, looted.InsertScript.Refusal?.ToString());
        Assert.AreEqual(revision + 1, owner.Version.Revision, "the stores move with the item, in its one revision");
        Assert.AreEqual((-3, -3), (owner.QueryGlobalVariable(5), owner.QueryGlobalVariable(6)));
        Assert.AreEqual((11, 11), (owner.QueryPcVariable(9), owner.QueryPcVariable(10)));
        Assert.AreEqual((true, true, false), (owner.QueryPcFlag(33), owner.QueryPcFlag(34), owner.QueryPcFlag(32)));
        Assert.AreEqual((11, true), (owner.QueryGlobalVariable(7), ReadFlag(owner, 3)), "the flag commits with the stores");
    }

    [TestMethod]
    public void A_session_without_a_hero_keeps_the_global_variables_and_drops_the_pc_s()
    {
        using var owner = Seeded(hero: false);
        var looted = owner.LootItem(owner.Version, "corpse", "torn", _ => SessionInsertVerdict.Accepted, Loot());
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (looted.Disposition, looted.Failure));
        Assert.AreEqual((-3, -3), (owner.QueryGlobalVariable(5), owner.QueryGlobalVariable(6)));
        Assert.AreEqual(0, owner.QueryGlobalVariable(7), "a later run reads the PC cell as 0, not the earlier run's dropped write");
        Assert.AreEqual((0, 0, false), (owner.QueryPcVariable(9), owner.QueryPcVariable(10), owner.QueryPcFlag(33)), "SA14: no PC to write to");
        Assert.IsTrue(owner.QueryScriptState().PcFlags.Count == 0 && owner.QueryScriptState().PcVariables.Count == 0);
    }

    [TestMethod]
    public void A_declined_pickup_keeps_its_store_writes_and_a_write_of_the_held_value_changes_nothing()
    {
        using var owner = Seeded(hero: true);
        var host = new Host();
        host.Slots[("charm", SessionScriptPoint.Get)] = GetScript;
        host.Programs[GetScript] = Program(Assign(Pc(PcVariable, 2), N(7)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var revision = owner.Version.Revision;
        var declined = owner.PickUpItem(owner.Version, "charm", _ => SessionInsertVerdict.Accepted, host);
        Assert.AreEqual((GameSessionDisposition.Changed, (string?)null, (int?)SessionScriptPoint.Get, revision + 1),
            (declined.Disposition, declined.Item, declined.DeclinedBy, owner.Version.Revision));
        Assert.AreEqual(7, owner.QueryPcVariable(2));

        var again = owner.PickUpItem(owner.Version, "charm", _ => SessionInsertVerdict.Accepted, host);
        Assert.AreEqual((GameSessionDisposition.NoChange, revision + 1), (again.Disposition, owner.Version.Revision), "7 is already held");
    }
}
