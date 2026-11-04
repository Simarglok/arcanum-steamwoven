using System.Buffers.Binary;
using System.Numerics;

namespace Arcanum.Executable;

public sealed partial class X86Machine
{
    private static readonly string[] AluKinds = ["add", "or", "adc", "sbb", "and", "sub", "xor", "cmp"];
    private static readonly string[] ShiftKinds = ["rol", "ror", "rcl", "rcr", "shl", "shr", "shl", "sar"];
    private static readonly string[] ConditionNames = ["o", "no", "b", "ae", "e", "ne", "be", "a", "s", "ns", "p", "np", "l", "ge", "le", "g"];
    private static readonly string[] JumpKinds = ConditionNames.Select(c => "j" + c).ToArray();
    private static readonly string[] SetKinds = ConditionNames.Select(c => "set" + c).ToArray();
    private static readonly string[] MoveKinds = ConditionNames.Select(c => "cmov" + c).ToArray();

    // Decoding state of the current instruction.
    private byte[]? fetchData;
    private uint fetchPage = uint.MaxValue;
    private int mod, reg, rm;
    private uint eaOffset, segmentBase;
    private string kind = "";

    private uint EffectiveAddress => eaOffset + segmentBase;

    private void Step()
    {
        instructionStart = eip;
        segmentBase = 0;
        var o16 = false;
        var rep = 0;
        byte op;
        for (var prefixes = 0; ; prefixes++)
        {
            if (prefixes == 14) throw Unsupported("more than 14 prefixes");
            op = Fetch8();
            if (op == 0x66) o16 = true;
            else if (op == 0xF3) rep = 1;
            else if (op == 0xF2) rep = 2;
            else if (op is 0x26 or 0x2E or 0x36 or 0x3E) segmentBase = 0;
            else if (op == 0x64)
            {
                if (Options.FsBase == 0) throw Unsupported("an FS override without an FS base");
                segmentBase = Options.FsBase;
            }
            else if (op == 0xF0) { } // lock: one interpreter thread, no effect
            else break;
        }
        var v = o16 ? 2 : 4;

        switch (op)
        {
            case < 0x40 when (op & 7) < 6:
                ExecuteAlu(op, v);
                break;
            case 0x0F:
                ExecuteTwoByte(o16, v);
                break;
            case >= 0x40 and <= 0x47:
                kind = "inc";
                SetReg(op & 7, v, Inc(GetReg(op & 7, v), v));
                break;
            case >= 0x48 and <= 0x4F:
                kind = "dec";
                SetReg(op & 7, v, Dec(GetReg(op & 7, v), v));
                break;
            case >= 0x50 and <= 0x57:
                kind = "push";
                Push(GetReg(op & 7, v), v);
                break;
            case >= 0x58 and <= 0x5F:
                kind = "pop";
                SetReg(op & 7, v, Pop(v));
                break;
            case 0x60:
                if (o16) throw Unsupported("pushaw");
                kind = "pushal";
                {
                    var sp = r[EspIndex];
                    for (var i = 0; i < 8; i++) Push(i == EspIndex ? sp : r[i], 4);
                }
                break;
            case 0x61:
                if (o16) throw Unsupported("popaw");
                kind = "popal";
                for (var i = 7; i >= 0; i--)
                {
                    var value = Pop(4);
                    if (i != EspIndex) r[i] = value;
                }
                break;
            case 0x68:
                kind = "push";
                Push(FetchImmediate(v), v);
                break;
            case 0x69:
            case 0x6B:
                kind = "imul";
                ModRM();
                {
                    var source = ReadRM(v);
                    var immediate = op == 0x69 ? FetchImmediate(v) : (uint)(sbyte)Fetch8();
                    SetReg(reg, v, MultiplySigned(source, immediate, v));
                }
                break;
            case 0x6A:
                kind = "push";
                Push((uint)(sbyte)Fetch8(), v);
                break;
            case >= 0x70 and <= 0x7F:
                {
                    if (o16) throw Unsupported("a 16-bit jump");
                    kind = JumpKinds[op & 15];
                    var displacement = (uint)(sbyte)Fetch8();
                    if (Condition(op & 15)) Transfer(eip + displacement);
                }
                break;
            case 0x80:
            case 0x81:
            case 0x82:
            case 0x83:
                {
                    var size = op == 0x81 || op == 0x83 ? v : 1;
                    ModRM();
                    kind = AluKinds[reg];
                    var immediate = op == 0x81 ? FetchImmediate(v) : op == 0x83 ? (uint)(sbyte)Fetch8() & Mask(v) : Fetch8();
                    var result = Alu(reg, ReadRM(size), immediate, size);
                    if (reg != 7) WriteRM(size, result);
                }
                break;
            case 0x84:
            case 0x85:
                {
                    kind = "test";
                    var size = op == 0x84 ? 1 : v;
                    ModRM();
                    Alu(4, ReadRM(size), GetReg(reg, size), size);
                }
                break;
            case 0x86:
            case 0x87:
                {
                    kind = "xchg";
                    var size = op == 0x86 ? 1 : v;
                    ModRM();
                    var memory = ReadRM(size);
                    WriteRM(size, GetReg(reg, size));
                    SetReg(reg, size, memory);
                }
                break;
            case 0x88:
                kind = "mov";
                ModRM();
                WriteRM(1, GetReg(reg, 1));
                break;
            case 0x89:
                kind = "mov";
                ModRM();
                WriteRM(v, GetReg(reg, v));
                break;
            case 0x8A:
                kind = "mov";
                ModRM();
                SetReg(reg, 1, ReadRM(1));
                break;
            case 0x8B:
                kind = "mov";
                ModRM();
                SetReg(reg, v, ReadRM(v));
                break;
            case 0x8D:
                kind = "lea";
                ModRM();
                if (mod == 3) throw Unsupported("lea of a register");
                SetReg(reg, v, eaOffset);
                break;
            case 0x8F:
                {
                    kind = "pop";
                    // The destination's address is computed after ESP is incremented.
                    var value = Pop(v);
                    ModRM();
                    if (reg != 0) throw Unsupported("8F /" + reg);
                    WriteRM(v, value);
                }
                break;
            case 0x90:
                kind = "nop";
                break;
            case >= 0x91 and <= 0x97:
                {
                    kind = "xchg";
                    var other = GetReg(op & 7, v);
                    SetReg(op & 7, v, GetReg(EaxIndex, v));
                    SetReg(EaxIndex, v, other);
                }
                break;
            case 0x98:
                if (o16)
                {
                    kind = "cbw";
                    SetReg(EaxIndex, 2, (uint)(sbyte)r[EaxIndex]);
                }
                else
                {
                    kind = "cwde";
                    r[EaxIndex] = (uint)(short)r[EaxIndex];
                }
                break;
            case 0x99:
                if (o16)
                {
                    kind = "cwd";
                    SetReg(EdxIndex, 2, (r[EaxIndex] & 0x8000) != 0 ? 0xFFFFu : 0);
                }
                else
                {
                    kind = "cdq";
                    r[EdxIndex] = (r[EaxIndex] & 0x8000_0000) != 0 ? 0xFFFF_FFFFu : 0;
                }
                break;
            case 0x9C:
                if (o16) throw Unsupported("pushfw");
                kind = "pushfd";
                Push(Eflags, 4);
                break;
            case 0x9D:
                if (o16) throw Unsupported("popfw");
                kind = "popfd";
                Eflags = Pop(4);
                break;
            case 0x9E:
                kind = "sahf";
                {
                    var ah = r[EaxIndex] >> 8;
                    cf = (ah & 0x1) != 0;
                    pf = (ah & 0x4) != 0;
                    af = (ah & 0x10) != 0;
                    zf = (ah & 0x40) != 0;
                    sf = (ah & 0x80) != 0;
                }
                break;
            case 0x9F:
                kind = "lahf";
                SetReg(4, 1, Eflags & 0xD5 | 0x2);
                break;
            case 0xA0:
                kind = "mov";
                SetReg(EaxIndex, 1, Memory.ReadU8(Fetch32() + segmentBase));
                break;
            case 0xA1:
                kind = "mov";
                SetReg(EaxIndex, v, ReadMemory(Fetch32() + segmentBase, v));
                break;
            case 0xA2:
                kind = "mov";
                Memory.WriteU8(Fetch32() + segmentBase, (byte)r[EaxIndex]);
                break;
            case 0xA3:
                kind = "mov";
                WriteMemory(Fetch32() + segmentBase, v, r[EaxIndex]);
                break;
            case >= 0xA4 and <= 0xA7:
            case >= 0xAA and <= 0xAF:
                ExecuteString(op, rep, v);
                break;
            case 0xA8:
                kind = "test";
                Alu(4, r[EaxIndex], Fetch8(), 1);
                break;
            case 0xA9:
                kind = "test";
                Alu(4, r[EaxIndex], FetchImmediate(v), v);
                break;
            case >= 0xB0 and <= 0xB7:
                kind = "mov";
                SetReg(op & 7, 1, Fetch8());
                break;
            case >= 0xB8 and <= 0xBF:
                kind = "mov";
                SetReg(op & 7, v, FetchImmediate(v));
                break;
            case 0xC0:
            case 0xC1:
            case 0xD0:
            case 0xD1:
            case 0xD2:
            case 0xD3:
                {
                    var size = (op & 1) == 0 ? 1 : v;
                    ModRM();
                    kind = ShiftKinds[reg];
                    var count = (op <= 0xC1 ? Fetch8() : op <= 0xD1 ? 1u : r[EcxIndex]) & 0x1F;
                    var value = ReadRM(size); // read even for a count of 0, as the CPU does (it may fault)
                    if (count != 0) WriteRM(size, Shift(reg, value, count, size));
                }
                break;
            case 0xC2:
            case 0xC3:
                {
                    if (o16) throw Unsupported("a 16-bit return");
                    kind = "ret";
                    var release = op == 0xC2 ? Fetch16() : 0u;
                    var target = Pop(4);
                    r[EspIndex] += release;
                    Transfer(target);
                }
                break;
            case 0xC6:
            case 0xC7:
                {
                    kind = "mov";
                    var size = op == 0xC6 ? 1 : v;
                    ModRM();
                    if (reg != 0) throw Unsupported($"{op:X2} /{reg}");
                    WriteRM(size, size == 1 ? Fetch8() : FetchImmediate(v));
                }
                break;
            case 0xC8:
                {
                    if (o16) throw Unsupported("a 16-bit enter");
                    kind = "enter";
                    var frame = Fetch16();
                    var level = Fetch8() & 0x1F;
                    if (level != 0) throw Unsupported($"enter with nesting level {level}");
                    Push(r[EbpIndex], 4);
                    r[EbpIndex] = r[EspIndex];
                    r[EspIndex] -= frame;
                }
                break;
            case 0xC9:
                if (o16) throw Unsupported("a 16-bit leave");
                kind = "leave";
                r[EspIndex] = r[EbpIndex];
                r[EbpIndex] = Pop(4);
                break;
            case 0xCC:
                throw Fault(MachineFaultKind.Breakpoint, "int3");
            case 0xD7:
                kind = "xlat";
                SetReg(EaxIndex, 1, Memory.ReadU8(r[EbxIndex] + (r[EaxIndex] & 0xFF) + segmentBase));
                break;
            case >= 0xE0 and <= 0xE3:
                {
                    if (o16) throw Unsupported("a 16-bit loop");
                    var displacement = (uint)(sbyte)Fetch8();
                    bool taken;
                    if (op == 0xE3)
                    {
                        kind = "jecxz";
                        taken = r[EcxIndex] == 0;
                    }
                    else
                    {
                        r[EcxIndex]--;
                        kind = op == 0xE2 ? "loop" : op == 0xE1 ? "loope" : "loopne";
                        taken = r[EcxIndex] != 0 && (op == 0xE2 || (op == 0xE1) == zf);
                    }
                    if (taken) Transfer(eip + displacement);
                }
                break;
            case 0xE8:
                {
                    if (o16) throw Unsupported("a 16-bit call");
                    kind = "call";
                    var displacement = Fetch32();
                    CallTo(eip + displacement);
                }
                break;
            case 0xE9:
                if (o16) throw Unsupported("a 16-bit jump");
                kind = "jmp";
                {
                    var displacement = Fetch32();
                    Transfer(eip + displacement);
                }
                break;
            case 0xEB:
                if (o16) throw Unsupported("a 16-bit jump");
                kind = "jmp";
                {
                    var displacement = (uint)(sbyte)Fetch8();
                    Transfer(eip + displacement);
                }
                break;
            case 0xF5:
                kind = "cmc";
                cf = !cf;
                break;
            case 0xF6:
            case 0xF7:
                ExecuteGroup3(op == 0xF6 ? 1 : v, v);
                break;
            case 0xF8:
                kind = "clc";
                cf = false;
                break;
            case 0xF9:
                kind = "stc";
                cf = true;
                break;
            case 0xFC:
                kind = "cld";
                df = false;
                break;
            case 0xFD:
                kind = "std";
                df = true;
                break;
            case 0xFE:
                ModRM();
                if (reg == 0) { kind = "inc"; WriteRM(1, Inc(ReadRM(1), 1)); }
                else if (reg == 1) { kind = "dec"; WriteRM(1, Dec(ReadRM(1), 1)); }
                else throw Unsupported("FE /" + reg);
                break;
            case 0xFF:
                ExecuteGroup5(o16, v);
                break;
            case 0x9B:
            case >= 0xD8 and <= 0xDF:
                throw Unsupported("floating point");
            default:
                throw Unsupported($"opcode {op:X2}");
        }
        Census?.Instruction(instructionStart, kind);
    }

