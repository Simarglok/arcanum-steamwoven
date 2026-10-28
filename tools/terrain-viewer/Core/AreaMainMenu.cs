using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The original main-menu windows this menu models, by their window type in the original's window table (ledger MM2).</summary>
public enum AreaMenuWindow { Main = 2, SinglePlayer = 5, NewGame = 11, PickCharacter = 13 }

/// <summary>What a menu button does in Steamwoven.</summary>
public enum AreaMenuRole
{
    /// <summary>Opens <see cref="AreaMenuButton.Target"/> over the current window (the current one goes on the back stack).</summary>
    Open,
    /// <summary>Back to the previous window (the original's target -2).</summary>
    Back,
    LoadGame,
    LastSave,
    Exit,
    /// <summary>A button the original has whose function Steamwoven does not have yet; hidden (FR-UI-001).</summary>
    Unavailable,
}

public enum AreaMenuCommandKind
{
    None,
    /// <summary>Open the save list's load window; the menu waits under it (<see cref="AreaMainMenu.Resume"/> when it closes unused).</summary>
    LoadGame,
    /// <summary>Load the newest save (the original's "Last Save", window 9).</summary>
    LastSave,
    /// <summary>Start a new game as pre-made hero <see cref="AreaMenuCommand.Hero"/> (index in <see cref="AreaStartingHero.Premade"/>).</summary>
    StartNewGame,
    Exit,
}

public readonly record struct AreaMenuCommand(AreaMenuCommandKind Kind, int Hero = -1)
{
    public static AreaMenuCommand Nothing => new(AreaMenuCommandKind.None);
}

/// <param name="Message">The label's message in <c>mes\MainMenu.mes</c>; its hotkey letter is message + 10 (ledger MM3).</param>
/// <param name="Target">The window an <see cref="AreaMenuRole.Open"/> button opens.</param>
/// <param name="Unavailable">Why the button is hidden, for the window's note; null when it is shown.</param>
public sealed record AreaMenuButton(int Message, AreaMenuRole Role, AreaMenuWindow? Target = null, string? Unavailable = null)
{
    public bool Shown => Role != AreaMenuRole.Unavailable;
    public int HotkeyMessage => Message + AreaMainMenuTexts.HotkeyOffset;
}

/// <summary>
/// The labels and hotkey letters of the main menu: the installed <c>mes\MainMenu.mes</c> when it reads (the original's text in the
/// player's language), otherwise the project's English text with the reason in <see cref="Detail"/>. Godot-free.
/// </summary>
public sealed class AreaMainMenuTexts
{
    /// <summary>A button's hotkey letter is its label's message + 10 (original window build, ledger MM3).</summary>
    public const int HotkeyOffset = 10;
    public const string MainMenuPath = "mes/MainMenu.mes";
    private const int MaxLabel = 64;
    private static readonly IReadOnlyDictionary<int, string> ProjectText = new Dictionary<int, string>
    {
        [10] = "Single Player", [11] = "MultiPlayer", [12] = "Options", [13] = "Credits", [14] = "Exit Game",
        [20] = "S", [21] = "M", [22] = "O", [23] = "C", [24] = "E",
        [50] = "New Game", [51] = "Load Game", [52] = "Last Save", [53] = "View Intro", [54] = "Cancel",
        [60] = "N", [61] = "L", [62] = "S", [63] = "V", [64] = "C",
        [420] = "Pick Character", [421] = "New Character", [422] = "Cancel",
        [430] = "P", [431] = "N", [432] = "X",
    };
    private readonly Func<int, string?> _original;

    private AreaMainMenuTexts(Func<int, string?> original, bool fromGame, string detail)
    {
        _original = original; FromGame = fromGame; Detail = detail;
    }

    /// <summary>True when the labels come from the installed <c>MainMenu.mes</c>.</summary>
    public bool FromGame { get; }
    public string Detail { get; }

