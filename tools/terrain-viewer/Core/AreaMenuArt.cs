using Arcanum.Data.Content;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-058: the original look of the menu's button windows (main 2, single player 5, new game 11, in-play 3 and 26), read from the
/// window table 0x5C3A40 and the window build 0x546340 (ledger MW1–MW8). Each window's background art fills the 800 × 600 screen (an
/// art 600 rows high is drawn at (0, 0); a shorter one goes to (0, 41) between the strips 336 and 335, which none of these windows
/// uses); its buttons are text only (no art), the i-th at (410, 143 + 50 × i), centred on x (flag 4), in the font set of flag 8 (the
/// Morph30 art font 327). A label is drawn three times: black at (−1, −1), brown at (+1, +1) and the face colour at (0, 0); the face is
/// dark red, bright red while the pointer is over the button (event 2, back on event 3). The hit area is the label's measured size.
/// ARC-UI-059: the options window (6) is drawn the same way over its own background 556 `OptionsMenuBack` (800 × 600), its four page
/// buttons (window flags 0xD) at (130, 221 + 40 × i) (descriptor 0x5C44A0, button table 0x5C43D0; ledger MW7); its page's controls stand
/// in two columns of four slots (0x589280, MW8), each drawn in its own art (ARC-UI-061, <see cref="AreaOptionsControlLayout"/>). Godot-free.
/// </summary>
public static class AreaMenuArtLayout
{
    public const int ScreenWidth = 800, ScreenHeight = 600;
    public const int MainArt = 329, SinglePlayerArt = 331, FontArt = 327;
    public const int ButtonX = 410, FirstButtonY = 143, ButtonStep = 50;
    public const int OptionsArt = 556, OptionsButtonX = 130, OptionsFirstButtonY = 221, OptionsButtonStep = 40;

    /// <summary>The x of the options page's two slot columns (0x5CCD38) and the y of their four rows (0x5CCD58).</summary>
    public static IReadOnlyList<int> OptionsSlotColumns { get; } = [261, 531];
    public static IReadOnlyList<int> OptionsSlotRows { get; } = [122, 197, 272, 347];

    /// <summary>The colours of the three passes (r, g, b of the table 0x5C4040), in drawing order, with their offsets (table 0x5C4070,
    /// added in turn: −1, +2, −1).</summary>
    public static IReadOnlyList<(int Dx, int Dy, (byte R, byte G, byte B) Colour)> Passes { get; } =
        [(-1, -1, (0, 0, 0)), (1, 1, (97, 61, 42)), (0, 0, (100, 0, 0))];

    /// <summary>The face colour of a hovered button (the second font set, 0x64C234, third entry).</summary>
    public static (byte R, byte G, byte B) HoverFace { get; } = (240, 15, 15);

    /// <summary>ARC-UI-061: the options controls' art (control build 0x57FAD0, MW9): the slider's and the list's backgrounds (0x57FC20),
    /// the slider's bar (whose size is also the list's value field), the two arrows, the label font and the value font (0x57F500).</summary>
    public const int SliderArt = 688, ListArt = 671, BarArt = 623, LessArt = 667, MoreArt = 668, LabelFontArt = 27, ValueFontArt = 229;

    public static IReadOnlyList<int> ArtNumbers { get; } = [MainArt, SinglePlayerArt, FontArt, OptionsArt, SliderArt, ListArt, BarArt, LessArt, MoreArt,
        LabelFontArt, ValueFontArt];

    /// <summary>The background art of a button window (descriptor +0), or null for a window this look does not draw.</summary>
    public static int? Background(AreaMenuWindow window) => window switch
    {
        AreaMenuWindow.Main or AreaMenuWindow.NewGame or AreaMenuWindow.InPlay or AreaMenuWindow.InPlayLimited => MainArt,
        AreaMenuWindow.SinglePlayer => SinglePlayerArt,
        AreaMenuWindow.Options => OptionsArt,
        _ => null,
    };

    /// <summary>The place of the i-th label of <paramref name="size"/> on the 800 × 600 screen: its top-left is the button's point moved
    /// left by half the label's width (0x5469B3, the half rounded toward zero) and its size is the label's.</summary>
    public static (int X, int Y, int Width, int Height) Button(int slot, (int Width, int Height) size) => Button(AreaMenuWindow.Main, slot, size);