    private void ExecuteAlu(byte op, int v)
    {
        var operation = op >> 3;
        kind = AluKinds[operation];
        switch (op & 7)
        {
            case 0:
                {
                    ModRM();
                    var result = Alu(operation, ReadRM(1), GetReg(reg, 1), 1);
                    if (operation != 7) WriteRM(1, result);
                }
                break;
            case 1:
                {
                    ModRM();
                    var result = Alu(operation, ReadRM(v), GetReg(reg, v), v);
                    if (operation != 7) WriteRM(v, result);
                }
                break;
            case 2:
                {
                    ModRM();
                    var result = Alu(operation, GetReg(reg, 1), ReadRM(1), 1);
                    if (operation != 7) SetReg(reg, 1, result);
                }
                break;
            case 3:
                {
                    ModRM();
                    var result = Alu(operation, GetReg(reg, v), ReadRM(v), v);
                    if (operation != 7) SetReg(reg, v, result);
                }
                break;
            case 4:
                {
                    var result = Alu(operation, r[EaxIndex], Fetch8(), 1);
                    if (operation != 7) SetReg(EaxIndex, 1, result);
                }
                break;
            default:
                {
                    var result = Alu(operation, r[EaxIndex], FetchImmediate(v), v);
                    if (operation != 7) SetReg(EaxIndex, v, result);
                }
                break;
        }
    }

