using System.Buffers.Binary;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-013: an item's insert-item script on a session transfer. Seed of InventoryStateTests ("glove" lies on the
// ground, "boots" in the dead corpse, both with an insert-item script); programs and texts are invented.
[TestClass]
public sealed class InsertItemScriptTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    internal const byte Local = 2, Number = 3, Flag = 4;
    private const int Amulet = 500;

    internal static (int Type, (byte Kind, int Value)[] Operands) C(int type, params (byte, int)[] operands) => (type, operands);

    internal static ScrProgram Program(params (int Type, (byte Kind, int Value)[] Operands)[] actions)
    {
        var bytes = new byte[64 + actions.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), actions.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), actions.Length);
        for (var line = 0; line < actions.Length; line++)
        {
            // Always true, then the action; the false branch does nothing.
            var slot = bytes.AsSpan(64 + line * 132 + 44, 44);
            BinaryPrimitives.WriteInt32LittleEndian(slot, actions[line].Type);
            for (var i = 0; i < actions[line].Operands.Length; i++)
            {
                slot[4 + i] = actions[line].Operands[i].Kind;
                BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], actions[line].Operands[i].Value);
            }
        }
        var read = ScrProgram.Read(bytes);
        Assert.AreEqual(ScrReadStatus.Read, read.Status);
        return read.Program!;
    }

    private static readonly ScrProgram PrintAndSkip = Program(C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6)), C(ScrCatalogue.Action.ReturnSkipDefault));

    /// <summary>A host with the item slots of the seed's scripted items and fixed DLG answers; it reads the session's flags.</summary>
    private sealed class Host(GameSessionOwner session) : ISessionItemScriptHost
    {
        public Dictionary<int, ScrProgram> Programs { get; } = new() { [Amulet] = PrintAndSkip };
        public Dictionary<(int, int), SessionPrintedLine> Lines { get; } = new() { [(Amulet, 1)] = new(SessionPrintedLineStatus.Shown, "It hums.", "line 1") };
        public List<string> Asked { get; } = [];
        public Action? DuringLine { get; set; }
        public bool Unreadable { get; init; }

        public ScrSlot? Slot(ScrObjectRef attachee, int point)
        {
            Asked.Add($"slot {attachee.Code} {point}");
            return point == SessionScriptPoint.InsertItem && attachee.Code is "glove" or "boots" or "glove-held" ? new ScrSlot(Amulet, 0, 0) : null;
        }

        public ScrScriptLookup Script(int number) => Unreadable ? ScrScriptLookup.Unreadable
            : Programs.TryGetValue(number, out var program) ? ScrScriptLookup.Of(program) : ScrScriptLookup.Missing;

        public bool GlobalFlag(int index)
        {
            var read = session.Flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), session.FlagVersion, new FlagId(index)));
            // A flag the session holds as unknown answers false here; the session refuses a write to it.
            return read.Value ?? false;
        }

        public int StoryState() => throw new InvalidOperationException("not admitted");
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => new ScrObjectRef(Exterior.RuntimeMap, SessionEventKinds.PlayerEntity);
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter.Code == SessionEventKinds.PlayerEntity;

        public SessionPrintedLine PrintedLine(int script, int line)
        {
            Asked.Add($"line {script} {line}");
            DuringLine?.Invoke();
            return Lines.TryGetValue((script, line), out var printed) ? printed : new(SessionPrintedLineStatus.NoDialog, string.Empty, "no dialog");
        }
    }

    private static GameSessionOwner Attached() => InventoryStateTests.Attached();
    private static string[] Pc(GameSessionOwner owner) => owner.QueryPcInventory().Select(item => item.Entity).ToArray();

    private static void Refused(InventoryMutationResult result, GameSessionOwner owner, GameSessionFailure failure, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void The_insert_item_script_runs_on_pickup_and_loot_and_its_line_comes_back()
    {
        using var owner = Attached();
        // Without a host the item is refused as before.
        Refused(owner.PickUpItem(owner.Version, "glove", Accept), owner, GameSessionFailure.ItemScriptUnsupported, 0);
        var host = new Host(owner);
        var picked = owner.PickUpItem(owner.Version, "glove", Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, "glove", 1L), (picked.Disposition, picked.Item, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { new SessionScriptMessage(Amulet, 1, 6, "It hums.") }, picked.Messages.ToArray());
        Assert.AreEqual((Amulet, ScrStop.ReturnSkipDefault), (picked.InsertScript!.Script, picked.InsertScript.Stop));
        CollectionAssert.Contains(host.Asked, $"slot glove {SessionScriptPoint.InsertItem}");

        // Loot: the item's own examine point is not run; the insert-item script is.
        var looted = owner.LootItem(owner.Version, "corpse", "boots", Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, 1), (looted.Disposition, looted.Messages.Count));
        CollectionAssert.AreEqual(new[] { "glove", "boots" }, Pc(owner));
        Assert.IsFalse(host.Asked.Any(question => question.EndsWith($" {SessionScriptPoint.Examine}", StringComparison.Ordinal)));

        // An item without the script never asks the host.
        host.Asked.Clear();
        Assert.AreEqual(0, owner.PickUpItem(owner.Version, "apple", Accept, host).Messages.Count);
        Assert.AreEqual(0, host.Asked.Count);
    }

    [TestMethod]
    public void A_missing_script_or_dialog_takes_the_item_without_a_line()
    {
        using var owner = Attached();
        var host = new Host(owner);
        host.Lines.Clear();
        var noDialog = owner.PickUpItem(owner.Version, "glove", Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, 0), (noDialog.Disposition, noDialog.Messages.Count));
        host.Programs.Clear();
        var missing = owner.LootItem(owner.Version, "corpse", "boots", Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, ScrStop.MissingScript, 0), (missing.Disposition, missing.InsertScript!.Stop, missing.Messages.Count));
    }

    [TestMethod]
    public void Global_flags_apply_with_the_transfer_and_unmodelled_effects_refuse_it()
    {
        using var owner = Attached();
        var host = new Host(owner);
        // A flag the session admits is written with the transfer, in the same revision.
        host.Programs[Amulet] = Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 3)), C(ScrCatalogue.Action.ReturnSkipDefault));
        Assert.AreEqual(false, ReadFlag(owner, 3));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "glove", Accept, host).Disposition);
        Assert.AreEqual((true, 1L), (ReadFlag(owner, 3), owner.Version.Revision));

        // Refused with no effect: a flag of unknown value, a changed attachment, a dialog action (not admitted) and a line
        // the host cannot show.
        foreach (var (program, line) in new (ScrProgram, SessionPrintedLine?)[]
        {
            (Program(C(ScrCatalogue.Action.SetGlobalFlag, (Number, 9)), C(ScrCatalogue.Action.ReturnSkipDefault)), null),
            (Program(C(ScrCatalogue.Action.SetLocalFlag, (Number, 2)), C(ScrCatalogue.Action.ReturnSkipDefault)), null),
            (Program(C(ScrCatalogue.Action.Dialog, (Number, 10))), null),
            (Program(C(ScrCatalogue.Action.Assign, (Flag, 7), (Number, 0)), C(ScrCatalogue.Action.PrintLine, (Number, 1), (Number, 6))),
                new SessionPrintedLine(SessionPrintedLineStatus.Unsupported, string.Empty, "action field")),
        })
        {
            host.Programs[Amulet] = program;
            if (line is not null) host.Lines[(Amulet, 1)] = line;
            var result = owner.LootItem(owner.Version, "corpse", "boots", Accept, host);
            Refused(result, owner, GameSessionFailure.ItemScriptUnsupported, 1);
            Assert.IsNotNull(result.InsertScript);
        }
        CollectionAssert.AreEqual(new[] { "glove" }, Pc(owner));
        Assert.AreEqual((true, (bool?)null), (ReadFlag(owner, 7), ReadFlag(owner, 9)));
    }

    [TestMethod]
    public void An_unreadable_script_or_an_unknown_flag_refuses_the_transfer()
    {
        using var owner = Attached();
        var host = new Host(owner) { Unreadable = true };
        var unreadable = owner.PickUpItem(owner.Version, "glove", Accept, host);
        Refused(unreadable, owner, GameSessionFailure.ItemScriptUnsupported, 0);
        Assert.AreEqual(ScrStop.UnreadableScript, unreadable.InsertScript!.Stop);

        // The run reads flags from the session: flag 9 is unknown there, whatever the host would answer.
        host = new Host(owner);
        host.Programs[Amulet] = Program(C(ScrCatalogue.Action.Assign, (Local, 0), (Flag, 9)), C(ScrCatalogue.Action.ReturnSkipDefault));
        Refused(owner.PickUpItem(owner.Version, "glove", Accept, host), owner, GameSessionFailure.ItemScriptUnsupported, 0);
        Assert.AreEqual(0, Pc(owner).Length);
    }

    [TestMethod]
    public void Spilled_flag_writes_of_the_original_mode_apply_together_or_refuse()
    {
        var created = GameSessionOwner.Create(Descriptor() with { Rules = RuleMode.Original });
        using var owner = created.Owner!;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, InventoryStateTests.Seed()).Disposition);
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, WriteFlag(owner, 7, false).Disposition);
        var host = new Host(owner);
        // X10: a value of 17 written to flag 3 sets flag 3 and, four bits up, flag 7.
        host.Programs[Amulet] = Program(C(ScrCatalogue.Action.Assign, (Flag, 3), (Number, 17)), C(ScrCatalogue.Action.ReturnSkipDefault));
        var taken = owner.PickUpItem(owner.Version, "glove", Accept, host);
        Assert.AreEqual((GameSessionDisposition.Changed, 1L), (taken.Disposition, owner.Version.Revision));
        CollectionAssert.AreEqual(new[] { new ScrFlagWrite(3, true), new ScrFlagWrite(7, true) }, taken.InsertScript!.Effects.GlobalFlags.ToArray());
        Assert.AreEqual((true, true), (ReadFlag(owner, 3), ReadFlag(owner, 7)));

        // 1 + 64 also spills into flag 9, which the session does not know: refused, nothing written.
        host.Programs[Amulet] = Program(C(ScrCatalogue.Action.Assign, (Flag, 3), (Number, 65)), C(ScrCatalogue.Action.ReturnSkipDefault));
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, WriteFlag(owner, 3, false).Disposition);
        Refused(owner.LootItem(owner.Version, "corpse", "boots", Accept, host), owner, GameSessionFailure.ItemScriptUnsupported, 1);
        Assert.AreEqual((false, (bool?)null), (ReadFlag(owner, 3), ReadFlag(owner, 9)));
    }

    [TestMethod]
    public void The_script_runs_after_the_insert_check_and_a_call_back_makes_the_transfer_stale()
    {
        using var owner = Attached();
        var host = new Host(owner);
        Refused(owner.PickUpItem(owner.Version, "glove", _ => SessionInsertVerdict.TooHeavy, host), owner, GameSessionFailure.InsertRefused, 0);
        Assert.AreEqual(0, host.Asked.Count, "a refused insert check runs no script");

        host.DuringLine = () => owner.PlaceActor(owner.Version, Start with { Facing = Arcanum.Simulation.Movement.NeighborDirection.XMinus });
        var stale = owner.PickUpItem(owner.Version, "glove", Accept, host);
        Refused(stale, owner, GameSessionFailure.StaleVersion, 1);
        Assert.IsNotNull(stale.InsertScript);
        // A global flag written from the host is a change too, though it moves no session revision.
        host.DuringLine = () => WriteFlag(owner, 3, true);
        Refused(owner.PickUpItem(owner.Version, "glove", Accept, host), owner, GameSessionFailure.StaleVersion, 1);
        Assert.AreEqual(0, Pc(owner).Length);
    }

    [TestMethod]
    public void The_dialog_port_passes_the_host_to_negative_in()
    {
        using var owner = Create();
        var seed = InventoryStateTests.Seed();
        var giver = seed.Holders[0] with { Items = [.. seed.Holders[0].Items, "glove-held"] };
        var scripted = seed.Items.Single(item => item.Entity == "glove") with { Entity = "glove-held", Cell = null };
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed with { Holders = [giver, .. seed.Holders.Skip(1)], Items = [.. seed.Items, scripted] }).Disposition);
        var host = new Host(owner);
        host.Asked.Clear();
        var pc = new DialogParticipantId(1); var npc = new DialogParticipantId(2);
        var port = new SessionDialogInventoryPort(owner, pc, id => id == npc ? "virgil" : null, Accept, host);
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new GiveItemCommand(npc, pc, 2810)));
        Assert.AreEqual(("glove-held", 1), (port.LastResult!.Value.Item, port.LastResult.Value.Messages.Count));
        CollectionAssert.Contains(host.Asked, $"slot glove-held {SessionScriptPoint.InsertItem}");
        // Without the host the same IN is refused, as before.
        using var other = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, other.AttachMapInventory(Exterior, seed with { Holders = [giver, .. seed.Holders.Skip(1)], Items = [.. seed.Items, scripted] }).Disposition);
        var bare = new SessionDialogInventoryPort(other, pc, id => id == npc ? "virgil" : null, Accept);
        Assert.AreEqual(DialogCommandResult.Rejected, bare.Execute(new GiveItemCommand(npc, pc, 2810)));
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, bare.LastResult!.Value.Failure);
    }
}
