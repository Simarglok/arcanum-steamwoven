using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.InsertItemScriptTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-1119 (HK24): the examine script (point 0) of an item the session made, which has no object row, runs from the prototype's slot
// through the session's item-run machinery. Invented prototype, scripts and texts.
[TestClass]
public sealed class ItemExamineScriptTests
{
    private const int Prototype = 7800, ExamineScript = 810;
    private static readonly SessionItemKind Kind = new(SessionItemType.Armor, Prototype, 7801, 7802);
    private const byte Counter = 0;

    private sealed class Host(GameSessionOwner session) : ISessionItemScriptHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = [];
        public List<string> Asked { get; } = [];
        public ScrSlot? Slot(ScrObjectRef attachee, int point) { Asked.Add($"slot {attachee.Code} {point}"); return null; }
        public ScrScriptLookup Script(int number) => Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;
        public bool GlobalFlag(int index) => ReadFlag(session, index) ?? false;
        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(session.QueryActor().Map.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;
        public SessionPrintedLine PrintedLine(int script, int line) => new(SessionPrintedLineStatus.Shown, $"line {script}/{line}", "test");
    }

    // A session with one made item in the hero's list whose prototype has an examine element (script, flags 3, counters 0), the source attached or not.
    private static (GameSessionOwner Owner, string Entity, Host Host) Setup(ScrProgram? program, bool attach = true, int script = ExamineScript)
    {
        var owner = CreateWith(HalfElf());
        if (attach) owner.AttachItemScripts(prototype => prototype == Prototype ? [new(SessionScriptPoint.Examine, script, 3, 0), new(SessionScriptPoint.Use, 0, 0, 0)] : null);
        var entity = owner.NextObjectEntity(SessionObjectOrigin.Made)!;
        owner.CreateMadeItem(new(entity, Kind, SessionCreatePlace.HeroList, new SessionItemWeight(1, 0, 0)));
        var host = new Host(owner);
        if (program is not null) host.Programs[ExamineScript] = program;
        return (owner, entity, host);
    }

