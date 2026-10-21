using System.Text;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Written;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Technology;

namespace Arcanum.TerrainViewer.Core;

public enum AreaReadingDataStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed }

/// <summary>The reading window's data of a module (ARC-UI-049): the written texts and the written facts of every written prototype
/// (for a kit item, which has no area row), read once in the background.</summary>
public sealed class AreaReadingData
{
    public AreaReadingData(WrittenTexts texts, IReadOnlyDictionary<int, SessionWrittenFacts> prototypes)
    {
        Texts = texts ?? throw new ArgumentNullException(nameof(texts));
        Prototypes = prototypes ?? throw new ArgumentNullException(nameof(prototypes));
    }

    public WrittenTexts Texts { get; }
    public IReadOnlyDictionary<int, SessionWrittenFacts> Prototypes { get; }
}

public sealed record AreaReadingDataLoad(AreaReadingDataStatus Status, AreaReadingData? Data, string Detail);

/// <summary>
/// One spread of the reading window as the diagnostic panel draws it: the subtype, its background and frame art, the header lines (a
/// telegram's form, a newspaper's masthead), the left and right page texts (a book shows two pages a spread and turns two at a time;
/// every other subtype shows one page), the spread index and count, a picture's art, and a note.
/// </summary>
public sealed record AreaReadingView(WrittenSubtype Subtype, int BackgroundArt, IReadOnlyList<string> Header, string Left, string Right,
    int Spread, int Spreads, int? PictureArt, string? Note)
{
    public bool HasPrevious => Subtype == WrittenSubtype.Book && Spread > 0;
    public bool HasNext => Subtype == WrittenSubtype.Book && Spread < Spreads - 1;
}

/// <summary>
/// ARC-UI-049: the reading window, Godot-free. The documents come from <see cref="WrittenTexts"/> through the session
/// (<see cref="GameSessionOwner.UseWrittenItem"/> for an item, <see cref="GameSessionOwner.QueryWrittenDocument"/> for a script opener);
/// this class decodes and pages them for the diagnostic panel. The original pages a book by pixel height (two pages of 200 × 290 a
/// spread) with its fonts; the panel pages by <see cref="BookPageCharacters"/> characters instead (PROJECT layout), keeping the two
/// pages a spread, the turn of two and the bound of <see cref="WrittenTexts.MaxBookPages"/> pages.
/// </summary>
public static class AreaReading
{
    /// <summary>The characters the diagnostic panel puts on one book page (the original measures pixels).</summary>
    public const int BookPageCharacters = 700;
    /// <summary>How many newspaper filler messages the diagnostic panel shows after the body (the original fills the columns).</summary>
    public const int NewspaperFillerShown = 3;

