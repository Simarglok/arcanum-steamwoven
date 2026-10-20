using System.Text;
using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Technology;

public sealed record SchematicCatalogLoad(TechnologyDataStatus Status, SchematicCatalog? Catalog, TechnologyDataRefusal? Refusal = null);

public sealed record SchematicRecordRead(TechnologyDataStatus Status, SchematicRecord? Record, TechnologyDataRefusal? Refusal = null);

public sealed record SchematicAlternativesParse(TechnologyDataStatus Status, SchematicAlternatives? Alternatives);

/// <summary>The window's tabs of one crafter (ARC-TECHNOLOGY-003): per discipline 0..7 the learned and the found record numbers
/// in the window's order.</summary>
public sealed record SchematicTabs(IReadOnlyList<IReadOnlyList<int>> Learned, IReadOnlyList<IReadOnlyList<int>> Found);

/// <summary>
/// ARC-TECHNOLOGY-001: the schematic data the original's schematic window loads at its init (contract docs/formats/schematics.md,
/// ledger "Schematic data"): the records of <c>rules\schematic.mes</c> and the texts of <c>mes\schematic_text.mes</c>, both from
/// the base sources of the init phase and both required. A record is the seven entries n..n+6; learned records are 2000..3999
/// (discipline <c>(n − 2000) / 200</c>), found records 4000 and up, and the window shows the placeholder <c>6000 + 10 ×
/// discipline</c> for an empty tab. Every record of the file is parsed when the catalogue is created, so a line the original
/// would read out of bounds (TC6) refuses the catalogue. Universal: nothing here names a map or a location.
/// </summary>
public sealed class SchematicCatalog
{
    public const string Path = "rules/schematic.mes", TextPath = "mes/schematic_text.mes";
    public const int FirstLearned = 2000, LastLearned = 3999, FirstFound = 4000, FirstPlaceholder = 6000;
    public const int RecordStride = 10, NumbersPerDiscipline = 200, FirstLearnedNumberBase = 1990;
    /// <summary>The original copies a list line into a buffer of this size; the longest line it holds is one byte shorter.</summary>
    public const int LineBufferBytes = 2000;

    private readonly MesFile _rules;
    private readonly MesFile _texts;
    private readonly SortedDictionary<int, SchematicRecord> _records;

    private SchematicCatalog(MesFile rules, MesFile texts, int learnedCount, int[] learnedStarts, SortedDictionary<int, SchematicRecord> records, int[] unread)
    {
        _rules = rules;
        _texts = texts;
        _records = records;
        LearnedCount = learnedCount;
        LearnedStarts = Array.AsReadOnly(learnedStarts);
        Records = Array.AsReadOnly(records.Values.ToArray());
        Unread = Array.AsReadOnly(unread);
    }

    /// <summary>The number of entries of <c>rules\schematic.mes</c> in 2000..3999, divided by 7 (C division).</summary>
    public int LearnedCount { get; }
    /// <summary>The original's "full list" of learned record numbers: each of 2000, 2010, ... below 3999 whose entry exists, in
    /// order; empty when <see cref="LearnedCount"/> is 0. Its length is the count (the catalogue refuses a file where it is not,
    /// TC11).</summary>
    public IReadOnlyList<int> LearnedStarts { get; }
    /// <summary>Every record that holds at least one of its entries n..n+6 (n mod 10 = 0), ascending.</summary>
    public IReadOnlyList<SchematicRecord> Records { get; }
    /// <summary>Entries no record reads (offsets 7..9 of a record's ten numbers), ascending; a diagnostic.</summary>
    public IReadOnlyList<int> Unread { get; }

    /// <summary>A text of <c>mes\schematic_text.mes</c> (a record's name or description, or a window text 0..5); null when
    /// missing (the original shows its missing-line text, which the host formats). Latin-1 characters of the stored bytes.</summary>
    public string? Text(int number) => _texts.Find(number) is { } m ? Encoding.Latin1.GetString(m.Bytes.Span) : null;

    /// <summary>The number of messages of <c>mes\schematic_text.mes</c>.</summary>
    public int TextCount => _texts.Count;

    /// <summary>The record at <paramref name="number"/> as the original reads it: any number, a missing entry reading as 0. The
    /// original replaces −1 (an empty selection) by the placeholder first; that substitution is <see cref="RecordToShow"/>, so
    /// here −1 is read as entries −1..5.</summary>
    public SchematicRecordRead Read(int number) =>
        _records.TryGetValue(number, out var record) ? new(TechnologyDataStatus.Read, record) : ReadRecord(_rules, number);