    private void ExecuteTwoByte(bool o16, int v)
    {
        var op = Fetch8();
        switch (op)
        {
            case 0x1F:
                kind = "nop";
                ModRM();
                break;
            case >= 0x40 and <= 0x4F:
                {
                    kind = MoveKinds[op & 15];
                    ModRM();
                    var value = ReadRM(v);
                    if (Condition(op & 15)) SetReg(reg, v, value);
                }
                break;
            case >= 0x80 and <= 0x8F:
                {
                    if (o16) throw Unsupported("a 16-bit jump");
                    kind = JumpKinds[op & 15];
                    var displacement = Fetch32();
                    if (Condition(op & 15)) Transfer(eip + displacement);
                }
                break;
            case >= 0x90 and <= 0x9F:
                kind = SetKinds[op & 15];
                ModRM();
                WriteRM(1, Condition(op & 15) ? 1u : 0u);
                break;
            case 0xA3:
            case 0xAB:
            case 0xB3:
            case 0xBB:
                ModRM();
                BitTest(op == 0xA3 ? 4 : op == 0xAB ? 5 : op == 0xB3 ? 6 : 7, GetReg(reg, v), v, true);
                break;
            case 0xBA:
                ModRM();
                if (reg < 4) throw Unsupported("0F BA /" + reg);
                BitTest(reg, Fetch8(), v, false);
                break;
            case 0xA4:
            case 0xA5:
            case 0xAC:
            case 0xAD:
                {
                    if (o16) throw Unsupported("a 16-bit double shift");
                    var left = op <= 0xA5;
                    kind = left ? "shld" : "shrd";
                    ModRM();
                    var count = ((op & 1) == 0 ? Fetch8() : r[EcxIndex]) & 0x1F;
                    var value = ReadRM(4);
                    if (count != 0) WriteRM(4, DoubleShift(left, value, GetReg(reg, 4), (int)count));
                }
                break;
            case 0xAF:
                kind = "imul";
                ModRM();
                SetReg(reg, v, MultiplySigned(GetReg(reg, v), ReadRM(v), v));
                break;
            case 0xB6:
                kind = "movzx";
                ModRM();
                SetReg(reg, v, ReadRM(1));
                break;
            case 0xB7:
                kind = "movzx";
                ModRM();
                SetReg(reg, v, ReadRM(2));
                break;
            case 0xBE:
                kind = "movsx";
                ModRM();
                SetReg(reg, v, (uint)(sbyte)ReadRM(1));
                break;
            case 0xBF:
                kind = "movsx";
                ModRM();
                SetReg(reg, v, (uint)(short)ReadRM(2));
                break;
            case 0xBC:
            case 0xBD:
                {
                    kind = op == 0xBC ? "bsf" : "bsr";
                    ModRM();
                    var source = ReadRM(v) & Mask(v);
                    zf = source == 0;
                    if (source != 0) SetReg(reg, v, op == 0xBC ? (uint)BitOperations.TrailingZeroCount(source) : (uint)(31 - BitOperations.LeadingZeroCount(source)));
                }
                break;
            case >= 0xC8 and <= 0xCF:
                if (o16) throw Unsupported("a 16-bit bswap");
                kind = "bswap";
                r[op & 7] = BinaryPrimitives.ReverseEndianness(r[op & 7]);
                break;
            default:
                throw Unsupported($"opcode 0F {op:X2}");
        }
    }

