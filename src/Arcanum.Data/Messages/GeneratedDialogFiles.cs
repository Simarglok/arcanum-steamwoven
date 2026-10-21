using Arcanum.Data.Content;

namespace Arcanum.Data.Messages;

/// <summary>
/// The 32 generated-dialog files in the original's handle order (name table 0x5A063C). In each name the first letter is the
/// speaker's gender and the second the listener's; "pc" files are spoken by the PC to an NPC of the named gender.
/// </summary>
public enum GeneratedDialogFile
{
    PcToMale, PcToFemale,
    ClassMaleToMale, ClassMaleToFemale, ClassFemaleToFemale, ClassFemaleToMale,
    ClassPcToMale, ClassPcToFemale,
    RaceMaleToMale, RaceMaleToFemale, RaceFemaleToFemale, RaceFemaleToMale,
    NpcMaleToMale, NpcMaleToFemale, NpcFemaleToFemale, NpcFemaleToMale,
    DumbPcToMale, DumbPcToFemale,
    ClassDumbPcToMale, ClassDumbPcToFemale,
    ClassDumbMaleToMale, ClassDumbMaleToFemale, ClassDumbFemaleToFemale, ClassDumbFemaleToMale,
    RaceDumbMaleToMale, RaceDumbMaleToFemale, RaceDumbFemaleToFemale, RaceDumbFemaleToMale,
    StoryMaleToMale, StoryMaleToFemale, StoryFemaleToFemale, StoryFemaleToMale,
}

/// <summary>The three NPC-line groups of the dialog: by social class (<c>gd_cls</c>), by race (<c>gd_rce</c>) and the plain
/// NPC lines (<c>gd_npc</c>, no dumb variant).</summary>
public enum GeneratedLineGroup { Class, Race, Npc }

public enum GeneratedDialogStatus { Ready, ReadFailed, ParseFailed }

/// <summary>The files of a module, or the file that could not be read or parsed (<paramref name="Path"/>).</summary>
public sealed record GeneratedDialogLoad(GeneratedDialogStatus Status, GeneratedDialogFiles? Files, string? Path = null,
    ContentReadStatus? Read = null, MesParseResult? Parse = null);

/// <summary>One entry fetched for a line: <see cref="Message"/> is null when the file has no entry of that number (the original
/// then shows its "missing line" text) or when the file itself is absent (<see cref="FileAbsent"/>).</summary>
public sealed record GeneratedLine(GeneratedDialogFile File, int Number, MesMessage? Message, bool FileAbsent = false);

