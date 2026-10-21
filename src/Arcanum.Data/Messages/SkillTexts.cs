using Arcanum.Data.Content;

namespace Arcanum.Data.Messages;

public enum SkillTextStatus { Ready, FileMissing, ReadFailed, ParseFailed }

/// <summary>The skill texts, or why <c>mes\skill.mes</c> could not be loaded.</summary>
public sealed record SkillTextLoad(SkillTextStatus Status, SkillTexts? Texts, ContentReadStatus? Read = null, MesParseResult? Parse = null);

/// <summary>
/// ARC-UI-009: the texts the original's skill module loads from <c>mes\skill.mes</c> at its init (0x4C5BD0, ledger SR12), so from
/// the base sources (the game-start phase, before a module mounts): the names of the twelve basic skills (0..11) and the four
/// tech skills (12..15), the four training names (16..19) and the descriptions (20..31 basic, 32..35 tech). A number the file
/// does not hold reads as null; a missing, unreadable or unparsable file refuses the load (the original fails its init).
/// Texts are the stored MES bytes; decoding is the caller's choice of profile (<see cref="MesText"/>).
/// </summary>
public sealed class SkillTexts
{
    public const string Path = "mes/skill.mes";
    public const int BasicSkills = 12, TechSkills = 4, Trainings = 4;
    public const int FirstTechName = 12, FirstTraining = 16, FirstBasicDescription = 20, FirstTechDescription = 32;

    private readonly MesFile _file;

    private SkillTexts(MesFile file) => _file = file;

    /// <summary>The texts of an already-parsed <c>skill.mes</c> (synthetic data or another loader).</summary>
    public static SkillTexts Create(MesFile file) => new(file ?? throw new ArgumentNullException(nameof(file)));

    /// <summary>Reads <c>mes\skill.mes</c> from <paramref name="stack"/>'s base sources (the game-start phase).</summary>
    public static SkillTextLoad Load(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        if (ContentPhase.Candidate(stack, Path, ContentLoadPhase.GameStart) is not { } source) return new(SkillTextStatus.FileMissing, null);
        if (source.Length > options.MaxBytes) return new(SkillTextStatus.ParseFailed, null, Parse: new(MesParseStatus.TooLarge, null));
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read) return new(SkillTextStatus.ReadFailed, null, read.Status);
        var parsed = MesFile.Parse(read.Bytes!, options);
        return parsed.Status != MesParseStatus.Parsed ? new(SkillTextStatus.ParseFailed, null, Parse: parsed) : new(SkillTextStatus.Ready, new(parsed.File!));
    }

    /// <summary>A basic skill's name (message <paramref name="skill"/>, 0..11).</summary>
    [OriginalFunction(0x4C62B0, OriginalFunctionScope.Full)]
    public MesMessage? BasicName(int skill) => _file.Find(Check(skill, BasicSkills, nameof(skill)));

    /// <summary>A tech skill's name (message 12 + <paramref name="skill"/>, 0..3).</summary>
    [OriginalFunction(0x4C69A0, OriginalFunctionScope.Full)]
    public MesMessage? TechName(int skill) => _file.Find(FirstTechName + Check(skill, TechSkills, nameof(skill)));

    /// <summary>A training's name (message 16 + <paramref name="training"/>: none, apprentice, expert, master).</summary>
    [OriginalFunction(0x4C69D0, OriginalFunctionScope.Full)]
    public MesMessage? TrainingName(int training) => _file.Find(FirstTraining + Check(training, Trainings, nameof(training)));

    /// <summary>A basic skill's description (message 20 + <paramref name="skill"/>).</summary>
    [OriginalFunction(0x4C62C0, OriginalFunctionScope.Full)]
    public MesMessage? BasicDescription(int skill) => _file.Find(FirstBasicDescription + Check(skill, BasicSkills, nameof(skill)));

    /// <summary>A tech skill's description (message 32 + <paramref name="skill"/>).</summary>
    [OriginalFunction(0x4C69B0, OriginalFunctionScope.Full)]
    public MesMessage? TechDescription(int skill) => _file.Find(FirstTechDescription + Check(skill, TechSkills, nameof(skill)));

    private static int Check(int value, int count, string name) =>
        value >= 0 && value < count ? value : throw new ArgumentOutOfRangeException(name);
}