    private void ExecuteGroup3(int size, int v)
    {
        ModRM();
        switch (reg)
        {
            case 0:
            case 1:
                kind = "test";
                {
                    var value = ReadRM(size);
                    Alu(4, value, size == 1 ? Fetch8() : FetchImmediate(v), size);
                }
                break;
            case 2:
                kind = "not";
                WriteRM(size, ~ReadRM(size) & Mask(size));
                break;
            case 3:
                kind = "neg";
                WriteRM(size, Alu(5, 0, ReadRM(size), size));
                break;
            case 4:
                kind = "mul";
                MultiplyAccumulator(ReadRM(size), size, false);
                break;
            case 5:
                kind = "imul";
                MultiplyAccumulator(ReadRM(size), size, true);
                break;
            case 6:
                kind = "div";
                Divide(ReadRM(size), size, false);
                break;
            default:
                kind = "idiv";
                Divide(ReadRM(size), size, true);
                break;
        }
    }

    private void ExecuteGroup5(bool o16, int v)
    {
        ModRM();
        switch (reg)
        {
            case 0:
                kind = "inc";
                WriteRM(v, Inc(ReadRM(v), v));
                break;
            case 1:
                kind = "dec";
                WriteRM(v, Dec(ReadRM(v), v));
                break;
            case 2:
                if (o16) throw Unsupported("a 16-bit call");
                kind = "call";
                CallTo(ReadRM(4));
                break;
            case 4:
                if (o16) throw Unsupported("a 16-bit jump");
                kind = "jmp";
                Transfer(ReadRM(4));
                break;
            case 6:
                kind = "push";
                Push(ReadRM(v), v);
                break;
            default:
                throw Unsupported("FF /" + reg);
        }
    }