    /// <summary>As <see cref="Button(int, ValueTuple{int, int})"/> at <paramref name="window"/>'s button points (the options window's
    /// own, MW7; every other button window's MW2).</summary>
    public static (int X, int Y, int Width, int Height) Button(AreaMenuWindow window, int slot, (int Width, int Height) size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        var (x, y, step) = window == AreaMenuWindow.Options ? (OptionsButtonX, OptionsFirstButtonY, OptionsButtonStep) : (ButtonX, FirstButtonY, ButtonStep);
        return (x - size.Width / 2, y + step * slot, size.Width, size.Height);
    }

    /// <summary>The point of the options page's control slot 0..7 (the first column's four rows, then the second's; MW8).</summary>
    public static (int X, int Y) OptionsSlot(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, OptionsSlotColumns.Count * OptionsSlotRows.Count);
        return (OptionsSlotColumns[slot / OptionsSlotRows.Count], OptionsSlotRows[slot % OptionsSlotRows.Count]);
    }

    /// <summary>The label under a point of the 800 × 600 screen, or -1.</summary>
    public static int HitTest(IReadOnlyList<(int X, int Y, int Width, int Height)> buttons, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        for (var i = 0; i < buttons.Count; i++)
        {
            var (left, top, width, height) = buttons[i];
            if (x >= left && x < left + width && y >= top && y < top + height) return i;
        }
        return -1;
    }
}

/// <summary>ARC-UI-061: an options control's arrow (0x57FE60: the left lowers the value, 0x580390; the right raises it, 0x5803D0).</summary>
public enum AreaOptionsArrow { Less, More }

/// <summary>
/// ARC-UI-061: an options control in the original's look (control build 0x57FAD0 and its parts, ledger MW9–MW10). The control stands
/// at its slot's point (MW8): its background (688 `OptionSlider` for a slider, 671 `OptionMultiChoice` for a list) at the point, its
/// label centred on the background's width 2 rows lower in the label font (27 `morph15font`, white; grey when disabled), and, when
/// enabled, its two arrows (667 and 668, push buttons with the press sound 3000 and the release sound 3001, acting on the release) at
/// (+6, +21) and (+180, +21). A slider's bar 623 `SkilGauge` is drawn from its left edge at (+47, +24) as wide as value × the bar's width
/// / the top (rounded toward zero); the rest of the field is the background's. A list's value is its text centred on the bar's width at
/// (+47, +30) in the value font (229 `Flare12Font`, red). PROJECT: Steamwoven's shown controls take the page's first slots in order
/// (the hidden ones leave no gap, as the menu's hidden buttons, MW2); a text is one line (the text draw's line break 0x535C40 is not
/// modelled; every installed label and value fits its field). Godot-free.
/// </summary>
public static class AreaOptionsControlLayout
{
    public const int LabelDown = 2, LessX = 6, MoreX = 180, ArrowY = 21, FieldX = 47, BarY = 24, ValueY = 30;

    /// <summary>The label's face (font 0x6839B8) and a list value's (0x684244): the colour tables' entries 255 and red 255 alone
    /// (0x57F52C..0x57F661; a disabled control's grey 130, 0x684240, is not drawn: no Steamwoven control is disabled, OP4).</summary>
    public static (byte R, byte G, byte B) LabelFace { get; } = (255, 255, 255);
    public static (byte R, byte G, byte B) ValueFace { get; } = (255, 0, 0);

    /// <summary>The background art of a control (0x57FC20: a slider's 688, a list's 671).</summary>
    public static int Background(AreaOptionKind kind) => kind == AreaOptionKind.Slider ? AreaMenuArtLayout.SliderArt : AreaMenuArtLayout.ListArt;

    /// <summary>The point of the i-th shown control (PROJECT: the first slots in order).</summary>
    public static (int X, int Y) Place(int shown) => AreaMenuArtLayout.OptionsSlot(shown);

    /// <summary>The top-left of a text of <paramref name="width"/> centred on a field of <paramref name="fieldWidth"/> at x (font flag
    /// 0x10 of the text draw 0x535570: the free width halved toward zero).</summary>
    public static int Centred(int x, int fieldWidth, int width) => x + (fieldWidth - width) / 2;

    /// <summary>The label's top-left: centred on the background's width, 2 rows below the point (0x57FD80).</summary>
    public static (int X, int Y) Label((int X, int Y) point, int backgroundWidth, int width) => (Centred(point.X, backgroundWidth, width), point.Y + LabelDown);

