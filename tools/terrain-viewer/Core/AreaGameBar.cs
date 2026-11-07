using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The two strips of the original's in-game interface (ledger GB1): the top strip window (0,0,800,41) and the bottom strip
/// window (0,441,800,159) of the 800×600 screen.</summary>
public enum AreaBarStrip { Top, Bottom }

/// <summary>The original's interface-bar buttons, in the order its interface init creates them (ledger GB2).</summary>
public enum AreaBarButton { Skills, Spells, Combat, Schematics, Logbook, Character, Inventory, Map, FatePoints, Sleep }

/// <summary>A push button acts on its release over it; a toggle stays down until pressed again (the original's button flag 2,
/// ledger GB3).</summary>
public enum AreaBarButtonKind { Push, Toggle }

/// <summary>What a button shows (the original's button states, ledger GB4): idle, held down (or a toggle that is on) and under the
/// pointer.</summary>
public enum AreaBarButtonLook { Idle, Down, Hovered }

/// <param name="X">The left edge on the original's 800×600 screen (the strip's own origin is subtracted when it is drawn).</param>
/// <param name="Art">The interface art number (<c>art\interface\interface.mes</c>).</param>
/// <param name="HoverMessage">The <c>mes\intgame.mes</c> message the original shows while the pointer is over the button (GB5).</param>
public sealed record AreaBarButtonSpec(AreaBarButton Button, AreaBarStrip Strip, int X, int Y, int Art, AreaBarButtonKind Kind, int HoverMessage);

/// <summary>
/// ARC-UI-018: the layout of the original's in-game interface bars, as the 1.0.7.4 executable builds it (ledger GB1–GB7): the two
/// strip windows with their art, the ten buttons with their screen position, art number, kind and hover message, and the button
/// sounds. Godot-free.
/// </summary>
public static class AreaGameBarLayout
{
    public const int ScreenWidth = 800, ScreenHeight = 600;
    public const int TopArt = 185, BottomArt = 184;
    public const int TopHeight = 41, BottomTop = 441, BottomHeight = 159;
    /// <summary>The bar buttons' sounds (GB6): the press plays 3000 and the release over the button 3001; the pointer entering or
    /// leaving plays nothing.</summary>
    public const int PressSound = 3000, ReleaseSound = 3001;
    /// <summary>The menu windows' text-only buttons (GB7): the pointer entering one plays 3026, a press 3027, a release nothing.</summary>
    public const int MenuHoverSound = 3026, MenuPressSound = 3027;
    /// <summary>ARC-UI-024: the PC's gauges' vial art (PA1): the fill art of health (18), health while poisoned (17) and fatigue
    /// (19), each 28 × 96, and the empty vial (20); the gauges draw frame 0 of each.</summary>
    public const int HealthVialArt = 18, PoisonedVialArt = 17, FatigueVialArt = 19, EmptyVialArt = 20;
    /// <summary>ARC-UI-024: the gauges' rectangles on the screen (0x5C63D8, CH7).</summary>
    public static readonly (int X, int Y, int Width, int Height) HealthGauge = (14, 472, 28, 88), FatigueGauge = (754, 473, 28, 88);
    /// <summary>ARC-UI-024: the hovered critter's image in the message window (PA6): its top left on the screen and, for a
    /// portrait, the portrait's own 64 × 64 size; the name goes in the panel's first line rectangle (0x5C70C8, the message window's
    /// object window, ARC-UI-057).</summary>
    public static readonly (int X, int Y) HoverImage = (217, 510);
    public const int HoverPortraitSize = 64;

    public static (int X, int Y, int Width, int Height) StripRect(AreaBarStrip strip) =>
        strip == AreaBarStrip.Top ? (0, 0, ScreenWidth, TopHeight) : (0, BottomTop, ScreenWidth, BottomHeight);

    public static int StripArt(AreaBarStrip strip) => strip == AreaBarStrip.Top ? TopArt : BottomArt;

