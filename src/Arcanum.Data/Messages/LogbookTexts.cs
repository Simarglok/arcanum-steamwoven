using Arcanum.Data.Content;

namespace Arcanum.Data.Messages;

public enum LogbookTextStatus { Ready, ReadFailed, ParseFailed }

/// <summary>The logbook texts, or the file that could not be read or parsed (<paramref name="Path"/>).</summary>
public sealed record LogbookTextLoad(LogbookTextStatus Status, LogbookTexts? Texts, string? Path = null, ContentReadStatus? Read = null,
    MesParseResult? Parse = null);

/// <summary>
/// ARC-UI-008: the text the original logbook shows for a quest or a rumor, read from the content stack at module load (the
/// phase of the quest and rumor module-load functions, checked in the 1.0.7.4 executable, docs/research/original-verification-ledger.md
/// "Logbook"). Universal: it reads whatever module the stack mounts, never a location.
/// <list type="bullet">
/// <item>Quest descriptions (numbers 1000..1999) come from <c>mes\gamequestlog.mes</c> and, for a PC of low intelligence,
/// <c>mes\gamequestlogdumb.mes</c>; both are loaded only when the module's <c>rules\gamequest.mes</c> loads (LB8).</item>
/// <item>A rumor's text is message 20 × number of <c>mes\game_rd_npc_m2m.mes</c>, or of <c>mes\game_rd_npc_m2m_dumb.mes</c>
/// for a PC of low intelligence (LB10).</item>
/// </list>
/// Low intelligence is an effective intelligence of <see cref="LowIntelligence"/> or less. A file the stack does not hold
/// is not loaded (its texts are absent); a file that cannot be read or parsed refuses the whole load (project policy).
/// Texts are the stored MES bytes; decoding is the caller's choice of profile (<see cref="MesText"/>).
/// </summary>
public sealed class LogbookTexts
{
    public const string QuestRulesPath = "rules/gamequest.mes", QuestLogPath = "mes/gamequestlog.mes",
        QuestLogLowPath = "mes/gamequestlogdumb.mes", RumorPath = "mes/game_rd_npc_m2m.mes", RumorLowPath = "mes/game_rd_npc_m2m_dumb.mes";
    /// <summary>The highest intelligence that reads the low-intelligence texts.</summary>
    public const int LowIntelligence = 4;
    public const int FirstQuest = 1000, LastQuest = 1999;
    /// <summary>A rumor's logbook text is this multiple of its number in the rumor files.</summary>
    public const int RumorMessageStride = 20;

    private LogbookTexts(bool questDescriptions, MesFile? questLog, MesFile? questLogLow, MesFile? rumors, MesFile? rumorsLow)
    {
        QuestDescriptionsLoaded = questDescriptions;
        QuestLog = questDescriptions ? questLog : null;
        QuestLogLow = questDescriptions ? questLogLow : null;
        Rumors = rumors;
        RumorsLow = rumorsLow;
    }

    /// <summary>Whether the module has its quest table, without which the original loads no quest description.</summary>
    public bool QuestDescriptionsLoaded { get; }
    public MesFile? QuestLog { get; }
    public MesFile? QuestLogLow { get; }
    public MesFile? Rumors { get; }
    public MesFile? RumorsLow { get; }

    /// <summary>Builds the texts from already-parsed tables (synthetic data or another loader). <paramref name="questRules"/>
    /// says whether the module has <c>rules\gamequest.mes</c>; without it the quest tables are dropped, as the original does.</summary>
    public static LogbookTexts Create(bool questRules, MesFile? questLog = null, MesFile? questLogLow = null, MesFile? rumors = null,
        MesFile? rumorsLow = null) => new(questRules, questLog, questLogLow, rumors, rumorsLow);

    /// <summary>Reads the five files from <paramref name="stack"/>, each from the stack's winner (module-load phase).</summary>
    public static LogbookTextLoad Load(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        // The descriptions load only after the quest table itself loads (LB8): it is read and parsed here too.
        var failure = Read(stack, QuestRulesPath, options, out var rules);
        if (failure is not null) return failure;
        var questRules = rules is not null;
        MesFile? questLog = null, questLogLow = null;
        if (questRules)
        {
            failure = Read(stack, QuestLogPath, options, out questLog) ?? Read(stack, QuestLogLowPath, options, out questLogLow);
            if (failure is not null) return failure;
        }
        MesFile? rumorsLow = null;
        failure = Read(stack, RumorPath, options, out var rumors) ?? Read(stack, RumorLowPath, options, out rumorsLow);
        return failure ?? new(LogbookTextStatus.Ready, new(questRules, questLog, questLogLow, rumors, rumorsLow));
    }

    /// <summary>
    /// The quest's logbook description (LB7): the low-intelligence one when it exists and <paramref name="intelligence"/> is
    /// <see cref="LowIntelligence"/> or less, otherwise the normal one; null when the chosen table has no message for it
    /// (the original then shows no description) or the number is outside 1000..1999.
    /// </summary>
    [OriginalFunction(0x4C5250, OriginalFunctionScope.Full)]
    public MesMessage? QuestDescription(int quest, int intelligence)
    {
        if (quest is < FirstQuest or > LastQuest) return null;
        if (intelligence <= LowIntelligence && QuestLogLow?.Find(quest) is { } low) return low;
        return QuestLog?.Find(quest);
    }

    /// <summary>
    /// The rumor's logbook text (LB10): message 20 × <paramref name="rumor"/> of the normal rumor file, or of the
    /// low-intelligence file when <paramref name="intelligence"/> is <see cref="LowIntelligence"/> or less (no fallback to
    /// the other file); null when that file or message is absent or the number is not positive.
    /// </summary>
    [OriginalFunction(0x4C5920, OriginalFunctionScope.Full)]
    public MesMessage? RumorText(int rumor, int intelligence)
    {
        if (rumor <= 0 || rumor > int.MaxValue / RumorMessageStride) return null;
        var file = intelligence <= LowIntelligence ? RumorsLow : Rumors;
        return file?.Find(rumor * RumorMessageStride);
    }

    private static LogbookTextLoad? Read(ContentStack stack, string path, MesReaderOptions options, out MesFile? file)
    {
        file = null;
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.ModuleLoaded) is not { } source) return null;
        if (source.Length > options.MaxBytes) return new(LogbookTextStatus.ParseFailed, null, path, Parse: new(MesParseStatus.TooLarge, null));
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read) return new(LogbookTextStatus.ReadFailed, null, path, read.Status);
        var parsed = MesFile.Parse(read.Bytes!, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(LogbookTextStatus.ParseFailed, null, path, Parse: parsed);
        file = parsed.File;
        return null;
    }
}