    [TestMethod]
    public void An_item_without_a_script_at_the_point_runs_nothing()
    {
        // Detached source: the made item has no attachment at all; the point with no element likewise.
        var (owner, entity, host) = Setup(Program(C(ScrCatalogue.Action.ReturnSkipDefault)), attach: false);
        using var _ = owner;
        var before = owner.Version;
        var none = owner.RunItemExamine(before, entity, host);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, 0), (none.Disposition, none.Failure, none.Scripts.Count));
        Assert.IsNull(none.DeclinedBy);
        Assert.AreEqual(before, owner.Version);
        // A script number 0 at the point is an empty element, not a run.
        var (zero, zeroEntity, zeroHost) = Setup(Program(C(ScrCatalogue.Action.ReturnSkipDefault)), script: 0);
        using var __ = zero;
        Assert.AreEqual((GameSessionDisposition.NoChange, 0), (zero.RunItemExamine(zero.Version, zeroEntity, zeroHost).Disposition, zeroHost.Asked.Count));
    }

    [TestMethod]
    public void Skip_default_declines_the_description_and_keeps_what_the_run_did()
    {
        var (owner, entity, host) = Setup(Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnSkipDefault)));
        using var _ = owner;
        var start = owner.Version.Revision;
        var plain = owner.RunItemExamine(owner.Version, entity, host);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, SessionScriptPoint.Examine), (plain.Disposition, plain.Failure, plain.DeclinedBy));
        Assert.AreEqual(1, plain.Messages.Count);
        Assert.AreEqual($"line {ExamineScript}/1", plain.Messages[0].Text);
        CollectionAssert.AreEqual(new[] { (SessionScriptPoint.Examine, entity) }, plain.Scripts.Select(run => (run.Point, run.Attachee)).ToArray());
        Assert.AreEqual(start, owner.Version.Revision);

        var (writer, writerEntity, writerHost) = Setup(Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault)));
        using var __ = writer;
        var writerStart = writer.Version.Revision;
        var wrote = writer.RunItemExamine(writer.Version, writerEntity, writerHost);
        Assert.AreEqual((GameSessionDisposition.Changed, SessionScriptPoint.Examine), (wrote.Disposition, wrote.DeclinedBy));
        Assert.AreEqual((true, writerStart + 1), (ReadFlag(writer, 3), writer.Version.Revision));
    }

    [TestMethod]
    public void A_script_that_runs_past_its_last_line_skips_the_default()
    {
        var (owner, entity, host) = Setup(Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 2))));
        using var _ = owner;
        var ran = owner.RunItemExamine(owner.Version, entity, host);
        Assert.AreEqual(SessionScriptPoint.Examine, ran.DeclinedBy);
    }

    [TestMethod]
    public void Run_default_lets_the_description_follow_and_keeps_the_slot_the_run_wrote()
    {
        var (owner, entity, host) = Setup(Program(C(ScrCatalogue.Action.Assign, (Counter, 0), (Number, 7)), C(ScrCatalogue.Action.ReturnRunDefault)));
        using var _ = owner;
        var start = owner.Version.Revision;
        var ran = owner.RunItemExamine(owner.Version, entity, host);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, entity), (ran.Disposition, ran.Failure, ran.Item));
        Assert.IsNull(ran.DeclinedBy);
        Assert.AreEqual(start + 1, owner.Version.Revision);
        // The first write copied the prototype's array onto the item (the all-zero use element is not kept) and replaced the examine element's counters.
        CollectionAssert.AreEqual(new[] { new SessionItemScriptSlot(entity, SessionScriptPoint.Examine, ExamineScript, 3, 7) }, owner.QueryItemScriptSlots().ToArray());
        // A second run reads the kept slot: nothing changes now.
        var again = owner.RunItemExamine(owner.Version, entity, host);
        Assert.AreEqual((GameSessionDisposition.NoChange, start + 1), (again.Disposition, owner.Version.Revision));
        Assert.AreEqual(1, again.Scripts.Count);
    }

    [TestMethod]
    public void A_run_the_session_cannot_apply_is_refused_and_applies_nothing()
    {
        var (owner, entity, host) = Setup(Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.Dialog, (Number, 10))));
        using var _ = owner;
        var start = owner.Version.Revision;
        var refused = owner.RunItemExamine(owner.Version, entity, host);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported), (refused.Disposition, refused.Failure));
        Assert.AreEqual((false, start, 0), (ReadFlag(owner, 3), owner.Version.Revision, owner.QueryItemScriptSlots().Count));
        // A script file the host lacks is a missing script: the dispatcher answers skip default and applies nothing.
        var (missing, missingEntity, missingHost) = Setup(null);
        using var __ = missing;
        var gone = missing.RunItemExamine(missing.Version, missingEntity, missingHost);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None), (gone.Disposition, gone.Failure));
        Assert.AreEqual(SessionScriptPoint.Examine, gone.DeclinedBy, "a missing script file answers skip default, as the dispatcher does for every point");
    }

    [TestMethod]
    public void Only_a_made_item_the_session_holds_is_examined_here()
    {
        var (owner, entity, host) = Setup(Program(C(ScrCatalogue.Action.ReturnSkipDefault)));
        using var _ = owner;
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.RunItemExamine(owner.Version, "kit:99", host).Failure, "a kit item the hero does not hold");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.RunItemExamine(owner.Version, "made:99", host).Failure, "an item the session does not hold");
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.RunItemExamine(owner.Version, "", host).Failure);
        using var other = CreateWith(HalfElf());
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RunItemExamine(other.Version, entity, host).Failure, "a version of another session");
        Assert.AreEqual(0, host.Asked.Count);
    }

    // ARC-UI-1458: a starting-kit item is built from its prototype as a made item is, so it owns no row and examines from the prototype's slot too.
    [TestMethod]
    public void A_kit_item_examines_from_its_prototype_and_has_a_prototype_kind_wherever_it_is()
    {
        using var owner = CreateWith(HalfElf() with { Kit = [new(Kind)] });
        owner.AttachItemScripts(prototype => prototype == Prototype ? [new(SessionScriptPoint.Examine, ExamineScript, 3, 0)] : null);
        var host = new Host(owner);
        host.Programs[ExamineScript] = Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnSkipDefault));
        Assert.AreEqual(Kind, owner.QueryPrototypeItemKind("kit:1"));
        Assert.IsNull(owner.QueryPrototypeItemKind("kit:2"), "no such kit item");
        Assert.IsNull(owner.QueryPrototypeItemKind("not an entity"));
        var ran = owner.RunItemExamine(owner.Version, "kit:1", host);
        Assert.AreEqual((GameSessionDisposition.NoChange, GameSessionFailure.None, SessionScriptPoint.Examine), (ran.Disposition, ran.Failure, ran.DeclinedBy));
        Assert.AreEqual(1, ran.Messages.Count);
        // Every item run reads the kit item's prototype slot, as a made item's, and an item whose prototype has no element has none.
        Assert.AreEqual(new SessionItemScriptSlot("kit:1", SessionScriptPoint.Examine, ExamineScript, 3, 0), owner.QueryItemScriptSlot("kit:1", SessionScriptPoint.Examine));
        Assert.IsNull(owner.QueryItemScriptSlot("kit:1", SessionScriptPoint.Use));
    }
}
