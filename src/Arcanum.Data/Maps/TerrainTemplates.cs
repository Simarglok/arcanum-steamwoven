using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Maps;

/// <summary>A sector's 16-bit terrain word from <c>terrain.tdf</c> (ARC-DATA-031, ledger TR4): base type bits 11..15,
/// second type bits 6..10, transition code bits 2..5, variant bits 0..1. A sector outside the grid reads
/// <see cref="OutsideGrid"/>.</summary>
public readonly record struct TerrainWord(ushort Value)
{
    public const ushort OutsideGrid = 0xFFFF;
    public int Base => Value >> 11 & 31;
    public int Second => Value >> 6 & 31;
    public int Transition => Value >> 2 & 15;
    public int Variant => Value & 3;
}

/// <summary>One terrain type of <c>terrain\terrain.mes</c>: its folder name and the <c>/b</c> mark (blocked for
/// world-map travel).</summary>
public sealed record TerrainType(int Number, string Name, bool Blocked);

public enum TerrainTypeStatus
{
    Ready,
    /// <summary>No base source holds <c>terrain\terrain.mes</c>; the original does not start.</summary>
    Missing,
    ReadFailed,
    ParseFailed,
    /// <summary>Message 0 is missing; the original's terrain initialisation fails.</summary>
    NoTypes,
    /// <summary>Type 0 has no <c>0.sec</c> template in the base sources; the original's terrain initialisation fails.</summary>
    FirstTypeWithoutTemplate,
    /// <summary>A <c>/b</c> mark with nothing but white space before it; the original walks before the text.</summary>
    MalformedName,
}

public sealed record TerrainTypeLoad(TerrainTypeStatus Status, TerrainTypeTable? Table, string? Detail = null);

public enum TerrainTemplateKind
{
    /// <summary><c>terrain\&lt;base&gt;\&lt;variant slot&gt;.sec</c>.</summary>
    Plain,
    /// <summary><c>terrain\&lt;from&gt; to &lt;to&gt;\&lt;slot&gt;.sec</c> from the transition slot table.</summary>
    Transition,
    /// <summary>A transition code with no slot either way: the original builds a <c>-1.sec</c> name, which no
    /// archive holds, so the sector is filled.</summary>
    NoSlot,
    /// <summary>A base or second type without a name (at or above the type count, also every sector outside the
    /// grid): the original appends a null name and faults (ledger Wilderness A6).</summary>
    UnnamedType,
}

/// <summary>The template a terrain word names. <see cref="Path"/> is the lower-case virtual path (null for
/// <see cref="TerrainTemplateKind.UnnamedType"/>); <see cref="Slot"/> the sector of the template folder the file
/// holds (null when there is none).</summary>
public sealed record TerrainTemplatePath(TerrainTemplateKind Kind, string? Path, int From, int To, WorldSectorCoordinate? Slot);

/// <summary>
/// ARC-DATA-031: the terrain types of <c>terrain\terrain.mes</c> and the template path rule, as the 1.0.7.4 executable
/// has them (ledger "Terrain templates" TR1–TR5). The file is read by an init function before the module is mounted and
/// never again, so only the base sources count (<see cref="ContentLoadPhase.GameStart"/>); the templates themselves are
/// read when a sector loads, from the whole stack.
/// </summary>
public sealed class TerrainTypeTable
{
    public const string MesPath = "terrain/terrain.mes";
    /// <summary>The original reads messages 0..31 at most.</summary>
    public const int MaxTypes = 32;
    /// <summary>Slot of each transition code as a template-folder sector id, or -1 (table 0x5B9968).</summary>
    private static readonly long[] TransitionSlots =
        [-1, 1, 2L << 26 | 1, 1L << 26 | 1, 2L << 26 | 3, 0, 2L << 26 | 2, -1, 3, 2, -1, -1, 1L << 26 | 3, -1, -1, -1];

    private readonly TerrainType[] _types;
    private TerrainTypeTable(TerrainType[] types) => _types = types;

    /// <summary>The types in number order; their count is the original's type count.</summary>
    public IReadOnlyList<TerrainType> Types => Array.AsReadOnly(_types);

