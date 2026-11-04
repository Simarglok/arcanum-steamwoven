using System.Text;

namespace Arcanum.Executable;

/// <summary>
/// ARC-DEV-067: what a run of <see cref="X86Machine"/> reached, when <see cref="X86Machine.Census"/> is set: every called
/// function (the entry of <see cref="X86Machine.Call"/> and every call target) with its call count, every hook reached
/// by name, every distinct instruction address and the count of each instruction kind (a mnemonic, with <c>rep</c>/<c>repe</c>/<c>repne</c>
/// for a repeated string instruction). Recording costs time; leave it off for production runs.
/// </summary>
public sealed class MachineCensus
{
    private readonly Dictionary<string, long> kinds = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, long> functions = [];
    private readonly Dictionary<string, long> hooks = new(StringComparer.Ordinal);
    private readonly HashSet<uint> addresses = [];

    public IReadOnlyDictionary<string, long> InstructionKinds => kinds;
    public IReadOnlyDictionary<uint, long> Functions => functions;
    public IReadOnlyDictionary<string, long> Hooks => hooks;
    public int DistinctInstructionAddresses => addresses.Count;
    public long Instructions { get; private set; }

    public bool Executed(uint address) => addresses.Contains(address);

    internal void Instruction(uint address, string kind)
    {
        Instructions++;
        addresses.Add(address);
        kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
    }

    internal void Function(uint address) => functions[address] = functions.GetValueOrDefault(address) + 1;

    internal void Hook(string name) => hooks[name] = hooks.GetValueOrDefault(name) + 1;

    /// <summary>A stable text listing: functions by address, hooks and kinds by name.</summary>
    public string Report()
    {
        var text = new StringBuilder();
        text.Append("instructions ").Append(Instructions).Append(", distinct addresses ").Append(addresses.Count)
            .Append(", functions ").Append(functions.Count).Append(", kinds ").Append(kinds.Count).Append('\n');
        foreach (var (address, count) in functions.OrderBy(f => f.Key)) text.Append($"function 0x{address:X8} {count}\n");
        foreach (var (name, count) in hooks.OrderBy(h => h.Key, StringComparer.Ordinal)) text.Append($"hook {name} {count}\n");
        foreach (var (kind, count) in kinds.OrderBy(k => k.Key, StringComparer.Ordinal)) text.Append($"kind {kind} {count}\n");
        return text.ToString();
    }
}
