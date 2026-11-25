using Arcanum.Data.Written;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-UI-2765: a local census of the note address and signature rule (<see cref="AreaReadingNoteRule"/>) over every note of the game (every start line of the note text file: a message number with none before it or a multiple of ten) (ARC_AREA_READING_NOTE_CENSUS=FILE, with the Steamwoven <c>bar</c> smoke):
    /// how many split into address, body and signature (and fit that way), how many are drawn whole in the readable face, how many the face does not hold. The file gets start lines and reasons only, never a text
    /// of the original data; the log gets the counts.
    /// </summary>
    private void ReadingNoteCensus(string path)
    {
        if (_areaSession is not { } session || _readingData?.Data is not { } data || AdoptReadingArt() is not { } art) { GD.Print("ARC-UI-2765 note census: no reading data or art"); return; }
        var fonts = art.Fonts.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0).ToDictionary(load => load.Number, load => new AreaArtFont(load.Frames));
        if (!AreaReadingArtLayout.NoteBodyFonts.All(fonts.ContainsKey)) { GD.Print("ARC-UI-2765 note census: the readable face did not read"); return; }
        AreaArtFont FontOf(int number) => fonts.TryGetValue(number, out var font) ? font : fonts[AreaReadingArtLayout.NoteFont];
        var rows = new List<string>();
        var counts = new SortedDictionary<string, int>();
        void Count(string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
        var texts = data.Texts;
        var ambiguous = 0;
        for (var line = 0; line < 40_000; line++)
        {
            var first = session.QueryWrittenDocument(new((int)WrittenSubtype.Note, line), texts);
            if (!first.Readable || first.Body.Count == 0) continue;
            if (line % 10 != 0 && line > 0 && session.QueryWrittenDocument(new((int)WrittenSubtype.Note, line - 1), texts) is { Readable: true, Body.Count: > 0 }) continue;
            if (AreaReading.View(first, texts, 0, AreaHoverIndex.TextProfile, session.Rules.Written) is not { Art: { } document }) continue;
            Count("notes");
            var (parts, reason) = AreaReadingNoteRule.Check(document.Joined);
            var lines = AreaReadingFlow.Single(document, FontOf, readableNote: true);
            var drawn = lines.Count == 0 ? "empty" : lines.All(l => l.Font == AreaReadingArtLayout.NoteFont) ? "pepper-all"
                : lines.All(l => l.Font != AreaReadingArtLayout.NoteFont) ? "readable-all-" + lines[0].Font : "mixed-" + lines.First(l => l.Font != AreaReadingArtLayout.NoteFont).Font;
            Count("rule " + (parts is null ? "missed" : "matched"));
            if (reason is not null) Count("miss " + reason);
            Count("drawn " + drawn);
            // The second line from the end is short but not a closing line (it stays body): how often the stricter reading of "one or two short lines" differs.
            var trimmed = document.Joined.TrimEnd().Split('\n');
            if (parts is not null && trimmed.Length > 3 && trimmed[^2].Trim().Length is > 0 and <= AreaReadingNoteRule.MaxSignatureLine && !AreaReadingNoteRule.IsClosing(trimmed[^2])) ambiguous++;
            // Shape only, no text: the first line's length and last character, the last line's length.
            var shape = document.Joined.Trim('\n', ' ').Split('\n');
            var firstEnd = shape[0].TrimEnd().Length == 0 ? "none" : "'" + shape[0].TrimEnd()[^1] + "'";
            rows.Add($"{line}\tfirst={shape[0].Trim().Length}{firstEnd}\tlast={shape[^1].Trim().Length}\tlines={document.Joined.Split('\n').Length}\tchars={document.Joined.Length}\trule={(parts is null ? "missed:" + reason : "matched")}\tdrawn={drawn}");
        }
        GD.Print("ARC-UI-2765 note census short second-last line kept as body: " + ambiguous);
        System.IO.File.WriteAllLines(System.IO.Path.GetFullPath(path), rows);
        GD.Print("ARC-UI-2765 note census " + string.Join(", ", counts.Select(c => $"{c.Key}={c.Value}")) + " file=" + path);
    }
}