    /// <summary>Reads both files from the first base source holding each, in the original's order: the record file, then the text
    /// file (without it the init unloads the record file and fails).</summary>
    [OriginalFunction(0x56CD60, OriginalFunctionScope.Partial)]
    public static SchematicCatalogLoad Open(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        if (TechnologyFiles.Load(stack, Path, options, out var rules) is { } rulesRefusal) return Refused(rulesRefusal);
        if (TechnologyFiles.Load(stack, TextPath, options, out var texts) is { } textRefusal) return Refused(textRefusal);
        return Create(rules!, texts!);
    }

    /// <summary>The catalogue of two parsed files (synthetic data or another loader). An empty file fails as in the original's
    /// loader; a list line with more than three tokens refuses the catalogue (TC6), and so does a learned range the original's
    /// full list cannot hold (TC11).</summary>
    public static SchematicCatalogLoad Create(MesFile rules, MesFile texts)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(texts);
        if (rules.Count == 0) return Refused(new(TechnologyDataStatus.FileEmpty, Path));
        if (texts.Count == 0) return Refused(new(TechnologyDataStatus.FileEmpty, TextPath));
        var records = new SortedDictionary<int, SchematicRecord>();
        foreach (var start in rules.Messages.Where(m => m.Number % RecordStride < SchematicRecord.Lines)
                     .Select(m => m.Number - m.Number % RecordStride).Distinct())
        {
            var read = ReadRecord(rules, start);
            if (read.Refusal is { } refusal) return Refused(refusal);
            records.Add(start, read.Record!);
        }
        var unread = rules.Messages.Select(m => m.Number).Where(n => n % RecordStride >= SchematicRecord.Lines).ToArray();
        var count = CountLearned(rules);
        var starts = count > 0 ? LearnedStartsOf(rules) : [];
        if (count > 0 && starts.Length != count)
            return Refused(new(TechnologyDataStatus.LearnedListMismatch, Path, Token: $"{starts.Length} starts, count {count}"));
        if (starts.FirstOrDefault(n => (n - FirstLearned) / NumbersPerDiscipline >= TechTexts.Disciplines) is var outside and > 0)
            return Refused(new(TechnologyDataStatus.LearnedDisciplineOutOfRange, Path, outside));
        return new(TechnologyDataStatus.Read, new(rules, texts, count, starts, records, unread));
    }

    /// <summary>The learned count of the original's init: the entries in 2000..3999 divided by 7, or 0 when entry 2000 itself
    /// is missing (the original's count starts only at an existing number).</summary>
    private static int CountLearned(MesFile rules) =>
        rules.Find(FirstLearned) is null ? 0 : rules.Messages.Count(m => m.Number is >= FirstLearned and <= LastLearned) / SchematicRecord.Lines;

    /// <summary>The original's full list: each of 2000, 2010, ... below 3999 whose entry exists, in order. The original builds
    /// it only for a count above 0, in a list of "count" slots that it fills without a bound and walks to the count (TC11).</summary>
    private static int[] LearnedStartsOf(MesFile rules)
    {
        var starts = new List<int>();
        for (var n = FirstLearned; n < LastLearned; n += RecordStride)
            if (rules.Find(n) is not null) starts.Add(n);
        return starts.ToArray();
    }

    /// <summary>The kind of the range <paramref name="number"/> lies in.</summary>
    public static SchematicKind KindOf(int number) => number switch
    {
        < FirstLearned => SchematicKind.Other,
        <= LastLearned => SchematicKind.Learned,
        >= FirstPlaceholder and < FirstPlaceholder + RecordStride * TechTexts.Disciplines when number % RecordStride == 0 => SchematicKind.Placeholder,
        _ => SchematicKind.Found,
    };

    /// <summary>The record the window reads for an empty tab of <paramref name="discipline"/>.</summary>
    public static int PlaceholderNumber(int discipline)
    {
        CheckDiscipline(discipline);
        return FirstPlaceholder + RecordStride * discipline;
    }

    /// <summary>The learned schematic number of <paramref name="degree"/> (1..7) in <paramref name="discipline"/>:
    /// <c>1990 + 200 × discipline + 10 × degree</c> (the editor's degree tooltips; the window uses the full list instead).</summary>
    [OriginalFunction(0x4B0320, OriginalFunctionScope.Full)]
    public static int LearnedNumber(int discipline, int degree)
    {
        CheckDiscipline(discipline);
        ArgumentOutOfRangeException.ThrowIfLessThan(degree, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(degree, TechTexts.Degrees);
        return FirstLearnedNumberBase + NumbersPerDiscipline * discipline + RecordStride * degree;
    }

    /// <summary>
    /// The discipline of schematic <paramref name="number"/>: 4000 and up, the discipline of the first product prototype, which
    /// <paramref name="prototypeDiscipline"/> gives (prototype number → its discipline field, null when unknown); 2000..3999,
    /// <c>(n − 2000) / 200</c>; otherwise 0. Null when the lookup knows no discipline, the record cannot be read or its product
    /// line is empty (the original then reads a field it never wrote).
    /// </summary>
    [OriginalFunction(0x56DB00, OriginalFunctionScope.Partial)]
    public int? Discipline(int number, Func<int, int?> prototypeDiscipline)
    {
        ArgumentNullException.ThrowIfNull(prototypeDiscipline);
        if (number >= FirstFound)
        {
            var read = Read(number);
            if (read.Record is not { Product.IsEmpty: false } record) return null;
            return prototypeDiscipline(record.Product.Prototypes[0]);
        }
        return number >= FirstLearned ? (number - FirstLearned) / NumbersPerDiscipline : 0;
    }

    /// <summary>The learned records of <paramref name="discipline"/> in the full list's order.</summary>
    public IReadOnlyList<int> Learned(int discipline)
    {
        CheckDiscipline(discipline);
        return Array.AsReadOnly(LearnedStarts.Where(n => (n - FirstLearned) / NumbersPerDiscipline == discipline).ToArray());
    }

    /// <summary>
    /// The window's selection: when <paramref name="shown"/> (the tab's count for <paramref name="discipline"/>: the crafter's
    /// stored degree on the learned tab, the found count on the found tab) is 0, −1; otherwise the <paramref name="page"/>-th
    /// number (from 0) of <paramref name="list"/> whose discipline is <paramref name="discipline"/>, or −1 when there is none.
    /// The window keeps the page below <paramref name="shown"/>; the selection itself does not compare them.
    /// </summary>
    [OriginalFunction(0x56DB60, OriginalFunctionScope.Full)]
    public int Select(IReadOnlyList<int> list, int shown, int discipline, int page, Func<int, int?> prototypeDiscipline)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(prototypeDiscipline);
        CheckDiscipline(discipline);
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        if (shown == 0) return -1;
        var seen = 0;
        foreach (var number in list)
            if (Discipline(number, prototypeDiscipline) == discipline && seen++ == page) return number;
        return -1;
    }

    /// <summary>The learned tab's selection for a crafter with stored <paramref name="degrees"/> (eight, 0..7): the full list,
    /// with the crafter's degree in <paramref name="discipline"/> as the count. Learned numbers need no prototype.</summary>
    public int SelectLearned(IReadOnlyList<int> degrees, int discipline, int page)
    {
        ArgumentNullException.ThrowIfNull(degrees);
        if (degrees.Count != TechTexts.Disciplines || degrees.Any(d => d is < 0 or >= TechTexts.Degrees))
            throw new ArgumentException("Eight stored degrees 0..7 are required.", nameof(degrees));
        CheckDiscipline(discipline);
        return Select(LearnedStarts, degrees[discipline], discipline, page, static _ => null);
    }

    /// <summary>
    /// The window's two tabs for a crafter (0x56D130, its list part; ARC-TECHNOLOGY-003): the learned tab of each discipline is
    /// the first "stored degree" records of the full list (intelligence does not hide any: it only lowers the expertise); the
    /// found tab, for a PC crafter only, the crafter's found numbers in list order, each in the discipline
    /// <see cref="Discipline"/> gives. A found number whose discipline is unknown or outside 0..7 is left out (the original
    /// counts it at that index of an eight-entry table, past its end). An NPC crafter has no found tab.
    /// </summary>
    [OriginalFunction(0x56D130, OriginalFunctionScope.Partial)]
    public SchematicTabs Tabs(IReadOnlyList<int> degrees, IReadOnlyList<int> found, bool crafterIsPc, Func<int, int?> prototypeDiscipline)
    {
        ArgumentNullException.ThrowIfNull(degrees);
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(prototypeDiscipline);
        if (degrees.Count != TechTexts.Disciplines || degrees.Any(d => d is < 0 or >= TechTexts.Degrees))
            throw new ArgumentException("Eight stored degrees 0..7 are required.", nameof(degrees));
        var learned = new List<int>[TechTexts.Disciplines];
        var foundTabs = new List<int>[TechTexts.Disciplines];
        for (var d = 0; d < TechTexts.Disciplines; d++) { learned[d] = []; foundTabs[d] = []; }
        for (var d = 0; d < TechTexts.Disciplines; d++)
            for (var page = 0; page < degrees[d] && SelectLearned(degrees, d, page) is var number and >= 0; page++) learned[d].Add(number);
        if (crafterIsPc)
            foreach (var number in found)
                if (Discipline(number, prototypeDiscipline) is { } discipline and >= 0 and < TechTexts.Disciplines) foundTabs[discipline].Add(number);
        return new(Array.AsReadOnly(learned.Select(l => (IReadOnlyList<int>)l.AsReadOnly()).ToArray()),
            Array.AsReadOnly(foundTabs.Select(l => (IReadOnlyList<int>)l.AsReadOnly()).ToArray()));
    }

    /// <summary>The prototype-discipline lookup of <see cref="Discipline"/> over the item discipline field (0x61) of prototypes
    /// (<see cref="Combat.CombatFieldReader.ReadPrototype"/>); null for a prototype that is missing or not an item.</summary>
    public static Func<int, int?> PrototypeDisciplines(Func<int, Characters.PrototypeFile> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        return prototype => Combat.CombatFieldReader.ReadPrototype(lookup, prototype).Fields?.Item?.Discipline;
    }

    /// <summary>The record the window shows for a selection: the selected record, or for −1 (an empty tab) the placeholder
    /// <c>6000 + 10 × discipline</c>, as the original's record read substitutes it.</summary>
    public SchematicRecordRead RecordToShow(int selected, int discipline) => Read(selected == -1 ? PlaceholderNumber(discipline) : selected);

    /// <summary>One record: the seven entries n..n+6, name, description, art and quantity through C <c>atoi</c>, the three lists
    /// through <see cref="ParseAlternatives"/>. A missing entry is the original's missing-line text ("Error! Missing line N in
    /// rules\schematic.mes"): <c>atoi</c> reads it as 0, and the list parse reads its first three tokens as 0 and writes the
    /// other three past the field (TC6 class); Steamwoven keeps the three zeros and drops the rest in both modes. The
    /// placeholder for a selection of −1 is <see cref="RecordToShow"/>.</summary>
    [OriginalFunction(0x56DBD0, OriginalFunctionScope.Partial)]
    private static SchematicRecordRead ReadRecord(MesFile rules, int number)
    {
        var missing = new bool[SchematicRecord.Lines];
        var values = new int[SchematicRecord.Lines];
        var lists = new SchematicAlternatives[SchematicRecord.Lines];
        for (var line = 0; line < SchematicRecord.Lines; line++)
        {
            var entry = unchecked(number + line);
            var text = rules.Find(entry) is { } m ? Encoding.Latin1.GetString(m.Bytes.Span) : null;
            missing[line] = text is null;
            if ((SchematicLine)line is SchematicLine.Component1 or SchematicLine.Component2 or SchematicLine.Product)
            {
                if (text is null)
                {
                    lists[line] = new(Array.AsReadOnly(new int[SchematicAlternatives.Count]), SchematicAlternatives.Count);
                    continue;
                }
                var parsed = ParseAlternatives(text);
                if (parsed.Status != TechnologyDataStatus.Read) return new(parsed.Status, null, new(parsed.Status, Path, entry, $"{text.Length} bytes"));
                lists[line] = parsed.Alternatives!;
            }
            else values[line] = text is null ? 0 : SoundSchemeParser.Atoi(text);
        }
        return new(TechnologyDataStatus.Read, new()
        {
            Number = number, Kind = KindOf(number), NameText = values[(int)SchematicLine.Name], DescriptionText = values[(int)SchematicLine.Description],
            Art = values[(int)SchematicLine.Art], Component1 = lists[(int)SchematicLine.Component1], Component2 = lists[(int)SchematicLine.Component2],
            Product = lists[(int)SchematicLine.Product], Quantity = values[(int)SchematicLine.Quantity], MissingLines = Array.AsReadOnly(missing),
        });
    }

    /// <summary>
    /// One list line: split at single spaces as C <c>strtok</c> does (runs of spaces and leading or trailing spaces make no token;
    /// a tab is part of a token), each token through C <c>atoi</c>. One or two tokens repeat the last to fill three; no token
    /// leaves the field empty (the original leaves it unwritten). More than three tokens or a line the original's buffer cannot hold are refused (TC6: the original
    /// writes out of bounds; both rule modes refuse).
    /// </summary>
    [OriginalFunction(0x56DD20, OriginalFunctionScope.Partial)]
    public static SchematicAlternativesParse ParseAlternatives(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length >= LineBufferBytes) return new(TechnologyDataStatus.LineTooLong, null);
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > SchematicAlternatives.Count) return new(TechnologyDataStatus.TooManyTokens, null);
        if (tokens.Length == 0) return new(TechnologyDataStatus.Read, SchematicAlternatives.Empty);
        var bases = new int[SchematicAlternatives.Count];
        for (var i = 0; i < bases.Length; i++) bases[i] = SoundSchemeParser.Atoi(tokens[Math.Min(i, tokens.Length - 1)]);
        return new(TechnologyDataStatus.Read, new(Array.AsReadOnly(bases), tokens.Length));
    }

    private static void CheckDiscipline(int discipline)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(discipline);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(discipline, TechTexts.Disciplines);
    }

    private static SchematicCatalogLoad Refused(TechnologyDataRefusal refusal) => new(refusal.Status, null, refusal);
}
