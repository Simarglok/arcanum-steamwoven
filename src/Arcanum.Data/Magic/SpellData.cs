using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Magic;

/// <summary>
/// ARC-MAGIC-001: the texts of <c>mes\spell.mes</c> the original keeps (ledger "Spell records"): the name of each of the 223
/// records (message n), the 16 college names (500 + c), the descriptions of the 80 college spells (700 + n), the college
/// descriptions (8000 + c) and the mastery descriptions (9000 + c). A missing message is null: the original then shows its
/// shared missing-line text, which the host formats. Text is the stored bytes as Latin-1 characters.
/// </summary>
public sealed class SpellTexts
{
    public const string Path = "mes/spell.mes";
    public const int FirstCollegeName = 500, FirstSpellDescription = 700, FirstCollegeDescription = 8000, FirstMasteryDescription = 9000;

    private SpellTexts(string?[] names, string?[] colleges, string?[] descriptions, string?[] collegeDescriptions, string?[] masteries)
    {
        Names = Array.AsReadOnly(names);
        CollegeNames = Array.AsReadOnly(colleges);
        SpellDescriptions = Array.AsReadOnly(descriptions);
        CollegeDescriptions = Array.AsReadOnly(collegeDescriptions);
        MasteryDescriptions = Array.AsReadOnly(masteries);
    }

    public IReadOnlyList<string?> Names { get; }
    public IReadOnlyList<string?> CollegeNames { get; }
    public IReadOnlyList<string?> SpellDescriptions { get; }
    public IReadOnlyList<string?> CollegeDescriptions { get; }
    public IReadOnlyList<string?> MasteryDescriptions { get; }

    /// <summary>Reads the texts; strict mode refuses a missing one.</summary>
    public static SpellTextsRead Read(MesFile file, SpellRecordMode mode)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (mode is not (SpellRecordMode.Strict or SpellRecordMode.LikeOriginal)) throw new ArgumentOutOfRangeException(nameof(mode));
        var names = new string?[SpellRecord.Count];
        for (var spell = 0; spell < names.Length; spell++) names[spell] = Text(file, spell);
        var texts = SpellModuleTexts(file);
        if (mode == SpellRecordMode.Strict)
        {
            var missing = Enumerable.Range(0, SpellRecord.Count).Where(n => names[n] is null)
                .Concat(Missing(texts.Colleges, FirstCollegeName)).Concat(Missing(texts.Descriptions, FirstSpellDescription))
                .Concat(Missing(texts.CollegeDescriptions, FirstCollegeDescription)).Concat(Missing(texts.Masteries, FirstMasteryDescription))
                .Order().Cast<int?>().FirstOrDefault();
            if (missing is { } number) return new(SpellDataStatus.MissingText, null, new(SpellDataStatus.MissingText, Path, number));
        }
        return new(SpellDataStatus.Read, new(names, texts.Colleges, texts.Descriptions, texts.CollegeDescriptions, texts.Masteries));
    }

    /// <summary>The spell module's init: the college names, spell descriptions, college and mastery descriptions.</summary>
    [OriginalFunction(0x4B1440, OriginalFunctionScope.Partial)]
    private static (string?[] Colleges, string?[] Descriptions, string?[] CollegeDescriptions, string?[] Masteries) SpellModuleTexts(MesFile file) =>
        (Range(file, FirstCollegeName, SpellRecord.Colleges), Range(file, FirstSpellDescription, SpellRecord.CollegeSpells),
         Range(file, FirstCollegeDescription, SpellRecord.Colleges), Range(file, FirstMasteryDescription, SpellRecord.Colleges));

    private static string?[] Range(MesFile file, int first, int count) => Enumerable.Range(first, count).Select(n => Text(file, n)).ToArray();

    private static IEnumerable<int> Missing(string?[] texts, int first) => texts.Select((t, i) => (t, i)).Where(p => p.t is null).Select(p => first + p.i);

    internal static string? Text(MesFile file, int number) => file.Find(number) is { } m ? Encoding.Latin1.GetString(m.Bytes.Span) : null;
}

public sealed record SpellTextsRead(SpellDataStatus Status, SpellTexts? Texts, SpellDataRefusal? Refusal = null);

public sealed record SpellDataLoad(SpellDataStatus Status, SpellData? Data, SpellDataRefusal? Refusal = null);