    /// <summary>The project's English text only (no game folder, or the self-test).</summary>
    public static AreaMainMenuTexts Project(string reason) => new(_ => null, false, reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>Texts over a message lookup (tests): a number it does not answer takes the project's text.</summary>
    public static AreaMainMenuTexts From(Func<int, string?> messages, string detail) =>
        new(messages ?? throw new ArgumentNullException(nameof(messages)), true, detail ?? throw new ArgumentNullException(nameof(detail)));

    /// <summary>Reads <c>mes\MainMenu.mes</c> from the game folder of <paramref name="modulePath"/>, from the sources the original
    /// reads it in (game start, ARC-DATA-027); any failure gives the project's text with the reason.</summary>
    public static AreaMainMenuTexts Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Project($"{MainMenuPath} not read ({found.Status} {found.Detail})");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Project($"{MainMenuPath} not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        if (AreaStartingHero.Table(stack, MainMenuPath, ContentLoadPhase.GameStart, required: true, out var detail) is not { } table)
            return Project($"{MainMenuPath} not read ({detail})");
        // Only the menu's own numbers are kept (the table is not held).
        var kept = new Dictionary<int, string>();
        foreach (var number in ProjectText.Keys)
            if (table.Find(number) is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 and <= MaxLabel } text })
                kept[number] = text;
        return new(number => kept.GetValueOrDefault(number), true, $"{MainMenuPath}: {kept.Count} of {ProjectText.Count} texts");
    }

    /// <summary>The text of a message: the game's when it has it, else the project's.</summary>
    public string Text(int message) => _original(message) is { Length: > 0 } text ? text : ProjectText.GetValueOrDefault(message, $"[{message}]");

    /// <summary>The hotkey of a button: the first character of its hotkey message, upper-cased (MM3); null when the message is empty.</summary>
    public char? Hotkey(AreaMenuButton button)
    {
        ArgumentNullException.ThrowIfNull(button);
        var text = Text(button.HotkeyMessage);
        return text.Length > 0 && !char.IsControl(text[0]) ? char.ToUpperInvariant(text[0]) : null;
    }
}

/// <summary>
/// ARC-UI-011 main menu state, Godot-free: the original's main, single player and new game windows and the pre-made hero browser
/// (ledger MM1–MM7). A button opens a window over the current one (a back stack), Cancel and Escape go back; the root window ignores
/// Escape, as the original's does the first time it opens (later openings, from play, exit the game on Escape). A button's hotkey letter activates
/// it. Buttons whose function Steamwoven lacks (network play, options, credits, intro movies, the character editor) are hidden and
/// named in the window's note (FR-UI-001: the menu shows only what the profile supports). "Pick Character" needs the start map
/// (the original's new game teleports the PC there); the host says whether the shown map is it. The pre-made browser starts at the
/// first hero and cycles through all twelve (the original: 1..count-1, twelve heroes when the female heroes' art exists). The host
/// runs the returned command. Caller-serialized.
/// </summary>
public sealed class AreaMainMenu
{
    public const string NoNetworkPlay = "network play is not built";
    public const string NoOptions = "the options window is a later task (N9)";
    public const string NoCredits = "the credits screen is not built (FR-MEDIA-002)";
    public const string NoIntro = "movies do not play (FR-MEDIA-001)";
    public const string NoEditor = "the character editor is not built";
    private readonly List<AreaMenuWindow> _back = [];

    /// <param name="newGameRefusal">Why a new game cannot start here (null: the shown map is the start map and a hero can be picked).</param>
    public AreaMainMenu(AreaMainMenuTexts texts, string? newGameRefusal)
    {
        Texts = texts ?? throw new ArgumentNullException(nameof(texts));
        NewGameRefusal = newGameRefusal;
    }

    public AreaMainMenuTexts Texts { get; }
    public string? NewGameRefusal { get; }
    public AreaMenuWindow Window { get; private set; } = AreaMenuWindow.Main;
    /// <summary>The browsed pre-made hero (index in <see cref="AreaStartingHero.Premade"/>).</summary>
    public int Hero { get; private set; }
    /// <summary>The focused shown button (controller); -1 none.</summary>
    public int Focus { get; private set; }
    public string? Notice { get; set; }
    public IReadOnlyList<AreaMenuWindow> BackStack => _back;

    /// <summary>The window's buttons in the original's order (hidden ones included; <see cref="Shown"/> lists the visible ones).</summary>
    public IReadOnlyList<AreaMenuButton> Buttons => Window switch
    {
        AreaMenuWindow.Main =>
        [
            new(10, AreaMenuRole.Open, AreaMenuWindow.SinglePlayer), new(11, AreaMenuRole.Unavailable, Unavailable: NoNetworkPlay),
            new(12, AreaMenuRole.Unavailable, Unavailable: NoOptions), new(13, AreaMenuRole.Unavailable, Unavailable: NoCredits), new(14, AreaMenuRole.Exit),
        ],
        AreaMenuWindow.SinglePlayer =>
        [
            new(50, AreaMenuRole.Open, AreaMenuWindow.NewGame), new(51, AreaMenuRole.LoadGame), new(52, AreaMenuRole.LastSave),
            new(53, AreaMenuRole.Unavailable, Unavailable: NoIntro), new(54, AreaMenuRole.Back),
        ],
        AreaMenuWindow.NewGame =>
        [
            NewGameRefusal is null ? new(420, AreaMenuRole.Open, AreaMenuWindow.PickCharacter) : new(420, AreaMenuRole.Unavailable, Unavailable: NewGameRefusal),
            new(421, AreaMenuRole.Unavailable, Unavailable: NoEditor), new(422, AreaMenuRole.Back),
        ],
        _ => [],
    };

