using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2887 (ledger TQ19, NT2; exe 0x4617F0 → 0x461810 fresh path, 0x466640): an object the game creates and moves into a holder (the new gold pile of 0x464830, the new ammunition stack of 0x4658E0, a restock item of 0x463E20) has no parent, so
// the move runs the new item's get script (point 4, the holder as the triggerer, no extra) before the insert check (skip default refuses the move) and at the end of the insert its insert-item script (21), also with the holder as the triggerer.
// Both scripts come from the prototype's array (the new item owns no slot yet). Over the synthetic seed of InventoryStateTests; script numbers, programs and texts are invented.
[TestClass]
public sealed class CreatedItemScriptTests
{
    internal const int GetScript = 703, InsertScript = 704;
    internal const int GoldProto = 9056, AmmoProto = 7059;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, GoldProto, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, AmmoProto, 3301, 33301);

    internal sealed class Host(GameSessionOwner session) : ISessionItemScriptHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = new()
        {
            [GetScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.PrintLine, (Number, 5), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault)),
            [InsertScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 7)), C(ScrCatalogue.Action.PrintLine, (Number, 6), (Number, 0)), C(ScrCatalogue.Action.ReturnRunDefault)),
        };

        public ScrSlot? Slot(ScrObjectRef attachee, int point) => null;
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
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, $"hero reads {line}", "test");
        public SessionPrintedLine PrintedLineForNpc(int script, int line) => new(SessionPrintedLineStatus.Shown, $"npc reads {line}", "test");
    }

    /// <summary>The prototype arrays of the new items: the gold pile and the ammunition both carry a get and an insert-item script.</summary>
    internal static Func<int, IReadOnlyList<SessionPrototypeScript>?> Prototypes { get; } = prototype =>
        prototype is GoldProto or AmmoProto ? new SessionPrototypeScript[] { new(SessionScriptPoint.Get, GetScript, 0, 0), new(SessionScriptPoint.InsertItem, InsertScript, 0, 0) } : null;

    private static GameSessionOwner Hero(bool attach = true)
    {
        var owner = CreateWith(HalfElf());
        var seed = Seed();
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed with
        {
            Holders = [.. seed.Holders, new("archer", SessionHolderKind.Npc, false, false, [], ["arrows"])],
            Items = [.. seed.Items, new SessionSeedItem("arrows", Facts(SessionItemType.Ammo, 3300, quantity: 30) with { AmmoType = 0 }, null)],
        }).Failure);
        if (attach) owner.AttachItemScripts(Prototypes);
        return owner;
    }

    private static (int Point, string Attachee)[] Points(InventoryMutationResult result) => result.Scripts.Select(run => (run.Point, run.Attachee)).ToArray();

    [TestMethod]
    public void A_new_gold_pile_runs_the_get_script_then_the_insert_script_with_the_npc_as_the_triggerer_and_the_new_pile_as_the_attachee()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var given = owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:1"), (given.Disposition, given.Failure, given.Item));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "made:1"), (SessionScriptPoint.InsertItem, "made:1") }, Points(given));
        Assert.IsNotNull(given.InsertScript);
        CollectionAssert.AreEqual(new[] { "npc reads 5", "npc reads 6" }, given.Messages.Select(message => message.Text).ToArray());
        Assert.AreEqual(true, ReadFlag(owner, 3), "the get run's flag applies with the move");
        Assert.AreEqual((40, 345), (owner.QueryNpcGold("virgil"), owner.QueryGold()), "the pile is the NPC's and the hero's coins went");
    }

    [TestMethod]
    public void A_get_script_that_skips_the_default_refuses_the_new_pile_and_nothing_moves()
    {
        using var owner = Hero();
        var host = new Host(owner);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var revision = owner.Version.Revision;
        var gold = owner.QueryGold();
        var given = owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, (int?)SessionScriptPoint.Get), (given.Disposition, given.Failure, given.DeclinedBy));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "made:1") }, Points(given), "the insert-item script is not run");
        Assert.AreEqual((revision, gold, 0), (owner.Version.Revision, owner.QueryGold(), owner.QueryNpcGold("virgil")));
        Assert.AreEqual(false, ReadFlag(owner, 3) == true, "a refused move applies none of its script's effects");
        Assert.AreEqual("made:1", owner.GiveGoldToNpc(owner.Version, "virgil", 1, GoldPile, Accept, new Host(owner)).Item, "the entity the refusal did not use is the next one made");
    }

    [TestMethod]
    public void A_new_pile_whose_prototype_has_scripts_is_refused_without_a_host_and_one_without_scripts_runs_none()
    {
        using var owner = Hero();
        var refused = owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported, 0), (refused.Disposition, refused.Failure, owner.QueryNpcGold("virgil")));
        using var plain = Hero(attach: false);
        var given = plain.GiveGoldToNpc(plain.Version, "virgil", 40, GoldPile, Accept, new Host(plain));
        Assert.AreEqual((GameSessionDisposition.Changed, 0), (given.Disposition, given.Scripts.Count), "no prototype source: no attachment is known, as before");
    }

    [TestMethod]
    public void A_pile_made_from_nothing_and_a_pile_moved_between_npcs_run_the_new_pile_scripts_too()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var made = owner.GiveGoldToNpcFromNothing(owner.Version, "virgil", 7, GoldPile, Accept, host);
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "made:1"), (SessionScriptPoint.InsertItem, "made:1") }, Points(made));
        // The merchant's coins (a seed pile without scripts) go to an NPC with no pile: the new pile runs its scripts, the source none.
        var moved = owner.MoveGoldBetweenNpcs(owner.Version, "merchant", "archer", 5, GoldPile, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:2"), (moved.Disposition, moved.Item));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Get, "made:2"), (SessionScriptPoint.InsertItem, "made:2") }, Points(moved));
    }

    [TestMethod]
    public void A_new_ammunition_stack_runs_the_get_then_the_insert_script_after_the_source_stacks_own_insert_script()
    {
        using var owner = Hero();
        var host = new Host(owner);
        var taken = owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:1"), (taken.Disposition, taken.Item));
        // The made stack carries the prototype's insert-item script, so giving units away runs 21 for it (the source), then the new stack's 4 and 21.
        var given = owner.GiveAmmoToNpc(owner.Version, "virgil", "made:1", 5, Bullets, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "made:2"), (given.Disposition, given.Item));
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.InsertItem, "made:1"), (SessionScriptPoint.Get, "made:2"), (SessionScriptPoint.InsertItem, "made:2") }, Points(given));
        CollectionAssert.AreEqual(new[] { "npc reads 6", "npc reads 5", "npc reads 6" }, given.Messages.Select(message => message.Text).ToArray());
    }

    [TestMethod]
    public void A_refusing_get_script_of_a_new_ammunition_stack_leaves_the_source_stack_alone()
    {
        using var owner = Hero();
        var host = new Host(owner);
        Assert.AreEqual("made:1", owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets, Accept, host).Item);
        host.Programs[GetScript] = Program(C(ScrCatalogue.Action.ReturnSkipDefault));
        var revision = owner.Version.Revision;
        var refused = owner.GiveAmmoToNpc(owner.Version, "virgil", "made:1", 5, Bullets, Accept, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, (int?)SessionScriptPoint.Get), (refused.Disposition, refused.Failure, refused.DeclinedBy));
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(30, owner.QueryHeroAmmo().Single(stack => stack.Entity == "made:1").Units, "the hero's stack keeps every unit");
    }
}
