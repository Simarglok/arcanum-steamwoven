using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-1112 (ledger FW1–FW6): the layout of the original's Fate Point window (0x56FC70, art 292), read from the 1.0.7.4 executable. The window
/// is the interface art 292 at (0, 41) of the 800 × 600 screen, of its art's size, with twelve lines of text (<c>mes\fate_ui.mes</c> 0..11, font
/// art 229, white) at x = 11 and y = 3 + 18 × kind, and twelve toggle buttons of art 293 at x = 223 and y = 4 + 18 × kind, one for each fate kind
/// (the same number as <see cref="FateKind"/>). Godot-free.
/// </summary>
public static class AreaFateLayout
{
    public const int BackgroundArt = 292, ButtonArt = 293, FontArt = 229;
    /// <summary>The twelve fate kinds, each with one line and one button (0x56FC70 builds twelve of each).</summary>
    public const int Kinds = 12;
    /// <summary>The window's top left on the screen: the same place as the other windows between the strips (SZ13).</summary>
    public const int ScreenX = 0, ScreenY = 41;
    public const int ButtonX = 223, ButtonTop = 4, TextX = 11, TextTop = 3, RowStep = 18;
    /// <summary>The path of the lines (0x56FAE0).</summary>
    public const string MessagePath = "mes/fate_ui.mes";
    /// <summary>The text's colour: the font style takes entry 255 of each colour table (0x56FAE0), white.</summary>
    public static (byte R, byte G, byte B) TextColour { get; } = (255, 255, 255);

    /// <summary>The interface art the window needs: the window, the buttons and the font.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, ButtonArt, FontArt];

    public static (int X, int Y) ButtonAt(int kind) => InRange(kind) ? (ButtonX, ButtonTop + RowStep * kind) : throw new ArgumentOutOfRangeException(nameof(kind));

    /// <summary>The top left of a line (the line's rectangle is <c>width − 11</c> wide and 18 high, 0x56FC70).</summary>
    public static (int X, int Y) TextAt(int kind) => InRange(kind) ? (TextX, TextTop + RowStep * kind) : throw new ArgumentOutOfRangeException(nameof(kind));

    public static bool InRange(int kind) => kind is >= 0 and < Kinds;

    /// <summary>The button of <paramref name="kind"/> that holds the window point (<paramref name="x"/>, <paramref name="y"/>) when every button is
    /// <paramref name="buttonWidth"/> × <paramref name="buttonHeight"/>; null when none does. The buttons do not overlap.</summary>
    public static int? KindAt(double x, double y, int buttonWidth, int buttonHeight)
    {
        if (buttonWidth <= 0 || buttonHeight <= 0) return null;
        for (var kind = 0; kind < Kinds; kind++)
        {
            var (bx, by) = ButtonAt(kind);
            if (x >= bx && x < bx + buttonWidth && y >= by && y < by + buttonHeight) return kind;
        }
        return null;
    }
}

/// <summary>The lines of the window: <c>mes\fate_ui.mes</c> 0..11 when it reads, otherwise the project's English names with the reason in <see cref="Detail"/>.</summary>
public sealed class AreaFateTexts
{
    private const int MaxText = 96;
    private static readonly IReadOnlyList<string> ProjectNames =
    [
        "Full heal", "Force good reaction", "Critical hit", "Critical miss", "Save against magick", "Spell at maximum",
        "Gambling", "Heal", "Pick pocket", "Repair", "Pick locks", "Disarm traps",
    ];
    private readonly IReadOnlyDictionary<int, string> _game;

    private AreaFateTexts(IReadOnlyDictionary<int, string> game, string detail) { _game = game; Detail = detail; }

    public string Detail { get; }
    /// <summary>How many of the twelve lines the game's file gave.</summary>
    public int GameCount => _game.Count;
    public bool FromGame => _game.Count > 0;

    public static AreaFateTexts Project(string reason) => new(new Dictionary<int, string>(), reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>Reads <c>mes\fate_ui.mes</c> as the window's init does (0x56FAE0); a failure gives the project's names.</summary>
    public static AreaFateTexts Load(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (AreaStartingHero.Table(stack, AreaFateLayout.MessagePath, ContentLoadPhase.GameStart, required: true, out var detail) is not { } table)
            return Project($"{AreaFateLayout.MessagePath} not read ({detail})");
        var kept = new Dictionary<int, string>();
        for (var kind = 0; kind < AreaFateLayout.Kinds; kind++)
            if (table.Find(kind) is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 and <= MaxText } text })
                kept[kind] = text;
        return new(kept, $"{AreaFateLayout.MessagePath}: {kept.Count} of {AreaFateLayout.Kinds} lines");
    }

    /// <summary>The line of a kind: the game's, else the project's name.</summary>
    public string Line(int kind) => AreaFateLayout.InRange(kind)
        ? _game.GetValueOrDefault(kind) ?? ProjectNames[kind] : throw new ArgumentOutOfRangeException(nameof(kind));
}