    /// <summary>The buttons (GB2): bottom strip Skills, Spells, Combat, Schematics; top strip Logbook, Character, Inventory, Map, Fate
    /// Points and Sleep. The map button shows the town map art (193) the original starts with (its world map art 194 is set by a game
    /// callback, not ready).</summary>
    public static readonly IReadOnlyList<AreaBarButtonSpec> Buttons =
    [
        new(AreaBarButton.Skills, AreaBarStrip.Bottom, 693, 456, 472, AreaBarButtonKind.Toggle, 1001),
        new(AreaBarButton.Spells, AreaBarStrip.Bottom, 649, 494, 473, AreaBarButtonKind.Toggle, 1000),
        new(AreaBarButton.Combat, AreaBarStrip.Bottom, 86, 457, 470, AreaBarButtonKind.Push, 1002),
        new(AreaBarButton.Schematics, AreaBarStrip.Bottom, 693, 539, 471, AreaBarButtonKind.Push, 1003),
        new(AreaBarButton.Logbook, AreaBarStrip.Top, 41, 2, 187, AreaBarButtonKind.Push, 1004),
        new(AreaBarButton.Character, AreaBarStrip.Top, 4, 2, 169, AreaBarButtonKind.Push, 1005),
        new(AreaBarButton.Inventory, AreaBarStrip.Top, 115, 2, 186, AreaBarButtonKind.Push, 1006),
        new(AreaBarButton.Map, AreaBarStrip.Top, 78, 2, 193, AreaBarButtonKind.Push, 1007),
        new(AreaBarButton.FatePoints, AreaBarStrip.Top, 157, 9, 137, AreaBarButtonKind.Push, 1008),
        new(AreaBarButton.Sleep, AreaBarStrip.Top, 605, 9, 137, AreaBarButtonKind.Push, 1009),
    ];

    public static AreaBarButtonSpec Spec(AreaBarButton button) => Buttons.First(spec => spec.Button == button);

    /// <summary>Every interface art number the bars draw.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } =
        [TopArt, BottomArt, .. Buttons.Select(spec => spec.Art).Distinct(), HealthVialArt, PoisonedVialArt, FatigueVialArt, EmptyVialArt];

    /// <summary>The frame a button draws (GB4): idle frame 0; down advances one frame and hovered two, each step wrapping to frame 0
    /// past the art's last frame (so a two-frame art shows frame 0 when hovered, a one-frame art always frame 0).</summary>
    public static int Frame(AreaBarButtonLook look, int frames)
    {
        if (frames <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
        var steps = look switch { AreaBarButtonLook.Down => 1, AreaBarButtonLook.Hovered => 2, _ => 0 };
        var frame = 0;
        for (var i = 0; i < steps; i++) frame = frame + 1 >= frames ? 0 : frame + 1;
        return frame;
    }
}

/// <summary>What one pointer event on the bars did: the sounds to play in order, the button whose function runs (a push button's
/// release over it, either change of a toggle), and the hover name to show (null clears it; unchanged when <see cref="HoverChanged"/>
/// is false). <see cref="LetGo"/> is the other toggle of the Spells/Skills pair that a toggle turning on let go silently (the host
/// sets it on again when it refuses the press).</summary>
public sealed record AreaBarEffect(IReadOnlyList<int> Sounds, AreaBarButton? Act, bool HoverChanged, int? HoverMessage, AreaBarButton? LetGo = null)
{
    public static AreaBarEffect None { get; } = new([], null, false, null);
}

/// <summary>
/// ARC-UI-018: the pointer state of the original's interface-bar buttons, Godot-free (ledger GB3–GB6, the original's button library
/// at 0x537B50–0x5385C0 and the in-game filter 0x54B5D0/0x54DC80). A push button goes down on a press (sound 3000) and acts on the
/// release over it (sound 3001); a release elsewhere only lets it go. A toggle flips on each press: on plays 3000, off 3001, and the
/// host acts on both. Spells and Skills are one choice: turning one on lets the other go silently. The pointer entering a button shows
/// its hover message and its hover frame (not while it is down); leaving clears the message. A held push button shows down while the
/// pointer is over it and idle away from it, and no other button takes the pointer until the release. Caller-serialized.
/// </summary>
public sealed class AreaGameBar
{
    private readonly HashSet<AreaBarButton> _on = [];

    /// <summary>The button the pointer is over; null none.</summary>
    public AreaBarButton? Hovered { get; private set; }
    /// <summary>The push button held down by the pointer; null none.</summary>
    public AreaBarButton? Held { get; private set; }
    /// <summary>The hover message shown (GB5); null none.</summary>
    public int? HoverMessage { get; private set; }

