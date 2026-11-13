using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-1363 (ledger SZ13, SWB6, SWB7): the layout of the original's sleep and wait window (0x57B4F0), read from the 1.0.7.4 executable. The window is
/// the interface art 565 at (573, 42) of the 800 × 600 screen, of its art's size (the Fate window's family, <see cref="AreaFateLayout"/>). Each length has
/// one push button of art 293 (the Fate window's toggle art, three frames) at x = 8 and y = 3 + 18 × row, created by the bars' button creator (0x54AA60:
/// press sound 3000, release sound 3001, none for the pointer), and one line of text (<c>mes\sleepUI.mes</c> 0..7, in the wait window 12..18, font art 229,
/// white, the style that 0x57B080 makes) at x = 36 and y = 2 + 18 × row, in a rectangle of the window's width less 36 and 18 high. Eight rows, seven in the
/// wait window. Godot-free.
/// </summary>
public static class AreaSleepLayout
{
    public const int BackgroundArt = 565, ButtonArt = 293, FontArt = 229;
    /// <summary>The window's top left on the screen (SZ13): the right edge of the play view.</summary>
    public const int ScreenX = 573, ScreenY = 42;
    public const int ButtonX = 8, ButtonTop = 3, TextX = 36, TextTop = 2, RowStep = 18, TextHeight = 18;
    /// <summary>The text's colour: the font style takes entry 255 of each colour table (0x57B080), white.</summary>
    public static (byte R, byte G, byte B) TextColour { get; } = (255, 255, 255);

    /// <summary>The interface art the window needs: the window, the buttons and the font.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, ButtonArt, FontArt];

    public static (int X, int Y) ButtonAt(int row) => row >= 0 ? (ButtonX, ButtonTop + RowStep * row) : throw new ArgumentOutOfRangeException(nameof(row));

    /// <summary>The top left of a row's line; its rectangle is <paramref name="windowWidth"/> − 36 wide and 18 high (0x57B4F0).</summary>
    public static (int X, int Y, int Width, int Height) TextField(int row, int windowWidth) =>
        row >= 0 ? (TextX, TextTop + RowStep * row, Math.Max(0, windowWidth - TextX), TextHeight) : throw new ArgumentOutOfRangeException(nameof(row));

    /// <summary>The row whose button holds the window point (<paramref name="x"/>, <paramref name="y"/>) when every button is
    /// <paramref name="buttonWidth"/> × <paramref name="buttonHeight"/>, of <paramref name="rows"/> rows; null when none does.</summary>
    public static int? RowAt(double x, double y, int buttonWidth, int buttonHeight, int rows)
    {
        if (buttonWidth <= 0 || buttonHeight <= 0) return null;
        for (var row = 0; row < rows; row++)
        {
            var (bx, by) = ButtonAt(row);
            if (x >= bx && x < bx + buttonWidth && y >= by && y < by + buttonHeight) return row;
        }
        return null;
    }
}

/// <summary>What the window draws from the game folder: the interface arts of <see cref="AreaSleepLayout.ArtNumbers"/>. The lines are
/// <see cref="AreaRestTexts"/>. Godot-free.</summary>
public sealed record AreaSleepContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, string Detail)
{
    /// <summary>True when every art the window needs read (a window without them keeps the project's plain panel).</summary>
    public bool Ready => AreaSleepLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0);

    /// <summary>No game folder (the self-test): no art.</summary>
    public static AreaSleepContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), reason);

    public static AreaSleepContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        // The font has 225 frames.
        var art = AreaInterfaceArt.Load(stack, AreaSleepLayout.ArtNumbers, new AreaInterfaceArtLimits(MaxFrames: 256, MaxRgbaBytes: 32L * 1048576));
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}");
    }
}

/// <summary>What one pointer event on a sleep button did: the sounds to play in order and the row whose button acts (a left release over the button
/// that was pressed).</summary>
public sealed record AreaSleepEffect(IReadOnlyList<int> Sounds, int? Act)
{
    public static AreaSleepEffect None { get; } = new([], null);
}

/// <summary>
/// ARC-UI-1363 (GB3, GB4, GB6): the pointer state of the window's push buttons, as the bars' push buttons: a press goes down (sound 3000), the release
/// over the same button acts (sound 3001), a release elsewhere only lets it go; the pointer entering shows the lit frame (not while another is held), and
/// a held button shows down while the pointer is over it and idle away from it. Caller-serialized.
/// </summary>
public sealed class AreaSleepButtons
{
    /// <summary>The row the pointer is over; null none.</summary>
    public int? Hovered { get; private set; }
    /// <summary>The row held down by the pointer; null none.</summary>
    public int? Held { get; private set; }
    /// <summary>The row the controller's focus is on (PROJECT; drawn lit); null none.</summary>
    public int? Focus { get; set; }

    public AreaBarButtonLook Look(int row) =>
        Held == row && Hovered == row ? AreaBarButtonLook.Down
        : Held is null && (Hovered == row || (Hovered is null && Focus == row)) ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;

    public void Enter(int row) { if (Held is null || Held == row) Hovered = row; }

    public void Exit(int row) { if (Hovered == row) Hovered = null; }

    public AreaSleepEffect Press(int row)
    {
        Held = row; Hovered = row;
        return new([AreaGameBarLayout.PressSound], null);
    }

    /// <summary>The left button was released; <paramref name="over"/> is the row under the pointer then (null none).</summary>
    public AreaSleepEffect Release(int? over)
    {
        if (Held is not { } held) return AreaSleepEffect.None;
        Held = null;
        return over == held ? new([AreaGameBarLayout.ReleaseSound], held) : AreaSleepEffect.None;
    }

    /// <summary>Forgets the pointer (the window closed or hid its buttons).</summary>
    public void Reset() { Hovered = null; Held = null; Focus = null; }
}