    private void ExecuteString(byte op, int rep, int v)
    {
        var size = (op & 1) == 0 ? 1 : v;
        var delta = df ? (uint)-size : (uint)size;
        var compares = op is 0xA6 or 0xA7 or 0xAE or 0xAF;
        var name = op switch { 0xA4 or 0xA5 => "movs", 0xA6 or 0xA7 => "cmps", 0xAA or 0xAB => "stos", 0xAC or 0xAD => "lods", _ => "scas" };
        kind = rep == 0 ? name : !compares ? "rep " + name : rep == 1 ? "repe " + name : "repne " + name;
        if (rep == 0)
        {
            StringOnce(op, size, delta);
            return;
        }
        while (r[EcxIndex] != 0)
        {
            if (steps >= stepLimit) throw Fault(MachineFaultKind.InstructionLimit, $"more than {stepLimit} steps");
            steps++;
            StringOnce(op, size, delta);
            r[EcxIndex]--;
            if (compares && (rep == 1 ? !zf : zf)) break;
        }
    }

    private void StringOnce(byte op, int size, uint delta)
    {
        switch (op)
        {
            case 0xA4:
            case 0xA5:
                WriteMemory(r[EdiIndex], size, ReadMemory(r[EsiIndex] + segmentBase, size));
                r[EsiIndex] += delta;
                r[EdiIndex] += delta;
                break;
            case 0xA6:
            case 0xA7:
                Alu(7, ReadMemory(r[EsiIndex] + segmentBase, size), ReadMemory(r[EdiIndex], size), size);
                r[EsiIndex] += delta;
                r[EdiIndex] += delta;
                break;
            case 0xAA:
            case 0xAB:
                WriteMemory(r[EdiIndex], size, r[EaxIndex]);
                r[EdiIndex] += delta;
                break;
            case 0xAC:
            case 0xAD:
                SetReg(EaxIndex, size, ReadMemory(r[EsiIndex] + segmentBase, size));
                r[EsiIndex] += delta;
                break;
            default:
                Alu(7, r[EaxIndex], ReadMemory(r[EdiIndex], size), size);
                r[EdiIndex] += delta;
                break;
        }
    }