    public bool IsOn(AreaBarButton button) => _on.Contains(button);

    public AreaBarButtonLook Look(AreaBarButton button) =>
        _on.Contains(button) || (Held == button && Hovered == button) ? AreaBarButtonLook.Down
        : Held is null && Hovered == button ? AreaBarButtonLook.Hovered : AreaBarButtonLook.Idle;

    /// <summary>The pointer entered <paramref name="button"/>. While a push button is held only that button takes the pointer (it
    /// shows down again over it, idle away from it).</summary>
    public AreaBarEffect Enter(AreaBarButton button)
    {
        if (Held is { } held && held != button) return AreaBarEffect.None;
        Hovered = button;
        HoverMessage = AreaGameBarLayout.Spec(button).HoverMessage;
        return new([], null, true, HoverMessage);
    }

    /// <summary>The pointer left <paramref name="button"/>: its hover message is cleared (0x54DC80).</summary>
    public AreaBarEffect Exit(AreaBarButton button)
    {
        if (Hovered != button) return AreaBarEffect.None;
        Hovered = null; HoverMessage = null;
        return new([], null, true, null);
    }

    /// <summary>A left press on <paramref name="button"/>.</summary>
    public AreaBarEffect Press(AreaBarButton button)
    {
        if (AreaGameBarLayout.Spec(button).Kind == AreaBarButtonKind.Push)
        {
            Held = button;
            return new([AreaGameBarLayout.PressSound], null, false, null);
        }
        if (_on.Remove(button)) return new([AreaGameBarLayout.ReleaseSound], button, false, null);
        _on.Add(button);
        // The other of the Spells/Skills pair goes up without a sound (the filter sets its state, GB3).
        var other = button == AreaBarButton.Spells ? AreaBarButton.Skills : AreaBarButton.Spells;
        var letGo = _on.Remove(other) ? other : (AreaBarButton?)null;
        return new([AreaGameBarLayout.PressSound], button, false, null, letGo);
    }

    /// <summary>The left button was released; <paramref name="over"/> is the button under the pointer then (null none).</summary>
    public AreaBarEffect Release(AreaBarButton? over)
    {
        if (Held is not { } held) return AreaBarEffect.None;
        Held = null;
        return over == held ? new([AreaGameBarLayout.ReleaseSound], held, false, null) : AreaBarEffect.None;
    }

    /// <summary>Forgets the pointer (hover, held button and hover message) and keeps the toggles: the bars were hidden, so no
    /// leave or release reaches them.</summary>
    public void Reset()
    {
        Hovered = null; Held = null; HoverMessage = null;
    }

    /// <summary>Sets a toggle without a sound or an action, for the host to follow its own state (a pane closed by its key).</summary>
    public void SetOn(AreaBarButton button, bool on)
    {
        if (AreaGameBarLayout.Spec(button).Kind != AreaBarButtonKind.Toggle) throw new ArgumentOutOfRangeException(nameof(button));
        if (on) _on.Add(button); else _on.Remove(button);
    }
}

/// <summary>The hover names of the bar buttons: the installed <c>mes\intgame.mes</c> 1000–1009 when it reads, otherwise the project's
/// English text with the reason in <see cref="Detail"/>. ARC-UI-035: also a spell hint's labels (intgame.mes 59 the cost, 73 the
/// minimum willpower, 74 and 75 the maintenance's "second" and "seconds", HK20) and the skill-bar buttons' names (<c>mes\skill_ui.mes</c>
/// 100–103, 0x57A700, which a hover in a pane mode writes). Godot-free.</summary>
public sealed class AreaGameBarTexts
{
    public const string IntgamePath = "mes/intgame.mes", SkillUiPath = "mes/skill_ui.mes";
    /// <summary>The spell hint's labels in <c>intgame.mes</c>.</summary>
    public const int CostLabel = 59, WillpowerLabel = 73, SecondLabel = 74, SecondsLabel = 75;
    /// <summary>The first skill-bar button name in <c>skill_ui.mes</c> (the skill bar init 0x579ED5..0x579F1C: 100 + button).</summary>
    public const int FirstSkillButtonName = 100;
    private const int MaxText = 64;
    private static readonly IReadOnlyDictionary<int, string> ProjectText = new Dictionary<int, string>
    {
        [1000] = "Spells", [1001] = "Skills", [1002] = "Combat Toggle", [1003] = "Schematics", [1004] = "Log Book",
        [1005] = "Character Editor", [1006] = "Inventory", [1007] = "Maps", [1008] = "Fate Points", [1009] = "Sleep",
        [CostLabel] = "Fatigue cost", [WillpowerLabel] = "Minimum willpower", [SecondLabel] = "second", [SecondsLabel] = "seconds",
    };
    private static readonly IReadOnlyList<string> ProjectSkillButtonNames = ["Prowling", "Pick Pocket", "Disarm Traps", "Repair"];
    private readonly IReadOnlyDictionary<int, string> _game;
    private readonly IReadOnlyDictionary<int, string> _skillButtons;
    // ARC-SKILLS-010: the skill bar's pick-pocket lines (skill_ui.mes 500 success, 501 failure, 525 a target that cannot be robbed).
    private static readonly IReadOnlyDictionary<int, string> ProjectSkillLines = new Dictionary<int, string>
    {
        [Arcanum.Simulation.Skills.PickPocketRules.SuccessLine] = "Pick pocket: success",
        [Arcanum.Simulation.Skills.PickPocketRules.FailureLine] = "Pick pocket: failure",
        [Arcanum.Simulation.Skills.PickPocketRules.NotAllowedLine] = "That pocket cannot be picked",
    };
    private readonly IReadOnlyDictionary<int, string> _skillLines;