/// <summary>What the window draws from the game folder: the interface arts of <see cref="AreaFateLayout.ArtNumbers"/> and the lines. Godot-free.</summary>
public sealed record AreaFateContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, AreaFateTexts Texts, string Detail)
{
    /// <summary>True when every art the window needs read (a window without them has no look to show).</summary>
    public bool Ready => AreaFateLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0);

    /// <summary>No game folder (the self-test): no art, the project's names.</summary>
    public static AreaFateContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), AreaFateTexts.Project(reason), reason);

    public static AreaFateContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        // The font has 225 frames.
        var art = AreaInterfaceArt.Load(stack, AreaFateLayout.ArtNumbers, new AreaInterfaceArtLimits(MaxFrames: 256, MaxRgbaBytes: 32L * 1048576));
        var texts = AreaFateTexts.Load(stack);
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, texts, $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}; {texts.Detail}");
    }
}

/// <summary>What one pointer event on a fate button did: the sounds to play in order and the change the host runs (the kind and whether it is now on:
/// on arms the fate, off undoes it).</summary>
public sealed record AreaFateEffect(IReadOnlyList<int> Sounds, int? Kind, bool On)
{
    public static AreaFateEffect None { get; } = new([], null, false);
}

/// <summary>
/// ARC-UI-1112 (FW3, FW4): the pointer state of the twelve fate buttons. Each is a toggle (<see cref="AreaBarButtonKind.Toggle"/>, flag 2 of 0x54AA60):
/// a press flips it, sound 3000 turning on and 3001 turning off, and the host acts on both (on arms the kind, off undoes it, 0x56FE70). The window's
/// own state is the PC's: the host sets every button from the armed kinds (<see cref="Sync"/>), which is how a refused press pops the button back
/// (0x537FE0) and how a kind used by an event, or the full heal, which is used at once, pops its button (the callback 0x56FF40). A button shows
/// as <see cref="AreaBarButtonLook"/> does for the bars: down when on, hovered under the pointer, else idle. Caller-serialized.
/// </summary>
public sealed class AreaFateButtons
{
    private readonly bool[] _on = new bool[AreaFateLayout.Kinds];

    /// <summary>The button the pointer is over; null none.</summary>
    public int? Hovered { get; private set; }

    public bool IsOn(int kind) => AreaFateLayout.InRange(kind) ? _on[kind] : throw new ArgumentOutOfRangeException(nameof(kind));

    public AreaBarButtonLook Look(int kind) => IsOn(kind) ? AreaBarButtonLook.Down : Hovered == kind ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;

    public void Enter(int kind) { if (!AreaFateLayout.InRange(kind)) throw new ArgumentOutOfRangeException(nameof(kind)); Hovered = kind; }

    public void Exit(int kind) { if (Hovered == kind) Hovered = null; }

    /// <summary>A left press on a button: it flips and the host is told the new state.</summary>
    public AreaFateEffect Press(int kind)
    {
        if (!AreaFateLayout.InRange(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        _on[kind] = !_on[kind];
        return new([_on[kind] ? AreaGameBarLayout.PressSound : AreaGameBarLayout.ReleaseSound], kind, _on[kind]);
    }

    /// <summary>Sets the buttons from the PC's fate (a kind is on when it is armed, FT2); the full heal is never armed. Without a state every button is up.</summary>
    public void Sync(FateState? fate)
    {
        for (var kind = 0; kind < AreaFateLayout.Kinds; kind++)
            _on[kind] = fate is { } state && FateRules.IsArmed(state, (FateKind)kind);
    }

    /// <summary>Every button up and the pointer away (the window closed).</summary>
    public void Reset() { Array.Clear(_on); Hovered = null; }
}

/// <summary>
/// ARC-UI-1112 (FW1, FW2, FW5): when the window opens, closes and what the host asks of the session. The Fate button and the key F call the same
/// function (0x56FBF0): an open window closes; otherwise it opens for a living PC (an object that is missing or has less than one hit point does
/// not open it). Space closes it (the window's own handler, 0x56FE70, takes the key, scan code 0x39). Godot-free.
/// </summary>
public static class AreaFateWindowRules
{
    /// <summary>What the Fate button or the key F did.</summary>
    public enum Toggle { Closed, Opened, RefusedNoHero, RefusedDead }

    /// <summary>The decision of 0x56FBF0 for the hero (<paramref name="hasHero"/>, <paramref name="dead"/>) and the window's state.</summary>
    public static Toggle Decide(bool open, bool hasHero, bool dead) =>
        open ? Toggle.Closed : !hasHero ? Toggle.RefusedNoHero : dead ? Toggle.RefusedDead : Toggle.Opened;
}