    private void CallTo(uint target)
    {
        Push(eip, 4);
        Census?.Function(target);
        Transfer(target);
    }

    private void BitTest(int operation, uint offset, int size, bool registerOffset)
    {
        kind = operation switch { 4 => "bt", 5 => "bts", 6 => "btr", _ => "btc" };
        var bits = size * 8;
        uint value, address = 0;
        int bit;
        if (mod == 3)
        {
            bit = (int)(offset & (uint)(bits - 1));
            value = GetReg(rm, size);
        }
        else
        {
            if (registerOffset)
            {
                // A register offset reaches past the operand: the address moves by whole operands (signed).
                var signed = size == 4 ? (int)offset : (short)offset;
                address = EffectiveAddress + (uint)((signed >> (size == 4 ? 5 : 4)) * size);
                bit = signed & (bits - 1);
            }
            else
            {
                address = EffectiveAddress;
                bit = (int)(offset & (uint)(bits - 1));
            }
            value = ReadMemory(address, size);
        }
        cf = ((value >> bit) & 1) != 0;
        if (operation == 4) return;
        var changed = operation switch { 5 => value | 1u << bit, 6 => value & ~(1u << bit), _ => value ^ 1u << bit };
        if (mod == 3) SetReg(rm, size, changed);
        else WriteMemory(address, size, changed);
    }