    private AreaGameBarTexts(IReadOnlyDictionary<int, string> game, bool fromGame, string detail, IReadOnlyDictionary<int, string>? skillButtons = null,
        IReadOnlyDictionary<int, string>? skillLines = null)
    {
        _game = game; FromGame = fromGame; Detail = detail; _skillButtons = skillButtons ?? new Dictionary<int, string>();
        _skillLines = skillLines ?? new Dictionary<int, string>();
    }

    /// <summary>True when the file read and gave at least one name; <see cref="GameCount"/> says how many.</summary>
    public bool FromGame { get; }
    public string Detail { get; }
    /// <summary>The names the game's file gave (of <see cref="Count"/>).</summary>
    public int GameCount => _game.Count;
    public static int Count => ProjectText.Count;

    public static AreaGameBarTexts Project(string reason) => new(new Dictionary<int, string>(), false, reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>Reads <c>mes\intgame.mes</c> as the original's interface init does (game start); any failure gives the project's text.</summary>
    public static AreaGameBarTexts Load(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (AreaStartingHero.Table(stack, IntgamePath, ContentLoadPhase.GameStart, required: true, out var detail) is not { } table)
            return Project($"{IntgamePath} not read ({detail})");
        var kept = new Dictionary<int, string>();
        foreach (var number in ProjectText.Keys)
            if (Decoded(table, number) is { } text) kept[number] = text;
        // skill_ui.mes is the skill bar's own file (read at its init, 0x579ED5): a failure keeps the project's names.
        var buttons = new Dictionary<int, string>();
        var lines = new Dictionary<int, string>();
        var skillDetail = "";
        if (AreaStartingHero.Table(stack, SkillUiPath, ContentLoadPhase.GameStart, required: true, out var skillRead) is { } skillUi)
        {
            for (var button = 0; button < ProjectSkillButtonNames.Count; button++)
                if (Decoded(skillUi, FirstSkillButtonName + button) is { } name) buttons[button] = name;
            foreach (var number in ProjectSkillLines.Keys)
                if (Decoded(skillUi, number) is { } line) lines[number] = line;
            skillDetail = $"; {SkillUiPath}: {buttons.Count} of {ProjectSkillButtonNames.Count} names, {lines.Count} of {ProjectSkillLines.Count} lines";
        }
        else skillDetail = $"; {SkillUiPath} not read ({skillRead})";
        return new(kept, kept.Count > 0, $"{IntgamePath}: {kept.Count} of {ProjectText.Count} texts{skillDetail}", buttons, lines);
    }

    private static string? Decoded(MesFile table, int number) =>
        table.Find(number) is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 and <= MaxText } text }
            ? text : null;

    public string Text(int message) => _game.GetValueOrDefault(message) ?? ProjectText.GetValueOrDefault(message, $"[{message}]");