    public IReadOnlyList<AreaMenuButton> Shown => Buttons.Where(button => button.Shown).ToArray();

    /// <summary>The hidden buttons of the window with the reason, for its note.</summary>
    public IReadOnlyList<string> HiddenNotes => Buttons.Where(button => !button.Shown).Select(button => $"{Texts.Text(button.Message)}: {button.Unavailable}").ToArray();

    /// <summary>Activates the <paramref name="index"/>-th shown button.</summary>
    public AreaMenuCommand Activate(int index)
    {
        var shown = Shown;
        if (index < 0 || index >= shown.Count) return AreaMenuCommand.Nothing;
        var button = shown[index];
        Notice = null;
        switch (button.Role)
        {
            case AreaMenuRole.Open: Open(button.Target!.Value); return AreaMenuCommand.Nothing;
            case AreaMenuRole.Back: Back(); return AreaMenuCommand.Nothing;
            case AreaMenuRole.LoadGame: return new(AreaMenuCommandKind.LoadGame);
            case AreaMenuRole.LastSave: return new(AreaMenuCommandKind.LastSave);
            case AreaMenuRole.Exit: return new(AreaMenuCommandKind.Exit);
            default: return AreaMenuCommand.Nothing;
        }
    }

    /// <summary>A typed character: the shown button whose hotkey letter it is (upper-cased first, as the original), if any.</summary>
    public AreaMenuCommand Type(char typed)
    {
        var upper = char.ToUpperInvariant(typed);
        var shown = Shown;
        for (var i = 0; i < shown.Count; i++)
            if (Texts.Hotkey(shown[i]) == upper) return Activate(i);
        return AreaMenuCommand.Nothing;
    }

    /// <summary>Escape: back to the previous window; true when it went back (the root window does nothing).</summary>
    public bool Back()
    {
        if (_back.Count == 0) return false;
        Window = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Focus = Shown.Count > 0 ? 0 : -1;
        Notice = null;
        return true;
    }

    /// <summary>Moves the focus over the shown buttons (controller), wrapping.</summary>
    public void Move(int direction)
    {
        var count = Window == AreaMenuWindow.PickCharacter ? 0 : Shown.Count;
        if (count == 0 || direction == 0) return;
        Focus = Focus < 0 ? 0 : ((Focus + Math.Sign(direction)) % count + count) % count;
    }

    /// <summary>Confirm (controller): the focused button, or in the browser the start with the browsed hero.</summary>
    public AreaMenuCommand Confirm() => Window == AreaMenuWindow.PickCharacter ? Start() : Activate(Focus);

    /// <summary>The browser's previous or next hero, cycling through all of them.</summary>
    public void Browse(int direction)
    {
        if (Window != AreaMenuWindow.PickCharacter || direction == 0) return;
        var count = AreaStartingHero.Premade.Count;
        Hero = ((Hero + Math.Sign(direction)) % count + count) % count;
        Notice = null;
    }

    /// <summary>Starts a new game with the browsed hero (the browser only).</summary>
    public AreaMenuCommand Start() => Window == AreaMenuWindow.PickCharacter && NewGameRefusal is null
        ? new(AreaMenuCommandKind.StartNewGame, Hero) : AreaMenuCommand.Nothing;

    /// <summary>The "Last Save" outcome with no save: the original returns to the window it came from without a message (MM4);
    /// Steamwoven also names it in the note.</summary>
    public void NoSave() => Notice = "no save to continue from";

    /// <summary>After the load window closed without loading: the single player window is shown again (its notice kept).</summary>
    public void Resume(string? notice) => Notice = notice;

    private void Open(AreaMenuWindow target)
    {
        _back.Add(Window);
        Window = target;
        // The original's browser starts at its first hero (index 1).
        if (target == AreaMenuWindow.PickCharacter) Hero = 0;
        Focus = Shown.Count > 0 ? 0 : -1;
    }
}
