using Arcanum.Data.Content;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One placed line of art-font text: its offset from the text's rectangle and its glyphs (each with its x from the line's left).</summary>
public sealed record AreaArtTextLine(int X, int Y, IReadOnlyList<(int X, AreaArtGlyph Glyph)> Glyphs);

/// <summary>The lines a text draw shows in a rectangle and whether the whole text fitted (<see cref="Fits"/>, the text library's success).</summary>
public sealed record AreaArtTextPlacement(IReadOnlyList<AreaArtTextLine> Lines, bool Fits);

/// <summary>
/// ARC-DEV-156 (B27, B28): the two text fonts of the original's in-game windows, as art fonts: the title font (font art 27
/// <c>morph15font</c>, white; the message window's titles, ledger ML6) and the text font (font art 229 <c>Flare12Font</c>, white; the
/// message window's texts and pane names, ML6/HK21, the floaters, CH10). A font's colour is applied at the draw. Godot-free.
/// </summary>
public static class AreaTextFontLayout
{
    /// <summary>ARC-UI-124: the number font (art 230 <c>Flare14Font</c>, white, 0x64C4A0) of the combat panel's hit chance.</summary>
    public const int TitleArt = 27, TextArt = 229, NumberArt = 230;
    public static IReadOnlyList<int> ArtNumbers { get; } = [TitleArt, TextArt, NumberArt];
    /// <summary>The arts without which no original font is used (the number font is optional).</summary>
    public static IReadOnlyList<int> RequiredArtNumbers { get; } = [TitleArt, TextArt];

    /// <summary>The line height of a font's frames: its tallest glyph (PROJECT, as the dialog box's and MW26's; the text draw's own
    /// height getter is not read for the floaters, whose line is this height + 2, CH11).</summary>
    public static int LineHeight(IReadOnlyList<AreaInterfaceFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        return frames.Count == 0 ? 0 : frames.Max(frame => frame.Height);
    }

    /// <summary>
    /// A text in <paramref name="rect"/> (its width and height; the lines' offsets are from its top left) as the message window's text
    /// draw 0x550A10 shows it (ML4): the text broken at the rectangle's width (MW26) and drawn from its top left while the height
    /// takes one more line of <paramref name="lineHeight"/> (at least one line is drawn; the rest is not drawn, <see cref="AreaArtTextPlacement.Fits"/> false);
    /// with <paramref name="right"/> a text that is one line and narrower than the rectangle ends at its right edge (flag 0x2); with
    /// <paramref name="centre"/> (ARC-UI-124, flag 0x4) it starts at the free width halved.
    /// </summary>
    public static AreaArtTextPlacement Place(AreaArtFont font, int lineHeight, (int X, int Y, int Width, int Height) rect, string text, bool right = false,
        bool centre = false)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        var wrapped = AreaPremadeBrowserArtLayout.Wrap(font, text, rect.Width, out var complete);
        // At least one line, as the viewer's labels always drew one (a font taller than the rectangle must not blank the window).
        var room = Math.Max(1, rect.Height / lineHeight);
        var x = 0;
        if ((right || centre) && wrapped.Count == 1 && wrapped[0] is { Count: > 0 } only && only[^1].X + only[^1].Glyph.Width is var width && width < rect.Width)
            x = right ? rect.Width - width : (rect.Width - width) / 2;
        var lines = wrapped.Take(room).Select((line, index) => new AreaArtTextLine(x, index * lineHeight, line)).ToArray();
        return new(lines, complete && wrapped.Count <= room);
    }

    /// <summary>A one-line name centred both ways in <paramref name="rect"/> (a pane's hover name, HK21): a narrower line starts at the
    /// free width halved toward zero, a wider one at the rectangle's left (0x542DF0); the line's top is the free height halved toward zero.</summary>
    public static AreaArtTextPlacement Centred(AreaArtFont font, int lineHeight, (int X, int Y, int Width, int Height) rect, string text)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        var glyphs = font.Layout(text);
        var width = font.Measure(text).Width;
        var x = width < rect.Width ? (rect.Width - width) / 2 : 0;
        return new([new(x, (rect.Height - lineHeight) / 2, glyphs)], true);
    }

    /// <summary>A free-standing text (a tooltip's): its lines (split at line feeds) one under the other from (0, 0), and the size they take.</summary>
    public static (AreaArtTextPlacement Placement, int Width, int Height) Block(AreaArtFont font, int lineHeight, string text)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        var parts = text.Split('\n');
        var lines = parts.Select((part, index) => new AreaArtTextLine(0, index * lineHeight, font.Layout(part))).ToArray();
        return (new(lines, true), parts.Max(part => font.Measure(part).Width), parts.Length * lineHeight);
    }
}

/// <summary>ARC-DEV-156: the two text fonts' art (<see cref="AreaTextFontLayout.ArtNumbers"/>) read from the game folder, the title and text fonts all or nothing and the number font optional.</summary>
public sealed record AreaTextFontContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, string Detail)
{
    public bool ArtReady => AreaTextFontLayout.RequiredArtNumbers.All(Ready);

    /// <summary>ARC-UI-124: the combat panel's number font (art 230) read; without it the text font draws the number.</summary>
    public bool NumberArtReady => Ready(AreaTextFontLayout.NumberArt);

    private bool Ready(int number) => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0;

    public static AreaTextFontContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), reason);

    /// <summary>Reads the art from the game folder of <paramref name="modulePath"/>; failures are reported, never thrown. The frame limit
    /// is raised to 256 for the fonts' frames (one per character 31..255).</summary>
    public static AreaTextFontContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, AreaTextFontLayout.ArtNumbers, new AreaInterfaceArtLimits(MaxFrames: 256));
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}");
    }
}