    /// <summary>ARC-UI-035: a skill-bar button's name (<c>skill_ui.mes</c> 100 + button, 0x57A700), else the project's.</summary>
    public string SkillButtonName(int button) =>
        _skillButtons.GetValueOrDefault(button) ?? (button is >= 0 and < 4 ? ProjectSkillButtonNames[button] : $"[skill button {button}]");

    /// <summary>ARC-SKILLS-010: a pick-pocket line of the skill bar (<c>skill_ui.mes</c> 500, 501, 525), else the project's.</summary>
    public string SkillLine(int number) => _skillLines.GetValueOrDefault(number) ?? ProjectSkillLines.GetValueOrDefault(number, $"[{number}]");

    /// <summary>How many of the pick-pocket lines the game's file gave.</summary>
    public int GameSkillLineCount => _skillLines.Count;

    /// <summary>How many skill-bar button names the game's file gave.</summary>
    public int GameSkillButtonCount => _skillButtons.Count;
}

public enum AreaInterfaceArtStatus { Ready, Unnamed, MissingFile, Unreadable, LimitExceeded }

/// <param name="Name">The file name <c>interface.mes</c> gives the number; null when it names none.</param>
/// <param name="FramesPerSecond">The art header's frame rate (ARC-UI-056: an animated cursor's).</param>
/// <param name="Identity">ARC-DEV-242: the HD pack key of the file (hash and length of the whole file, name hash); null unless read.</param>
public sealed record AreaInterfaceArtLoad(int Number, AreaInterfaceArtStatus Status, string? Name, IReadOnlyList<AreaInterfaceFrame> Frames, string Detail,
    int FramesPerSecond = 0, Arcanum.Data.Hd.HdInterfaceArtIdentity? Identity = null);

/// <summary>Explicit limits of the interface art one load reads: one file's size, frames per file and the pixels of all files.</summary>
public sealed record AreaInterfaceArtLimits(int MaxFileBytes = 4 * 1048576, int MaxFrames = 32, long MaxRgbaBytes = 16L * 1048576);

/// <summary>
/// ARC-UI-018: interface art by its number, as the original names it (ledger GB1): the art library's init reads
/// <c>art\interface\interface.mes</c> (at game start), and an interface art id's file is <c>art\interface\</c> + the message of its
/// number; the frames are drawn with palette slot 0. Each file is read from the content stack's winner. Godot-free.
/// </summary>
public static class AreaInterfaceArt
{
    public const string IndexPath = "art/interface/interface.mes";
    public const string Folder = "art/interface/";

    /// <summary>Reads the interface art of <paramref name="numbers"/>; each number gets its own outcome.</summary>
    public static IReadOnlyDictionary<int, AreaInterfaceArtLoad> Load(ContentStack stack, IEnumerable<int> numbers, AreaInterfaceArtLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(numbers);
        limits ??= new();
        if (limits.MaxFileBytes <= 0 || limits.MaxFrames <= 0 || limits.MaxRgbaBytes <= 0) throw new ArgumentOutOfRangeException(nameof(limits));
        var index = AreaStartingHero.Table(stack, IndexPath, ContentLoadPhase.GameStart, required: true, out var indexDetail);
        var loads = new Dictionary<int, AreaInterfaceArtLoad>();
        long rgbaBytes = 0;
        foreach (var number in numbers.Distinct())
        {
            if (index is null) { loads[number] = new(number, AreaInterfaceArtStatus.Unnamed, null, [], indexDetail ?? IndexPath); continue; }
            var load = Read(stack, index, number, limits, limits.MaxRgbaBytes - rgbaBytes);
            rgbaBytes += load.Frames.Sum(frame => (long)frame.Rgba.Length);
            loads[number] = load;
        }
        return loads;
    }