    /// <summary>Loads the written texts and every written prototype's facts through the game folder's content stack.</summary>
    public static AreaReadingDataLoad LoadData(string modulePath)
    {
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return new(found.Status == AreaContentRootStatus.MountFailed ? AreaReadingDataStatus.MountFailed : AreaReadingDataStatus.ModuleOutsideGameRoot, null, found.Detail);
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return new(AreaReadingDataStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var texts = WrittenTexts.Open(stack);
        var prototypes = ContentPrototypes.Open(stack);
        var facts = new Dictionary<int, SessionWrittenFacts>();
        foreach (var number in prototypes.Numbers)
            if (CombatFieldReader.ReadPrototype(prototypes.Read, number).Fields is { Written: not null } fields && SessionWrittenFacts.FromCombatFields(fields) is { } written)
                facts[number] = written;
        return new(AreaReadingDataStatus.Ready, new(texts, facts),
            $"written prototypes {facts.Count}, text files refused {texts.Refusals.Count}");
    }

    /// <summary>The spread of a document: its decoded header and pages. A refused document gives null.</summary>
    public static AreaReadingView? View(WrittenDocument document, WrittenTexts texts, int spread, MesTextProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(texts);
        if (!document.Readable || document.Subtype is not { } subtype) return null;
        var header = document.Header.Select(h => Decode(h.Text, profile)).ToArray();
        if (subtype == WrittenSubtype.Picture)
            return new(subtype, document.BackgroundArt, header, "", "", 0, 1, document.PictureArt, "the picture is interface art " + document.PictureArt + " (not drawn here)");
        var body = document.Body.Select(m => (m.Centered ? "[centred] " : "") + Decode(m.Text, profile)).ToList();
        if (subtype == WrittenSubtype.Newspaper)
            body.AddRange(texts.NewspaperFiller(document.StartLine, NewspaperFillerShown).Select(m => "[filler] " + Decode(m.Text, profile)));
        var text = string.Join(subtype is WrittenSubtype.Note or WrittenSubtype.Telegram ? "" : "\n", body);
        if (subtype != WrittenSubtype.Book)
            return new(subtype, document.BackgroundArt, header, text, "", 0, 1, null, body.Count == 0 ? "no text at the start line" : null);
        var pages = Pages(text);
        var spreads = Math.Max(1, (pages.Count + 1) / 2);
        var shown = Math.Clamp(spread, 0, spreads - 1);
        return new(subtype, document.BackgroundArt, header, pages.ElementAtOrDefault(2 * shown) ?? "", pages.ElementAtOrDefault(2 * shown + 1) ?? "",
            shown, spreads, null, body.Count == 0 ? "no text at the start line" : null);
    }

    /// <summary>A script opener's document facts: a book (subtype 0), a picture (4) or a plaque (6) at the action's number.</summary>
    public static SessionWrittenFacts ScriptFacts(ScrReadingRequest request) => request.Kind switch
    {
        ScrReadingKind.Book => new((int)WrittenSubtype.Book, request.Number),
        ScrReadingKind.Picture => new((int)WrittenSubtype.Picture, request.Number),
        _ => new((int)WrittenSubtype.Plaque, request.Number),
    };

    /// <summary>The book's pages: at most <see cref="BookPageCharacters"/> characters each, broken at a space or newline when one is
    /// in the second half of the page, and never more than <see cref="WrittenTexts.MaxBookPages"/> pages.</summary>
    public static IReadOnlyList<string> Pages(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var pages = new List<string>();
        var at = 0;
        while (at < text.Length && pages.Count < WrittenTexts.MaxBookPages)
        {
            var length = Math.Min(BookPageCharacters, text.Length - at);
            if (at + length < text.Length)
            {
                var cut = text.LastIndexOfAny([' ', '\n'], at + length - 1, length);
                if (cut > at + length / 2) length = cut - at + 1;
            }
            pages.Add(text.Substring(at, length).TrimEnd());
            at += length;
        }
        if (pages.Count == 0) pages.Add("");
        return pages.AsReadOnly();
    }

    /// <summary>
    /// The message of a schematic document's reading (SK2): <c>mes\tech.mes</c> 89 and the schematic's name for a new one, 88 for one
    /// already known; Steamwoven's refusal of a document numbered 0 (TC8) gets a project text. Without the schematic data the numbers stand in.
    /// </summary>
    public static string SchematicMessage(SchematicReading? reading, int? number, AreaSchematicData? data, MesTextProfile profile)
    {
        var name = data is not null && number is { } n && data.Catalog.Read(n).Record is { } record
            ? Decode(data.Catalog.Text(record.NameText) ?? "", profile) : $"schematic {number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}";
        return reading switch
        {
            SchematicReading.Registered => (data?.Texts.Learned is { } learned ? Decode(learned, profile) : "[tech 89]") + " " + name,
            SchematicReading.AlreadyKnown => data?.Texts.AlreadyKnown is { } known ? Decode(known, profile) : "[tech 88]",
            SchematicReading.RefusedZero => "not read: a schematic document numbered 0 (Steamwoven, TC8)",
            _ => reading?.ToString() ?? "",
        };
    }

    /// <summary>The text of a session refusal of a written item's use for the inventory message.</summary>
    public static string UseRefused(GameSessionFailure failure) => failure switch
    {
        GameSessionFailure.InvalidLifeTarget => "no hero or the hero is dead",
        GameSessionFailure.ItemNotAvailable => "the hero does not hold it",
        GameSessionFailure.InventoryMismatch => "not a written item",
        GameSessionFailure.ItemScriptUnsupported => "its use script is not run here",
        GameSessionFailure.ItemTypeUnsupported => "a magical written item (the item-spell path is not modelled)",
        GameSessionFailure.InvalidItem => "an unknown written kind or a picture art out of range",
        GameSessionFailure.BudgetExceeded => "the found schematic list or the record budget is full",
        _ => failure.ToString(),
    };

    /// <summary>The text of a reading status for the status line.</summary>
    public static string Refused(WrittenReadStatus status) => status switch
    {
        WrittenReadStatus.TextsUnavailable => "the text file of this kind did not load",
        WrittenReadStatus.SubtypeOutOfRange => "an unknown written kind",
        WrittenReadStatus.ArtOutOfRange => "a picture art outside 0..4095",
        WrittenReadStatus.Schematic => "a schematic document",
        _ => status.ToString(),
    };

    private static string Decode(string latin1, MesTextProfile profile) =>
        MesText.Decode(Encoding.Latin1.GetBytes(latin1), profile) is { Status: MesDecodeStatus.Decoded, Text: { } text } ? text : "[text not decodable]";
}