    /// <summary>An arrow's top-left (0x57FE60).</summary>
    public static (int X, int Y) Arrow((int X, int Y) point, AreaOptionsArrow arrow) => (point.X + (arrow == AreaOptionsArrow.Less ? LessX : MoreX), point.Y + ArrowY);

    /// <summary>The value field's top-left: a slider's bar (0x580000) and a list's field (0x580190) start there.</summary>
    public static (int X, int Y) Field((int X, int Y) point) => (point.X + FieldX, point.Y + BarY);

    /// <summary>A list value's top-left: centred on the bar's width, 6 rows below the field (0x58031C).</summary>
    public static (int X, int Y) Value((int X, int Y) point, int barWidth, int width) => (Centred(point.X + FieldX, barWidth, width), point.Y + ValueY);

    /// <summary>The width of a slider's bar drawn for <paramref name="value"/> of <paramref name="top"/> (0x580076: value × width / top,
    /// a signed division).</summary>
    public static int Filled(int value, int top, int barWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);
        ArgumentOutOfRangeException.ThrowIfNegative(barWidth);
        if (value < 0 || value > top) throw new ArgumentOutOfRangeException(nameof(value));
        return value * barWidth / top;
    }
}

/// <summary>One glyph of an art font: its frame's size and its advance (the frame header's centre x, ledger MW4).</summary>
public readonly record struct AreaArtGlyph(int Frame, int Width, int Height, int Advance);

/// <summary>
/// ARC-UI-058: an interface art font (ledger MW4): a character's glyph is frame (character − 31) of the art (0x535D10, the character
/// as its byte); a text's width is the sum of its glyphs' advances (the frame header's centre x) except the last, which counts its
/// width, and its height the glyphs' height (0x535390). A character above 255 or without a frame has no glyph and is skipped.
/// </summary>
public sealed class AreaArtFont
{
    public const int FirstFrameCharacter = 31;
    private readonly IReadOnlyList<AreaInterfaceFrame> _frames;

    public AreaArtFont(IReadOnlyList<AreaInterfaceFrame> frames) => _frames = frames ?? throw new ArgumentNullException(nameof(frames));

    public AreaArtGlyph? Glyph(char character)
    {
        var frame = character - FirstFrameCharacter;
        if (character > 255 || frame < 0 || frame >= _frames.Count) return null;
        var art = _frames[frame];
        return new(frame, art.Width, art.Height, art.HotX);
    }

    /// <summary>The glyphs of a text with their x from its left edge.</summary>
    public IReadOnlyList<(int X, AreaArtGlyph Glyph)> Layout(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var placed = new List<(int, AreaArtGlyph)>(text.Length);
        var x = 0;
        foreach (var character in text)
        {
            if (Glyph(character) is not { } glyph) continue;
            placed.Add((x, glyph));
            x += glyph.Advance;
        }
        return placed;
    }

    public (int Width, int Height) Measure(string text)
    {
        var placed = Layout(text);
        if (placed.Count == 0) return (0, 0);
        var (x, last) = placed[^1];
        return (x + last.Width, placed.Max(glyph => glyph.Glyph.Height));
    }
}

/// <summary>ARC-UI-058: the menu art read from the game folder (<see cref="AreaMenuArtLayout.ArtNumbers"/>).</summary>
public sealed record AreaMenuArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, string Detail)
{
    /// <summary>True when every art read: the button windows then show the original look. All or nothing (ARC-UI-059: the options
    /// background 556 too; ARC-UI-061: the options controls' art), as every one of them is in the installed game.</summary>
    public bool ArtReady => AreaMenuArtLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready
        && load.Frames.Count > 0);

    public AreaArtFont? Font => ArtReady ? new(Art[AreaMenuArtLayout.FontArt].Frames) : null;

    /// <summary>ARC-UI-061: the options controls' label and value fonts (MW10).</summary>
    public AreaArtFont? LabelFont => ArtReady ? new(Art[AreaMenuArtLayout.LabelFontArt].Frames) : null;
    public AreaArtFont? ValueFont => ArtReady ? new(Art[AreaMenuArtLayout.ValueFontArt].Frames) : null;

    public static AreaMenuArtContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), reason);

    /// <summary>Reads the art from the game folder of <paramref name="modulePath"/>; failures are reported, never thrown. The frame limit
    /// is raised to 256 for the font's 225 frames (one per character 31..255).</summary>
    public static AreaMenuArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, AreaMenuArtLayout.ArtNumbers, new AreaInterfaceArtLimits(MaxFrames: 256));
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}");
    }
}
