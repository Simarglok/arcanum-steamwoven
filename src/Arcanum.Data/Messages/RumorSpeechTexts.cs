using Arcanum.Data.Content;

namespace Arcanum.Data.Messages;

/// <summary>
/// ARC-SCRIPT-1304 (ledger RMR7): the four rumor files an NPC tells a rumor from (<c>mes\game_rd_npc_m2m.mes</c>, <c>m2f</c>, <c>f2f</c>, <c>f2m</c>: the
/// speaker's gender, then the listener's), loaded with the module (0x4C5570); a rumor's text is message 20 × its number plus the speaker's social class plus 1,
/// or the class-less message 20 × number + 1 (0x4C5700). A file that is not in the module, or that cannot be read or parsed, reads as absent (its rumors
/// have no text); <see cref="Failures"/> names the ones that were present but unusable.
/// </summary>
public sealed class RumorSpeechTexts
{
    /// <summary>The files in the original's handle order: speaker gender to listener gender.</summary>
    public static readonly IReadOnlyList<string> Paths =
        ["mes/game_rd_npc_m2m.mes", "mes/game_rd_npc_m2f.mes", "mes/game_rd_npc_f2f.mes", "mes/game_rd_npc_f2m.mes"];

    /// <summary>The messages of a rumor are this far apart (0x4C5700: number × 20).</summary>
    public const int MessageStride = 20;

    private RumorSpeechTexts(IReadOnlyList<MesFile?> files, IReadOnlyList<string> failures)
    {
        Files = files;
        Failures = failures;
    }

    /// <summary>The four files, in <see cref="Paths"/> order; null for an absent or unusable one.</summary>
    public IReadOnlyList<MesFile?> Files { get; }

    /// <summary>Paths of the files that exist in the module but could not be read or parsed.</summary>
    public IReadOnlyList<string> Failures { get; }

    /// <summary>Builds the texts from already-parsed files (synthetic data or another loader).</summary>
    public static RumorSpeechTexts Create(IReadOnlyList<MesFile?> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count != Paths.Count) throw new ArgumentException("Four rumor files.", nameof(files));
        return new(files.ToArray(), []);
    }

    /// <summary>Reads the four files from <paramref name="stack"/> (module-load phase).</summary>
    public static RumorSpeechTexts Load(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        var files = new MesFile?[Paths.Count];
        var failures = new List<string>();
        for (var index = 0; index < Paths.Count; index++)
        {
            if (ContentPhase.Candidate(stack, Paths[index], ContentLoadPhase.ModuleLoaded) is not { } source) continue;
            if (source.Length > options.MaxBytes) { failures.Add(Paths[index]); continue; }
            var read = stack.Read(source);
            if (read.Status != ContentReadStatus.Read) { failures.Add(Paths[index]); continue; }
            var parsed = MesFile.Parse(read.Bytes!, options);
            if (parsed.Status != MesParseStatus.Parsed) { failures.Add(Paths[index]); continue; }
            files[index] = parsed.File;
        }
        return new(files, failures);
    }
}
