using System.Buffers.Binary;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>Synthetic SCR programs and a recording host for the dispatcher tests.</summary>
internal static class ScrTestPrograms
{
    internal const byte Counter = 0, Local = 2, Number = 3, GlobalFlag = 4;
    /// <summary>Critter disband (22), an action outside every set the interpreter runs (float line, 23, until ARC-SCRIPT-050).</summary>
    internal const int CritterDisband = 22;

    internal readonly record struct Clause(int Type, (byte Kind, int Value)[] Operands);
    internal static Clause C(int type, params (byte, int)[] operands) => new(type, operands);
    internal static (Clause, Clause, Clause) L(Clause condition, Clause onTrue, Clause onFalse) => (condition, onTrue, onFalse);
    internal static (Clause, Clause, Clause) Do(Clause action) => (C(0), action, C(0));
    internal static (byte, int) N(int value) => (Number, value);

    internal static ScrProgram Program(params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines) => Program(0, lines);

    internal static ScrProgram Program(uint fileFlags, params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), fileFlags);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
            foreach (var (offset, clause) in new[] { (0, lines[line].Condition), (44, lines[line].OnTrue), (88, lines[line].OnFalse) })
            {
                var slot = bytes.AsSpan(64 + line * 132 + offset, 44);
                BinaryPrimitives.WriteInt32LittleEndian(slot, clause.Type);
                for (var i = 0; i < clause.Operands.Length; i++) { slot[4 + i] = clause.Operands[i].Kind; BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], clause.Operands[i].Value); }
            }
        var read = ScrProgram.Read(bytes);
        Assert.AreEqual(ScrReadStatus.Read, read.Status);
        return read.Program!;
    }

    internal static readonly ScrObjectRef Npc = new(1, "npc.1"), Pc = new(1, "pc"), Chest = new(1, "obj.chest");
}

/// <summary>A host with fixed answers that records every question it was asked.</summary>
internal sealed class FakeHost : IScrHost
{
    public Dictionary<(ScrObjectRef, int), ScrSlot> Slots { get; } = new();
    public Dictionary<int, ScrScriptLookup> Scripts { get; } = new();
    public HashSet<int> Flags { get; } = new();
    public int Story { get; set; }
    public bool Generator { get; set; }
    public ScrDialogState Dialog { get; set; }
    public ScrTrapCheck Trap { get; set; }
    public bool TrapObject { get; set; }
    public List<string> Calls { get; } = new();

    public void Attach(ScrObjectRef attachee, int point, int script, ScrProgram program, uint localFlags = 0, uint counters = 0)
    {
        Slots[(attachee, point)] = new(script, localFlags, counters);
        Scripts[script] = ScrScriptLookup.Of(program);
    }

    public ScrSlot? Slot(ScrObjectRef attachee, int point) { Calls.Add($"slot {point}"); return Slots.TryGetValue((attachee, point), out var s) ? s : null; }
    public ScrScriptLookup Script(int number) { Calls.Add($"script {number}"); return Scripts.TryGetValue(number, out var s) ? s : ScrScriptLookup.Missing; }
    public bool GlobalFlag(int index) => Flags.Contains(index);
    public int StoryState() => Story;
    public bool IsMonsterGenerator(ScrObjectRef attachee) { Calls.Add("generator"); return Generator; }
    public ScrDialogState DialogState(ScrObjectRef attachee) { Calls.Add("dialog"); return Dialog; }
    public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) { Calls.Add("trap"); return Trap; }
    public bool IsTrapObject(ScrObjectRef attachee) { Calls.Add("trap object"); return TrapObject; }
    public ScrObjectRef? PlayerRef { get; set; } = ScrTestPrograms.Pc;
    public Dictionary<ScrObjectRef, ScrObjectRef> Leaders { get; } = new();
    public HashSet<ScrObjectRef> Jilted { get; } = new();
    public HashSet<ScrObjectRef> Pcs { get; } = new() { ScrTestPrograms.Pc };
    public ScrObjectRef? Player() => PlayerRef;
    public ScrObjectRef? Leader(ScrObjectRef critter) => Leaders.TryGetValue(critter, out var leader) ? leader : null;
    public bool IsJilted(ScrObjectRef critter) => Jilted.Contains(critter);
    public bool IsPc(ScrObjectRef critter) => Pcs.Contains(critter);
}
