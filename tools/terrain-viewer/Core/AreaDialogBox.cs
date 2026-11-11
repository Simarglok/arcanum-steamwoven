using Arcanum.Data.Content;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The buttons of the original's dialog box (0x51EE30): one Accept, one Cancel, or both (a yes/no question).</summary>
public enum AreaDialogBoxKind { Accept, Cancel, YesNo }

/// <summary>
/// ARC-WORLD-120: the original's common dialog box (0x51EA60; ledger DB1–DB4), the box of its yes/no questions such as the quit
/// question (SF3). A window 325 × 136 at the caller's point of the 800 × 600 screen (the quit question's (237, 232)) shows the
/// background 822 <c>DialogueBox</c> from its corner, the text in font art 229 <c>Flare12Font</c> in white with no flags (from the top
/// left of (30, 14, 265 × 65), lines broken as the text draw 0x535570 breaks them, MW26) and its buttons: a yes/no question 823
/// <c>Lil_Accept</c> at (73, 102) and 824 <c>Lil_Cancel</c> at (225, 102), a box with one button that button at (149, 102), all of the
/// window. The buttons have three frames (idle, down, lit; GB4), act on the release over them (GB3) and play no sound. Return, keypad
/// Enter and the first letter (Y, either case) answer yes, Escape and the second letter (N) no. Godot-free.
/// </summary>
public static class AreaDialogBoxLayout
{
    public const int BackgroundArt = 822, AcceptArt = 823, CancelArt = 824, FontArt = 229;
    public const int Width = 325, Height = 136;
    /// <summary>The quit question's window point (its top left) on the 800 × 600 screen (0x5416A0), which centres the box there.</summary>
    public static (int X, int Y) QuitPoint { get; } = (237, 232);
    /// <summary>The text's rectangle in the window (0x51ED40).</summary>
    public static (int X, int Y, int Width, int Height) Text { get; } = (30, 14, 265, 65);
    public static (byte R, byte G, byte B) Face { get; } = (255, 255, 255);
    public const char YesLetter = 'Y', NoLetter = 'N';
    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, AcceptArt, CancelArt, FontArt];

    /// <summary>The window points of the box's buttons (0x51EE30), the art and true for the yes (accept) button.</summary>
    public static IReadOnlyList<(int Art, bool Yes, int X, int Y)> Buttons(AreaDialogBoxKind kind) => kind switch
    {
        AreaDialogBoxKind.Accept => [(AcceptArt, true, 149, 102)],
        AreaDialogBoxKind.Cancel => [(CancelArt, false, 149, 102)],
        _ => [(AcceptArt, true, 73, 102), (CancelArt, false, 225, 102)],
    };

    /// <summary>The text's lines as the text draw shows them in <see cref="Text"/>: broken at its width (MW26), each glyph with its x
    /// from the line's left; lines are drawn while the rectangle's remaining height holds one more (a line that does not fit is not
    /// drawn, nor any after it).</summary>
    public static IReadOnlyList<IReadOnlyList<(int X, AreaArtGlyph Glyph)>> Lines(AreaArtFont font, string text, int lineHeight)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        var lines = AreaPremadeBrowserArtLayout.Wrap(font, text, Text.Width);
        return [.. lines.Take(Text.Height / lineHeight)];
    }
}

/// <summary>ARC-WORLD-120: the dialog box's art read from the game folder (<see cref="AreaDialogBoxLayout.ArtNumbers"/>), all or nothing.</summary>
public sealed record AreaDialogBoxContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, string Detail)
{
    public bool ArtReady => AreaDialogBoxLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready
        && load.Frames.Count > 0);

    public AreaArtFont? Font => ArtReady ? new(Art[AreaDialogBoxLayout.FontArt].Frames) : null;

    /// <summary>The font's line height: its tallest glyph (0 without the art).</summary>
    public int LineHeight => ArtReady ? Art[AreaDialogBoxLayout.FontArt].Frames.Max(frame => frame.Height) : 0;

    public static AreaDialogBoxContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), reason);

    /// <summary>Reads the art from the game folder of <paramref name="modulePath"/>; failures are reported, never thrown. The frame limit
    /// is raised to 256 for the font's frames (one per character 31..255).</summary>
    public static AreaDialogBoxContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, AreaDialogBoxLayout.ArtNumbers, new AreaInterfaceArtLimits(MaxFrames: 256));
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}");
    }
}