    /// <summary>Reads <see cref="MesPath"/> from the first base source and checks it as the original's terrain
    /// initialisation does; <paramref name="stack"/> also answers whether type 0's first template exists.</summary>
    public static TerrainTypeLoad Open(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new(); options.Validate();
        if (ContentPhase.Candidate(stack, MesPath, ContentLoadPhase.GameStart) is not { } candidate)
            return new(TerrainTypeStatus.Missing, null, MesPath);
        var read = stack.Read(candidate);
        if (read.Status != ContentReadStatus.Read) return new(TerrainTypeStatus.ReadFailed, null, read.Status.ToString());
        var parsed = MesFile.Parse(read.Bytes, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(TerrainTypeStatus.ParseFailed, null, parsed.Status.ToString());
        return Create(parsed.File!, path => ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not null);
    }

    /// <summary>The table of a parsed <c>terrain.mes</c>; <paramref name="baseTemplateExists"/> answers whether a
    /// virtual path exists in the base sources (the check of type 0's first template).</summary>
    [OriginalFunction(0x4E8EA0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4E8F40, OriginalFunctionScope.Partial)]
    public static TerrainTypeLoad Create(MesFile file, Func<string, bool> baseTemplateExists)
    {
        ArgumentNullException.ThrowIfNull(file); ArgumentNullException.ThrowIfNull(baseTemplateExists);
        var types = new List<TerrainType>();
        for (var number = 0; number < MaxTypes; number++)
        {
            if (file.Find(number) is not { } message) break;
            var name = Encoding.Latin1.GetString(message.Bytes.Span);
            var mark = name.IndexOf("/b", StringComparison.Ordinal);
            if (mark >= 0)
            {
                var end = mark - 1;
                while (end >= 0 && IsCSpace(name[end])) end--;
                if (end < 0) return new(TerrainTypeStatus.MalformedName, null, $"message {number}");
                name = name[..(end + 1)];
            }
            types.Add(new(number, name, mark >= 0));
        }
        if (types.Count == 0) return new(TerrainTypeStatus.NoTypes, null);
        if (!baseTemplateExists(PlainPath(types[0].Name, 0)))
            return new(TerrainTypeStatus.FirstTypeWithoutTemplate, null, PlainPath(types[0].Name, 0));
        return new(TerrainTypeStatus.Ready, new(types.ToArray()));
    }

    /// <summary>The folder name of a type, or null at or above the type count.</summary>
    [OriginalFunction(0x4E8B20, OriginalFunctionScope.Full)]
    public string? Name(int type) => type >= 0 && type < _types.Length ? _types[type].Name : null;

    /// <summary>The template a terrain word names (ledger TR5).</summary>
    [OriginalFunction(0x4E84C0, OriginalFunctionScope.Partial)]
    public TerrainTemplatePath Resolve(TerrainWord word)
    {
        int baseType = word.Base, second = word.Second, code = word.Transition;
        if (baseType == second || code == 0)
        {
            var name = Name(baseType);
            return name is null ? new(TerrainTemplateKind.UnnamedType, null, baseType, baseType, null)
                : new(TerrainTemplateKind.Plain, PlainPath(name, word.Variant), baseType, baseType, VariantSlot(word.Variant));
        }
        int from = baseType, to = second;
        var slot = TransitionSlots[code];
        if (slot == -1) { from = second; to = baseType; slot = TransitionSlots[15 - code]; }
        if (Name(from) is not { } fromName || Name(to) is not { } toName) return new(TerrainTemplateKind.UnnamedType, null, from, to, null);
        var folder = $"terrain/{Lower(fromName)} to {Lower(toName)}";
        return slot == -1 ? new(TerrainTemplateKind.NoSlot, $"{folder}/-1.sec", from, to, null)
            : new(TerrainTemplateKind.Transition, $"{folder}/{slot}.sec", from, to, WorldSectorCoordinate.TryFromId(slot, out var c) ? c : null);
    }

    /// <summary>The template-folder sector of a plain variant: (variant % 2, variant / 2).</summary>
    [OriginalFunction(0x4E8E60, OriginalFunctionScope.Full)]
    public static WorldSectorCoordinate VariantSlot(int variant) => new(variant % 2, variant / 2);

    private static string PlainPath(string name, int variant) => $"terrain/{Lower(name)}/{VariantSlot(variant).Id}.sec";
    private static string Lower(string name) => ContentMountPlan.AsciiLower(name);
    private static bool IsCSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';
}