/// <summary>
/// ARC-MAGIC-001: the spell data the original's magic-tech init loads (ledger "Spell records"), in its order:
/// <c>rules\magictech.mes</c> (the 25 component type names, messages 10..34, each required), <c>mes\spell.mes</c> and
/// <c>rules\spelllist.mes</c>. Init files see the base sources only (ARC-DATA-027 load phases): a module never changes them.
/// Universal: nothing here names a map or a location.
/// </summary>
public sealed class SpellData
{
    public const string ComponentTypesPath = "rules/magictech.mes";
    public const int FirstComponentType = 10, ComponentTypes = 25;

    private SpellData(string[] componentTypes, SpellTexts texts, SpellRecordTable records)
    {
        ComponentTypeNames = Array.AsReadOnly(componentTypes);
        Texts = texts;
        Records = records;
    }

    /// <summary>The <c>Type:</c> names of the 25 component types, in type order.</summary>
    public IReadOnlyList<string> ComponentTypeNames { get; }
    public SpellTexts Texts { get; }
    public SpellRecordTable Records { get; }

    /// <summary>Reads the three files from the first base source holding each, in the original's order (the component type
    /// file, the text file, the component type names, the record file).</summary>
    [OriginalFunction(0x44EF50, OriginalFunctionScope.Partial)]
    public static SpellDataLoad Open(ContentStack stack, SpellRecordMode mode, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ValidateMode(mode);
        options ??= new();
        options.Validate();
        if (Load(stack, ComponentTypesPath, options, out var componentFile) is { } componentRefusal) return Refused(componentRefusal);
        if (Load(stack, SpellTexts.Path, options, out var textFile) is { } textRefusal) return Refused(textRefusal);
        if (ReadComponentTypes(componentFile!, out var names) is { } missing) return Refused(missing);
        if (Load(stack, SpellRecordTable.Path, options, out var listFile) is { } listRefusal) return Refused(listRefusal);
        return Create(names, textFile!, listFile!, mode);
    }

    /// <summary>The data of the three parsed files (synthetic data or another loader).</summary>
    public static SpellDataLoad Create(MesFile componentTypes, MesFile spellTexts, MesFile spellList, SpellRecordMode mode)
    {
        ArgumentNullException.ThrowIfNull(componentTypes);
        ArgumentNullException.ThrowIfNull(spellTexts);
        ArgumentNullException.ThrowIfNull(spellList);
        ValidateMode(mode);
        return ReadComponentTypes(componentTypes, out var names) is { } missing ? Refused(missing) : Create(names, spellTexts, spellList, mode);
    }

    private static SpellDataLoad Create(string[] names, MesFile spellTexts, MesFile spellList, SpellRecordMode mode)
    {
        ArgumentNullException.ThrowIfNull(spellTexts);
        ArgumentNullException.ThrowIfNull(spellList);
        var records = SpellRecordTable.Read(spellList, mode);
        if (records.Refusal is { } recordRefusal) return Refused(recordRefusal);
        var texts = SpellTexts.Read(spellTexts, mode);
        if (texts.Refusal is { } textRefusal) return Refused(textRefusal);
        return new(SpellDataStatus.Read, new(names, texts.Texts!, records.Table!));
    }

    /// <summary>Messages 10..34, each required.</summary>
    private static SpellDataRefusal? ReadComponentTypes(MesFile file, out string[] names)
    {
        names = new string[ComponentTypes];
        for (var i = 0; i < names.Length; i++)
        {
            if (SpellTexts.Text(file, FirstComponentType + i) is not { } name)
                return new(SpellDataStatus.MissingComponentName, ComponentTypesPath, FirstComponentType + i);
            names[i] = name;
        }
        return null;
    }

    private static void ValidateMode(SpellRecordMode mode)
    {
        if (mode is not (SpellRecordMode.Strict or SpellRecordMode.LikeOriginal)) throw new ArgumentOutOfRangeException(nameof(mode));
    }

    private static SpellDataLoad Refused(SpellDataRefusal refusal) => new(refusal.Status, null, refusal);

    private static SpellDataRefusal? Load(ContentStack stack, string path, MesReaderOptions options, out MesFile? file)
    {
        file = null;
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } source) return new(SpellDataStatus.FileMissing, path);
        if (source.Length > options.MaxBytes) return new(SpellDataStatus.FileRefused, path, Token: nameof(MesParseStatus.TooLarge));
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read) return new(SpellDataStatus.FileUnreadable, path, Token: read.Status.ToString());
        var parsed = MesFile.Parse(read.Bytes!, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(SpellDataStatus.FileRefused, path, parsed.Number, $"{parsed.Status} line {parsed.Line}");
        if (parsed.File!.Count == 0) return new(SpellDataStatus.FileEmpty, path);
        file = parsed.File;
        return null;
    }
}
