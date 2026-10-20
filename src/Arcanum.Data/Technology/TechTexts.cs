using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Technology;

public sealed record TechTextsLoad(TechnologyDataStatus Status, TechTexts? Texts, TechnologyDataRefusal? Refusal = null);

/// <summary>
/// ARC-TECHNOLOGY-001: the texts of <c>mes\tech.mes</c> the original's tech module keeps (contract docs/formats/schematics.md,
/// ledger "Schematic data"): the 8 discipline names (messages 0..7), the 8 degree names (8..15), the 8 discipline descriptions
/// (16..23), the 64 degree descriptions (24 + discipline + 8 × degree) and the two schematic registration messages (88 "already
/// known", 89 "learned"). A missing message is null: the original then shows its shared missing-line text, which the host formats.
/// Text is the stored bytes as Latin-1 characters. Universal: nothing here names a map or a location.
/// </summary>
public sealed class TechTexts
{
    public const string Path = "mes/tech.mes";
    public const int Disciplines = 8, Degrees = 8;
    public const int FirstDegreeName = 8, FirstDisciplineDescription = 16, FirstDegreeDescription = 24;
    public const int AlreadyKnownMessage = 88, LearnedMessage = 89, Messages = 90;

    private readonly string?[] _degreeDescriptions;

    private TechTexts(string?[] disciplines, string?[] degrees, string?[] disciplineDescriptions, string?[] degreeDescriptions,
        string? alreadyKnown, string? learned, int[] missing)
    {
        DisciplineNames = Array.AsReadOnly(disciplines);
        DegreeNames = Array.AsReadOnly(degrees);
        DisciplineDescriptions = Array.AsReadOnly(disciplineDescriptions);
        _degreeDescriptions = degreeDescriptions;
        AlreadyKnown = alreadyKnown;
        Learned = learned;
        Missing = Array.AsReadOnly(missing);
    }

    /// <summary>Indexed by discipline 0..7.</summary>
    public IReadOnlyList<string?> DisciplineNames { get; }
    /// <summary>Indexed by degree 0..7.</summary>
    public IReadOnlyList<string?> DegreeNames { get; }
    /// <summary>Indexed by discipline 0..7.</summary>
    public IReadOnlyList<string?> DisciplineDescriptions { get; }
    /// <summary>Message 88: the schematic of a document is already known (the document is kept).</summary>
    public string? AlreadyKnown { get; }
    /// <summary>Message 89: a new schematic was learned from a document (the document is destroyed).</summary>
    public string? Learned { get; }
    /// <summary>The numbers of 0..89 the file does not hold, ascending (a diagnostic; the original shows its missing-line text).</summary>
    public IReadOnlyList<int> Missing { get; }

    /// <summary>The message number of the description of <paramref name="degree"/> in <paramref name="discipline"/>.</summary>
    public static int DegreeDescriptionMessage(int discipline, int degree)
    {
        Check(discipline, degree);
        return FirstDegreeDescription + discipline + Disciplines * degree;
    }

    public string? DegreeDescription(int discipline, int degree) => _degreeDescriptions[DegreeDescriptionMessage(discipline, degree) - FirstDegreeDescription];

    /// <summary>Reads <c>mes\tech.mes</c> from the first base source holding it; a missing, unreadable, empty or refused file
    /// fails as the original's module init does.</summary>
    public static TechTextsLoad Open(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        return TechnologyFiles.Load(stack, Path, options, out var file) is { } refusal ? new(refusal.Status, null, refusal) : Read(file!);
    }

    /// <summary>The texts of a parsed <c>mes\tech.mes</c> (synthetic data or another loader). An empty file fails as in the
    /// original's loader; a file without some of the messages is read with those texts null.</summary>
    [OriginalFunction(0x4AFCD0, OriginalFunctionScope.Partial)]
    public static TechTextsLoad Read(MesFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Count == 0) return new(TechnologyDataStatus.FileEmpty, null, new(TechnologyDataStatus.FileEmpty, Path));
        var texts = new string?[Messages];
        for (var number = 0; number < Messages; number++) texts[number] = file.Find(number) is { } m ? Encoding.Latin1.GetString(m.Bytes.Span) : null;
        var missing = Enumerable.Range(0, Messages).Where(n => texts[n] is null).ToArray();
        return new(TechnologyDataStatus.Read, new(texts[..Disciplines], texts[FirstDegreeName..(FirstDegreeName + Degrees)],
            texts[FirstDisciplineDescription..(FirstDisciplineDescription + Disciplines)],
            texts[FirstDegreeDescription..(FirstDegreeDescription + Disciplines * Degrees)], texts[AlreadyKnownMessage], texts[LearnedMessage], missing));
    }

    private static void Check(int discipline, int degree)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(discipline);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(discipline, Disciplines);
        ArgumentOutOfRangeException.ThrowIfNegative(degree);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(degree, Degrees);
    }
}