/// <summary>
/// ARC-DATA-042: the generated-dialog files of a module, <c>mes\gd_*.mes</c>, checked in the 1.0.7.4 executable by two readers
/// (docs/research/original-verification-ledger.md "Generated dialog files"). The original opens each file on first use with
/// the dialog module's paths and exits the program when one is missing; here every file is read from the stack's winner at
/// module load (the data cannot change between) and a missing one is kept absent, so its lines are <see cref="GeneratedLine.FileAbsent"/>
/// (project policy). A file that cannot be read or parsed as MES refuses the whole load. Universal: whatever module the
/// stack mounts. The lines are raw bytes; name substitution and the random pick are the caller's.
/// </summary>
public sealed class GeneratedDialogFiles
{
    /// <summary>The file names in handle order (<see cref="GeneratedDialogFile"/>).</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "gd_pc2m", "gd_pc2f",
        "gd_cls_m2m", "gd_cls_m2f", "gd_cls_f2f", "gd_cls_f2m",
        "gd_cls_pc2m", "gd_cls_pc2f",
        "gd_rce_m2m", "gd_rce_m2f", "gd_rce_f2f", "gd_rce_f2m",
        "gd_npc_m2m", "gd_npc_m2f", "gd_npc_f2f", "gd_npc_f2m",
        "gd_dumb_pc2m", "gd_dumb_pc2f",
        "gd_cls_dumb_pc2m", "gd_cls_dumb_pc2f",
        "gd_cls_dumb_m2m", "gd_cls_dumb_m2f", "gd_cls_dumb_f2f", "gd_cls_dumb_f2m",
        "gd_rce_dumb_m2m", "gd_rce_dumb_m2f", "gd_rce_dumb_f2f", "gd_rce_dumb_f2m",
        "gd_sto_m2m", "gd_sto_m2f", "gd_sto_f2f", "gd_sto_f2m",
    ];

    public const int Count = 32;

    private readonly MesFile?[] _files;

    private GeneratedDialogFiles(MesFile?[] files) => _files = files;

    /// <summary>The stack path of a file: <c>mes/{name}.mes</c>.</summary>
    public static string PathOf(GeneratedDialogFile file) => "mes/" + Names[Index(file)] + ".mes";

    /// <summary>The parsed file, or null when the module has none.</summary>
    public MesFile? File(GeneratedDialogFile file) => _files[Index(file)];

    /// <summary>The files the module lacks, in handle order.</summary>
    public IReadOnlyList<GeneratedDialogFile> Absent =>
        Array.AsReadOnly(Enum.GetValues<GeneratedDialogFile>().Where(file => _files[(int)file] is null).ToArray());

    /// <summary>
    /// The file an NPC line of <paramref name="group"/> reads, by the NPC's (speaker's) and the PC's (listener's) gender: the
    /// pairs m2m, m2f, f2f, f2m in that order, and for <see cref="GeneratedLineGroup.Class"/> and
    /// <see cref="GeneratedLineGroup.Race"/> the dumb files when the NPC is dumb (the NPC-line generators 0x418480, 0x4185F0,
    /// 0x418780 choose this way; <see cref="GeneratedLineGroup.Npc"/> has no dumb files and ignores <paramref name="npcDumb"/>).
    /// </summary>
    public static GeneratedDialogFile NpcLineFile(GeneratedLineGroup group, bool npcMale, bool pcMale, bool npcDumb)
    {
        var pair = npcMale ? (pcMale ? 0 : 1) : (pcMale ? 3 : 2);
        var first = group switch
        {
            GeneratedLineGroup.Class => npcDumb ? GeneratedDialogFile.ClassDumbMaleToMale : GeneratedDialogFile.ClassMaleToMale,
            GeneratedLineGroup.Race => npcDumb ? GeneratedDialogFile.RaceDumbMaleToMale : GeneratedDialogFile.RaceMaleToMale,
            GeneratedLineGroup.Npc => GeneratedDialogFile.NpcMaleToMale,
            _ => throw new ArgumentOutOfRangeException(nameof(group)),
        };
        return first + pair;
    }

    /// <summary>
    /// The number of entries a line pick may choose from in <paramref name="first"/>..<paramref name="last"/> (0x4D4550 over
    /// the file): 0 when the file has no entry numbered exactly <paramref name="first"/>, else how many stored entries from it
    /// are numbered ≤ <paramref name="last"/>. The numbers are not checked to be consecutive, so a block with a gap counts
    /// fewer entries than its span (anomaly GD-A1, the caller's). 0 for an absent file.
    /// </summary>
    public int RunLength(GeneratedDialogFile file, int first, int last) => _files[Index(file)]?.RunLength(first, last) ?? 0;

    /// <summary>The entry <paramref name="number"/> of a file (0x4D43A0): its message, or none when it is missing (the original
    /// shows a static missing-line message naming the number and the file instead; what is shown is the caller's).</summary>
    [OriginalFunction(0x4D43A0, OriginalFunctionScope.Partial)]
    public GeneratedLine Line(GeneratedDialogFile file, int number)
    {
        var mes = _files[Index(file)];
        return new(file, number, mes?.Find(number), mes is null);
    }

    /// <summary>Builds the set from already-parsed files in handle order (synthetic data or another loader); null entries are
    /// absent files.</summary>
    public static GeneratedDialogFiles Create(IReadOnlyList<MesFile?> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count != Count) throw new ArgumentException("Thirty-two files in handle order.", nameof(files));
        return new(files.ToArray());
    }

    /// <summary>
    /// Reads every file (the original's open 0x418250 with <c>mes\%s.mes</c>) from the stack's winner at module load. A
    /// missing file stays absent where the original exits; one that cannot be read or parsed refuses the load.
    /// </summary>
    [OriginalFunction(0x418250, OriginalFunctionScope.Partial)]
    public static GeneratedDialogLoad Load(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        var files = new MesFile?[Count];
        for (var i = 0; i < Count; i++)
        {
            var path = PathOf((GeneratedDialogFile)i);
            if (ContentPhase.Candidate(stack, path, ContentLoadPhase.ModuleLoaded) is not { } source) continue;
            if (source.Length > options.MaxBytes) return new(GeneratedDialogStatus.ParseFailed, null, path, Parse: new(MesParseStatus.TooLarge, null));
            var read = stack.Read(source);
            if (read.Status != ContentReadStatus.Read) return new(GeneratedDialogStatus.ReadFailed, null, path, read.Status);
            var parsed = MesFile.Parse(read.Bytes!, options);
            if (parsed.Status != MesParseStatus.Parsed) return new(GeneratedDialogStatus.ParseFailed, null, path, Parse: parsed);
            files[i] = parsed.File;
        }
        return new(GeneratedDialogStatus.Ready, new(files));
    }

    private static int Index(GeneratedDialogFile file) =>
        (uint)file < Count ? (int)file : throw new ArgumentOutOfRangeException(nameof(file));
}