    /// <summary>The file name of an interface art number: its <c>interface.mes</c> message, trimmed; null when it has none.</summary>
    public static string? Name(MesFile index, int number)
    {
        ArgumentNullException.ThrowIfNull(index);
        return index.Find(number) is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } text }
            && text.Trim() is { Length: > 0 } name ? name : null;
    }

    /// <summary>ARC-DEV-242: one interface ART file by its file name (<c>IntBotom.ART</c>), not through <c>interface.mes</c>; number 0 in the result.</summary>
    public static AreaInterfaceArtLoad LoadNamed(ContentStack stack, string fileName, AreaInterfaceArtLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stack); ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        limits ??= new();
        return ReadFile(stack, 0, fileName.Trim(), limits, limits.MaxRgbaBytes);
    }

    private static AreaInterfaceArtLoad Read(ContentStack stack, MesFile index, int number, AreaInterfaceArtLimits limits, long rgbaLeft)
    {
        if (Name(index, number) is not { } name) return new(number, AreaInterfaceArtStatus.Unnamed, null, [], $"{IndexPath} has no message {number}");
        return ReadFile(stack, number, name, limits, rgbaLeft);
    }

    private static AreaInterfaceArtLoad ReadFile(ContentStack stack, int number, string name, AreaInterfaceArtLimits limits, long rgbaLeft)
    {
        var path = Folder + name;
        if (stack.Resolve(path).Winner is not { } winner) return new(number, AreaInterfaceArtStatus.MissingFile, name, [], path);
        if (winner.Length > limits.MaxFileBytes) return new(number, AreaInterfaceArtStatus.LimitExceeded, name, [], $"{path} {winner.Length} bytes");
        var read = stack.Read(winner);
        if (read.Status != ContentReadStatus.Read || read.Bytes is not { } bytes) return new(number, AreaInterfaceArtStatus.Unreadable, name, [], $"{path} {read.Status}");
        try
        {
            var options = new ArtDecoderOptions { AllowTrailingBytes = true };
            var metadata = ArtDecoder.Inspect(bytes, options);
            var count = metadata.Header.FramesPerGroup;
            if (count <= 0 || count > limits.MaxFrames) return new(number, AreaInterfaceArtStatus.LimitExceeded, name, [], $"{path} {count} frames");
            var frames = new List<AreaInterfaceFrame>(count);
            var identity = Arcanum.Data.Hd.HdInterfaceKey.Identity(name, bytes);
            long total = 0;
            for (var i = 0; i < count; i++)
            {
                var frame = ArtDecoder.Decode(bytes, new ArtFrameSelection(0, i, 0), options);
                total += frame.Rgba.Count;
                if (total > rgbaLeft) return new(number, AreaInterfaceArtStatus.LimitExceeded, name, [], $"{path}: more than {limits.MaxRgbaBytes} bytes of pixels");
                var rgba = frame.Rgba.ToArray();
                frames.Add(new(frame.Width, frame.Height, rgba, frame.Frame.HotX, frame.Frame.HotY,
                    Arcanum.Data.Hd.HdInterfaceKey.Query(identity, i, frame.Width, frame.Height, rgba)));
            }
            return new(number, AreaInterfaceArtStatus.Ready, name, frames, $"{path} {count} frames", metadata.Header.FramesPerSecond,
                identity);
        }
        catch (ArtException error)
        {
            return new(number, AreaInterfaceArtStatus.Unreadable, name, [], $"{path} {error.Message}");
        }
    }
}

/// <summary>ARC-UI-018: what the bars draw from the game folder: their interface art and their hover names. Godot-free.</summary>
public sealed record AreaGameBarContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, AreaGameBarTexts Texts, string Detail)
{
    /// <summary>Every interface art number read for the bars: theirs (<see cref="AreaGameBarLayout.ArtNumbers"/>), the message
    /// window's (<see cref="AreaMessageLayout.ArtNumbers"/>, ARC-UI-023) and the maintain bar's slots
    /// (<see cref="AreaMaintainBarLayout.ArtNumbers"/>, ARC-MAGIC-026).</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } =
        [.. AreaGameBarLayout.ArtNumbers.Concat(AreaMessageLayout.ArtNumbers).Concat(AreaMaintainBarLayout.ArtNumbers).Distinct()];

    /// <summary>True when every art number of <see cref="ArtNumbers"/> read.</summary>
    public bool ArtReady => ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready);

    /// <summary>No game folder (the self-test): no art, the project's texts.</summary>
    public static AreaGameBarContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), AreaGameBarTexts.Project(reason), reason);

    /// <summary>Reads the bars' art and texts from the game folder of <paramref name="modulePath"/>; failures are reported, never thrown.</summary>
    public static AreaGameBarContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, ArtNumbers);
        var texts = AreaGameBarTexts.Load(stack);
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, texts, $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}; {texts.Detail}");
    }
}