    // ---- fetching, operands, stack -------------------------------------------------------------------------------

    private byte Fetch8()
    {
        var address = eip;
        if (address >> MachineMemory.PageBits != fetchPage)
        {
            fetchData = Memory.FetchPage(address);
            fetchPage = address >> MachineMemory.PageBits;
        }
        eip = address + 1;
        return fetchData![address & (MachineMemory.PageSize - 1)];
    }

    private uint Fetch16() => Fetch8() | (uint)Fetch8() << 8;

    private uint Fetch32() => Fetch8() | (uint)Fetch8() << 8 | (uint)Fetch8() << 16 | (uint)Fetch8() << 24;

    private uint FetchImmediate(int size) => size == 2 ? Fetch16() : Fetch32();

    private void ModRM()
    {
        var modrm = Fetch8();
        mod = modrm >> 6;
        reg = (modrm >> 3) & 7;
        rm = modrm & 7;
        if (mod == 3) return;
        uint address;
        if (rm == 4)
        {
            var sib = Fetch8();
            var scale = sib >> 6;
            var index = (sib >> 3) & 7;
            var baseRegister = sib & 7;
            address = index == 4 ? 0 : r[index] << scale;
            address += baseRegister == 5 && mod == 0 ? Fetch32() : r[baseRegister];
        }
        else if (rm == 5 && mod == 0)
        {
            address = Fetch32();
        }
        else
        {
            address = r[rm];
        }
        if (mod == 1) address += (uint)(sbyte)Fetch8();
        else if (mod == 2) address += Fetch32();
        eaOffset = address;
    }

    private uint ReadRM(int size) => mod == 3 ? GetReg(rm, size) : ReadMemory(EffectiveAddress, size);

    private void WriteRM(int size, uint value)
    {
        if (mod == 3) SetReg(rm, size, value);
        else WriteMemory(EffectiveAddress, size, value);
    }

    private uint GetReg(int index, int size) => size switch
    {
        4 => r[index],
        2 => r[index] & 0xFFFF,
        _ => index < 4 ? r[index] & 0xFF : (r[index - 4] >> 8) & 0xFF,
    };

    private void SetReg(int index, int size, uint value)
    {
        switch (size)
        {
            case 4:
                r[index] = value;
                break;
            case 2:
                r[index] = (r[index] & 0xFFFF_0000) | (value & 0xFFFF);
                break;
            default:
                if (index < 4) r[index] = (r[index] & ~0xFFu) | (value & 0xFF);
                else r[index - 4] = (r[index - 4] & ~0xFF00u) | (value & 0xFF) << 8;
                break;
        }
    }

    private uint ReadMemory(uint address, int size) => size switch
    {
        4 => Memory.ReadU32(address),
        2 => Memory.ReadU16(address),
        _ => Memory.ReadU8(address),
    };

    private void WriteMemory(uint address, int size, uint value)
    {
        switch (size)
        {
            case 4:
                Memory.WriteU32(address, value);
                break;
            case 2:
                Memory.WriteU16(address, (ushort)value);
                break;
            default:
                Memory.WriteU8(address, (byte)value);
                break;
        }
    }

    private void Push(uint value, int size)
    {
        var sp = r[EspIndex] - (uint)size;
        WriteMemory(sp, size, value);
        r[EspIndex] = sp;
    }

    private uint Pop(int size)
    {
        var value = ReadMemory(r[EspIndex], size);
        r[EspIndex] += (uint)size;
        return value;
    }

    private MachineFaultException Unsupported(string what)
    {
        Span<byte> bytes = stackalloc byte[8];
        var length = 0;
        for (; length < bytes.Length && instructionStart + (uint)length < eip + 2; length++)
        {
            if (!Memory.IsMapped(instructionStart + (uint)length)) break;
            Memory.Peek(instructionStart + (uint)length, bytes.Slice(length, 1));
        }
        return Fault(MachineFaultKind.UnsupportedInstruction, $"{what} (bytes {Convert.ToHexString(bytes[..length])})");
    }
}
