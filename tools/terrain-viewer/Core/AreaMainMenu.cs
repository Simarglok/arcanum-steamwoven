using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The original main-menu windows this menu models, by their window type in the original's window table (ledger MM2;
/// ARC-UI-012: the in-play windows 3 and 4 that Escape opens in play, ledger SF1–SF2; ARC-UI-013: the options window 6, OP1; ARC-UI-030:
/// the new-character window 12, NW1; ARC-UI-034: the character editor window 14, CM1).</summary>
public enum AreaMenuWindow { Main = 2, InPlay = 3, InPlayLimited = 4, SinglePlayer = 5, Options = 6, NewGame = 11, NewCharacter = 12, PickCharacter = 13,
    CharacterEditor = 14, Shop = 15 }

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
    /// <summary>ARC-UI-012: opens the save list's save window (in play).</summary>
    SaveGame,
    /// <summary>ARC-UI-012: asks whether to quit the game (message 5100); yes goes back to the main menu.</summary>
    Quit,
    /// <summary>ARC-UI-012: closes the in-play menu and play goes on.</summary>
    Continue,
    /// <summary>ARC-UI-013: shows <see cref="AreaMenuButton.Page"/> of the options window.</summary>
    OptionsPage,
    /// <summary>ARC-UI-013: the options window's Done: back to the main menu, or from play closes the menu (OP6).</summary>
    OptionsDone,
    /// <summary>ARC-UI-030: "New Character": the host makes the new character and opens its window (<see cref="AreaMainMenu.OpenNewCharacter"/>).</summary>
    NewCharacter,
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
    /// <summary>ARC-UI-012: open the save list's save window; the menu waits under it (a save ends it, as the original's, SF4).</summary>
    SaveGame,
    /// <summary>ARC-UI-012: the quit was confirmed: the game ends and the main menu opens over a fresh start of the game (SF3).</summary>
    QuitToMenu,
    /// <summary>ARC-UI-012: close the in-play menu.</summary>
    Continue,
    /// <summary>ARC-UI-030: make the new character (the original's window 12 opening, 0x5446F0) and open its window with it.</summary>
    OpenNewCharacter,
    /// <summary>ARC-UI-034: the new-character window's Accept passed: make the draft session of the new character and open the
    /// character editor (window 14) on it (<see cref="AreaMainMenu.OpenCharacterEditor"/>).</summary>
    OpenCharacterEditor,
    /// <summary>ARC-UI-030: start a new game as the new character (<see cref="AreaMainMenu.Creation"/>); since ARC-UI-034 with the editor's
    /// presses in the character; since ARC-UI-036 the opening shop's Accept (or Return), with the shop's kit.</summary>
    StartNewCharacter,
    /// <summary>ARC-UI-036: the character editor's Accept (or Return): make the opening shop's kit from the editor's draft and open the
    /// shop (window 15) with it (<see cref="AreaMainMenu.OpenShop"/>).</summary>
    OpenShop,
    /// <summary>ARC-UI-013: set <see cref="AreaMenuCommand.Option"/> to <see cref="AreaMenuCommand.Value"/> (the original's setter runs at
    /// once, OP3); the host applies it and gives the window the values it then has (<see cref="AreaMainMenu.SetOptions"/>).</summary>
    ChangeOption,
}

public readonly record struct AreaMenuCommand(AreaMenuCommandKind Kind, int Hero = -1, AreaOption Option = default, int Value = 0)
{
    public static AreaMenuCommand Nothing => new(AreaMenuCommandKind.None);
}

/// <param name="Message">The label's message in <c>mes\MainMenu.mes</c>; its hotkey letter is message + 10 (ledger MM3).</param>
/// <param name="Target">The window an <see cref="AreaMenuRole.Open"/> button opens.</param>
/// <param name="Unavailable">Why the button is hidden, for the window's note; null when it is shown.</param>
/// <param name="Page">The page an <see cref="AreaMenuRole.OptionsPage"/> button shows.</param>
public sealed record AreaMenuButton(int Message, AreaMenuRole Role, AreaMenuWindow? Target = null, string? Unavailable = null, AreaOptionsPage Page = default)
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
    /// <summary>ARC-UI-012: the question of the quit box (SF3).</summary>
    public const int QuitQuestion = 5100;
    private const int MaxLabel = 64;
    private const int MaxQuestion = 256;
    private static readonly IReadOnlyDictionary<int, string> ProjectText = new Dictionary<int, string>
    {
        [10] = "Single Player", [11] = "MultiPlayer", [12] = "Options", [13] = "Credits", [14] = "Exit Game",
        [20] = "S", [21] = "M", [22] = "O", [23] = "C", [24] = "E",
        [50] = "New Game", [51] = "Load Game", [52] = "Last Save", [53] = "View Intro", [54] = "Cancel",
        [60] = "N", [61] = "L", [62] = "S", [63] = "V", [64] = "C",
        [420] = "Pick Character", [421] = "New Character", [422] = "Cancel",
        [430] = "P", [431] = "N", [432] = "X",
        [30] = "Save Game", [31] = "Load Game", [32] = "Options", [33] = "Quit", [34] = "Continue",
        [40] = "S", [41] = "L", [42] = "O", [43] = "Q", [44] = "C",
        [70] = "Options", [71] = "Quit", [72] = "Continue",
        [80] = "O", [81] = "Q", [82] = "C",
        [4000] = "Game", [4001] = "Video", [4002] = "Sound", [4003] = "Done",
        [4010] = "G", [4011] = "V", [4012] = "S", [4013] = "D",
        [QuitQuestion] = "Quit the current game?",
        // ARC-UI-033: the new-character window's default name and its refusal (project texts, not the game's).
        [CharacterNameRules.DefaultNameMessage] = "Enter Name", [CharacterNameRules.RefusalMessage] = "Your character needs a name.",
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
            if (table.Find(number) is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 } text }
                && text.Length <= (number == QuitQuestion ? MaxQuestion : MaxLabel))
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
/// it. Buttons whose function Steamwoven lacks (network play, credits, intro movies) are hidden and
/// named in the window's note (FR-UI-001: the menu shows only what the profile supports). "Pick Character" needs the start map
/// (the original's new game teleports the PC there); the host says whether the shown map is it. The pre-made browser starts at the
/// first hero and cycles through all twelve (the original: 1..count-1, twelve heroes when the female heroes' art exists). The host
/// runs the returned command. Caller-serialized.
/// ARC-UI-012: the same menu opens in play on Escape at the in-play window (Save Game, Load Game, Options, Quit, Continue; window 4,
/// in a turn-based round on another's turn, has Options, Quit and Continue), whose Escape continues play (ledger SF1–SF2). Quit asks
/// first (message 5100; Y or N, Escape answers no) and then goes back to the main menu (SF3), whose root Escape exits the game once
/// the menu opens again (MM6: <c>reopened</c>).
/// ARC-UI-013: Options opens the options window (window 6, ledger OP1–OP8) from the main menu and from both in-play windows. Its
/// buttons show the Game, Video and Sound pages and Done; a page's controls change with their arrows at once (a list wraps around,
/// a slider stops at its ends), the host applying each change. Done goes back to the main menu, or from play closes the menu; Escape
/// does nothing there, and from play the O key closes the menu (OP6–OP7). The page and Done letters act, as in the other windows,
/// though the original's window ignores them (an original bug, OP7). Controls Steamwoven lacks are hidden with the reason.
/// ARC-UI-030: "New Character" (like "Pick Character", only where a new game can start) asks the host to make the new character
/// (<see cref="AreaNewCharacter"/>) and opens its window (window 12, ledger NW1): gender, race and background buttons, Accept and
/// Cancel; it has no letters (its buttons are art only), Escape and Cancel go back and drop the character. ARC-UI-033: portrait buttons and
/// the name field (typed while it is being typed into; Enter ends it), and Accept refuses the default name (NW6). ARC-UI-034: a passed
/// Accept asks the host for the character editor (window 14, ledger CM1–CM8) over a draft session of the character; its
/// Cancel or Escape goes back to window 12 with a new character (the original's opening makes one).
/// ARC-UI-036: the editor's Accept or Return asks the host for the opening shop (window 15, ledger OS1–OS6) with the kit made from the
/// draft; the shop's Accept or Return starts the game with it, its Back or Escape goes back to the editor, whose points stay free.
/// </summary>
public sealed class AreaMainMenu
{
    public const string NoNetworkPlay = "network play is not built";
    public const string NoCredits = "the credits screen is not built (FR-MEDIA-002)";
    public const string NoIntro = "movies do not play (FR-MEDIA-001)";
    /// <summary>ARC-UI-038: the opening shop's note while its merchant's barter is not open yet (the shop map loads behind the menu).</summary>
    public const string ShopLoading = "the shop map's merchant is loading; Accept starts the game with the kit";
    /// <summary>ARC-UI-038: the opening shop's note when the project could not open its merchant's barter (the shop is the kit alone).</summary>
    public static string ShopUnavailable(string why) => $"the shop map's merchant is not available ({why}); Accept starts the game with the kit";
    /// <summary>The quit box's fixed letters (the original's yes/no box, SF3).</summary>
    public const char YesLetter = 'Y', NoLetter = 'N';
    private readonly List<AreaMenuWindow> _back = [];

    /// <param name="newGameRefusal">Why a new game cannot start (null: a hero can be picked; the host reaches the start map).</param>
    /// <param name="root">The window the menu opens at: <see cref="AreaMenuWindow.Main"/>, or an in-play window (ARC-UI-012).</param>
    /// <param name="reopened">The main menu opens again after its first opening (from play): its root Escape exits (MM6).</param>
    public AreaMainMenu(AreaMainMenuTexts texts, string? newGameRefusal, AreaMenuWindow root = AreaMenuWindow.Main, bool reopened = false)
    {
        Texts = texts ?? throw new ArgumentNullException(nameof(texts));
        if (root is not (AreaMenuWindow.Main or AreaMenuWindow.InPlay or AreaMenuWindow.InPlayLimited)) throw new ArgumentOutOfRangeException(nameof(root));
        NewGameRefusal = newGameRefusal;
        Root = Window = root;
        Reopened = reopened;
    }

    public AreaMainMenuTexts Texts { get; }
    public string? NewGameRefusal { get; }
    public AreaMenuWindow Root { get; }
    public bool Reopened { get; }
    /// <summary>True while the quit box asks (message 5100); only its answer, Escape and its letters act.</summary>
    public bool QuitPending { get; private set; }
    public AreaMenuWindow Window { get; private set; }
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
            new(12, AreaMenuRole.Open, AreaMenuWindow.Options), new(13, AreaMenuRole.Unavailable, Unavailable: NoCredits), new(14, AreaMenuRole.Exit),
        ],
        AreaMenuWindow.SinglePlayer =>
        [
            new(50, AreaMenuRole.Open, AreaMenuWindow.NewGame), new(51, AreaMenuRole.LoadGame), new(52, AreaMenuRole.LastSave),
            new(53, AreaMenuRole.Unavailable, Unavailable: NoIntro), new(54, AreaMenuRole.Back),
        ],
        AreaMenuWindow.NewGame =>
        [
            NewGameRefusal is null ? new(420, AreaMenuRole.Open, AreaMenuWindow.PickCharacter) : new(420, AreaMenuRole.Unavailable, Unavailable: NewGameRefusal),
            NewGameRefusal is null ? new(421, AreaMenuRole.NewCharacter) : new(421, AreaMenuRole.Unavailable, Unavailable: NewGameRefusal),
            new(422, AreaMenuRole.Back),
        ],
        AreaMenuWindow.InPlay =>
        [
            new(30, AreaMenuRole.SaveGame), new(31, AreaMenuRole.LoadGame), new(32, AreaMenuRole.Open, AreaMenuWindow.Options),
            new(33, AreaMenuRole.Quit), new(34, AreaMenuRole.Continue),
        ],
        AreaMenuWindow.InPlayLimited => [new(70, AreaMenuRole.Open, AreaMenuWindow.Options), new(71, AreaMenuRole.Quit), new(72, AreaMenuRole.Continue)],
        AreaMenuWindow.Options =>
        [
            new(4000, AreaMenuRole.OptionsPage, Page: AreaOptionsPage.Game),
            AreaOptionTable.Offered(AreaOptionsPage.Video) ? new(4001, AreaMenuRole.OptionsPage, Page: AreaOptionsPage.Video)
                : new(4001, AreaMenuRole.Unavailable, Unavailable: AreaOptionTable.NoVideoPage),
            new(4002, AreaMenuRole.OptionsPage, Page: AreaOptionsPage.Sound), new(4003, AreaMenuRole.OptionsDone),
        ],
        _ => [],
    };

    public IReadOnlyList<AreaMenuButton> Shown => Buttons.Where(button => button.Shown).ToArray();

    /// <summary>The hidden buttons of the window with the reason, for its note; in the options window also the page's hidden controls
    /// and the values a shown control does not offer; in the opening shop what it skips.</summary>
    public IReadOnlyList<string> HiddenNotes => Buttons.Where(button => !button.Shown).Select(button => $"{Texts.Text(button.Message)}: {button.Unavailable}")
        .Concat(Window == AreaMenuWindow.Shop && ShopNote is { } shopNote ? [shopNote] : [])
        .Concat(Window != AreaMenuWindow.Options ? [] : AreaOptionTable.Page(Page)
            .Where(control => !control.Shown || control.LimitNote is not null)
            .Select(control => $"{OptionsTexts.Label(control)}: {control.Unavailable ?? control.LimitNote}"))
        .ToArray();

    /// <summary>ARC-UI-038: why the opening shop shows no merchant's barter (<see cref="ShopLoading"/>, <see cref="ShopUnavailable"/>); null
    /// once the barter is open. Each opening of the shop starts with <see cref="ShopLoading"/>.</summary>
    public string? ShopNote { get; set; }

    /// <summary>ARC-UI-013: the options texts (labels and values); the project's until the host gives the game's.</summary>
    public AreaOptionsTexts OptionsTexts { get; set; } = AreaOptionsTexts.Project("not read");
    /// <summary>ARC-UI-013: the page the options window shows (the Game page when it opens, OP4).</summary>
    public AreaOptionsPage Page { get; private set; }
    /// <summary>ARC-UI-013: the values the options window shows; null until the host gives them.</summary>
    public AreaGameOptions? Options { get; private set; }
    /// <summary>ARC-UI-013: the shown controls of the options window's page, in slot order.</summary>
    public IReadOnlyList<AreaOptionControl> Controls => Window == AreaMenuWindow.Options ? AreaOptionTable.Page(Page).Where(control => control.Shown).ToArray() : [];
    /// <summary>ARC-UI-013: the controller's focus in the options window over its controls, then its shown buttons; -1 none.</summary>
    public int OptionsFocus { get; private set; } = -1;

    /// <summary>ARC-UI-013: the host's values after it applied a change, or when the window opens.</summary>
    public void SetOptions(AreaGameOptions options) => Options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>ARC-UI-013: an arrow of the <paramref name="index"/>-th shown control (which takes the focus): the change for the host
    /// to apply (OP3), or nothing at a slider's end or before the values are known.</summary>
    public AreaMenuCommand Change(int index, int direction)
    {
        var controls = Controls;
        if (QuitPending || Options is not { } options || index < 0 || index >= controls.Count) return AreaMenuCommand.Nothing;
        // A control's arrow takes the focus, so the keys go on with it.
        OptionsFocus = index;
        var control = controls[index];
        if (options.Value(control.Option) is not { } value || control.Step(value, direction) is not { } next) return AreaMenuCommand.Nothing;
        Notice = null;
        return new(AreaMenuCommandKind.ChangeOption, Option: control.Option, Value: next);
    }

    /// <summary>ARC-UI-013: the physical O key: from play it closes the options window and the menu (the original's key table, OP7).</summary>
    public AreaMenuCommand OptionsKey() => Window == AreaMenuWindow.Options && Root != AreaMenuWindow.Main && !QuitPending
        ? new(AreaMenuCommandKind.Continue) : AreaMenuCommand.Nothing;

    /// <summary>Activates the <paramref name="index"/>-th shown button.</summary>
    public AreaMenuCommand Activate(int index)
    {
        var shown = Shown;
        if (QuitPending || index < 0 || index >= shown.Count) return AreaMenuCommand.Nothing;
        var button = shown[index];
        Notice = null;
        switch (button.Role)
        {
            case AreaMenuRole.SaveGame: return new(AreaMenuCommandKind.SaveGame);
            case AreaMenuRole.Quit: QuitPending = true; return AreaMenuCommand.Nothing;
            case AreaMenuRole.Continue: return new(AreaMenuCommandKind.Continue);
            case AreaMenuRole.OptionsPage: Page = button.Page; OptionsFocus = Controls.Count > 0 ? 0 : -1; return AreaMenuCommand.Nothing;
            // The original's Done goes back when the options came from the main menu and closes the menu from play (OP6).
            case AreaMenuRole.OptionsDone: if (Root != AreaMenuWindow.Main) return new(AreaMenuCommandKind.Continue); Back(); return AreaMenuCommand.Nothing;
            case AreaMenuRole.Open: Open(button.Target!.Value); return AreaMenuCommand.Nothing;
            case AreaMenuRole.Back: Back(); return AreaMenuCommand.Nothing;
            case AreaMenuRole.NewCharacter: return new(AreaMenuCommandKind.OpenNewCharacter);
            case AreaMenuRole.LoadGame: return new(AreaMenuCommandKind.LoadGame);
            case AreaMenuRole.LastSave: return new(AreaMenuCommandKind.LastSave);
            case AreaMenuRole.Exit: return new(AreaMenuCommandKind.Exit);
            default: return AreaMenuCommand.Nothing;
        }
    }

    /// <summary>A typed character: the shown button whose hotkey letter it is (upper-cased first, as the original), if any.</summary>
    public AreaMenuCommand Type(char typed)
    {
        // ARC-UI-033: while the new-character window's name field is being typed into it takes the character (its letters choose nothing).
        if (!QuitPending && Window == AreaMenuWindow.NewCharacter && Creation is { TypingName: true } typing)
        {
            Notice = typing.TypeName(typed);
            return AreaMenuCommand.Nothing;
        }
        var upper = char.ToUpperInvariant(typed);
        if (QuitPending) return upper == YesLetter ? Answer(true) : upper == NoLetter ? Answer(false) : AreaMenuCommand.Nothing;
        var shown = Shown;
        for (var i = 0; i < shown.Count; i++)
            if (Texts.Hotkey(shown[i]) == upper) return Activate(i);
        return AreaMenuCommand.Nothing;
    }

    /// <summary>The quit box's answer: yes quits to the main menu, no keeps the window.</summary>
    public AreaMenuCommand Answer(bool yes)
    {
        if (!QuitPending) return AreaMenuCommand.Nothing;
        QuitPending = false;
        return yes ? new(AreaMenuCommandKind.QuitToMenu) : AreaMenuCommand.Nothing;
    }

    /// <summary>Escape (ARC-UI-012): the quit box answers no; a window goes back; the in-play root continues play (SF2); the main
    /// menu's root exits once the menu opened again (MM6), and does nothing at its first opening. ARC-UI-013: the options window's
    /// key table has no Escape, so it does nothing there (OP7).</summary>
    public AreaMenuCommand Escape()
    {
        if (QuitPending) return Answer(false);
        if (Window == AreaMenuWindow.Options) return AreaMenuCommand.Nothing;
        if (Back()) return AreaMenuCommand.Nothing;
        if (Root != AreaMenuWindow.Main) return new(AreaMenuCommandKind.Continue);
        return Reopened ? new(AreaMenuCommandKind.Exit) : AreaMenuCommand.Nothing;
    }

    /// <summary>Back to the previous window; true when it went back (the root window does nothing).</summary>
    public bool Back()
    {
        if (QuitPending || _back.Count == 0) return false;
        // ARC-UI-030: leaving the new-character window drops its character (the original's next opening makes a new one, 0x5446F0).
        if (Window == AreaMenuWindow.NewCharacter) Creation = null;
        // ARC-UI-034: leaving the editor drops its draft, and window 12 opens again with a new character, as the original's back
        // (0x5417A0) runs window 12's opening, which makes a new PC from the generic prototype (CM6, NW1).
        if (Window == AreaMenuWindow.CharacterEditor)
        {
            var left = Creation!;
            DropEditor();
            Creation = new AreaNewCharacter(left.Generic, left.DefaultName);
        }
        // ARC-UI-036: back from the opening shop is the editor again (0x5417A0), which the original reopens in mode 3 on the snapshot it
        // took when the character was made (CM1, CM4): every point spent so far can still be taken back (owner decision 2026-10-28: the
        // points stay free until the game starts, in both rule modes). The kit goes; the next opening of the shop makes it anew (OS3).
        if (Window == AreaMenuWindow.Shop) Creation!.TakeKit(null);
        Window = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Focus = Window is AreaMenuWindow.NewCharacter or AreaMenuWindow.CharacterEditor ? 0 : Shown.Count > 0 ? 0 : -1;
        Notice = null;
        return true;
    }

    /// <summary>Moves the focus over the shown buttons (controller), wrapping; in the options window over its controls, then its buttons.</summary>
    public void Move(int direction)
    {
        if (Window == AreaMenuWindow.Options && !QuitPending)
        {
            var all = Controls.Count + Shown.Count;
            if (all > 0 && direction != 0) OptionsFocus = OptionsFocus < 0 ? 0 : ((OptionsFocus + Math.Sign(direction)) % all + all) % all;
            return;
        }
        var count = Window == AreaMenuWindow.PickCharacter || QuitPending ? 0 : Window switch
        {
            AreaMenuWindow.NewCharacter => CreationRows, AreaMenuWindow.CharacterEditor or AreaMenuWindow.Shop => EditorRows, _ => Shown.Count,
        };
        if (count == 0 || direction == 0) return;
        Focus = Focus < 0 ? 0 : ((Focus + Math.Sign(direction)) % count + count) % count;
    }

    /// <summary>Confirm (controller): the focused button, or in the browser the start with the browsed hero; in the options window a
    /// focused control's next value.</summary>
    public AreaMenuCommand Confirm()
    {
        if (QuitPending) return Answer(true);
        if (Window == AreaMenuWindow.PickCharacter) return Start();
        if (Window == AreaMenuWindow.NewCharacter) return ConfirmCreation();
        // ARC-UI-034: the editor's rows Accept and Cancel.
        if (Window is AreaMenuWindow.CharacterEditor or AreaMenuWindow.Shop) { if (Focus == 1) { Back(); return AreaMenuCommand.Nothing; } return Accept(); }
        if (Window != AreaMenuWindow.Options) return Activate(Focus);
        var controls = Controls.Count;
        return OptionsFocus < 0 ? AreaMenuCommand.Nothing : OptionsFocus < controls ? Change(OptionsFocus, 1) : Activate(OptionsFocus - controls);
    }

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

    /// <summary>ARC-UI-030: the new character the window holds (null outside it).</summary>
    public AreaNewCharacter? Creation { get; private set; }

    /// <summary>ARC-UI-030: the controller's rows in the new-character window: gender, race, background, portrait (ARC-UI-033), name
    /// (ARC-UI-033), Accept, Cancel.</summary>
    public const int CreationRows = 7;

    /// <summary>ARC-UI-030: opens the new-character window with the new character the host made (from the new game window only).</summary>
    public void OpenNewCharacter(AreaNewCharacter creation)
    {
        ArgumentNullException.ThrowIfNull(creation);
        if (QuitPending || Window != AreaMenuWindow.NewGame || NewGameRefusal is not null) return;
        Creation = creation;
        Open(AreaMenuWindow.NewCharacter);
    }

    /// <summary>ARC-UI-030: a race, gender or background button of the new-character window under the rule setting
    /// <paramref name="rules"/> (NW4); a refusal is the window's notice.</summary>
    public void PressCreation(AreaNewCharacterButton button, CharacterCreationRuleProfile rules)
    {
        if (QuitPending || Window != AreaMenuWindow.NewCharacter || Creation is not { } creation) return;
        Notice = creation.Press(button, rules);
    }

    /// <summary>ARC-UI-030: the new-character window's Accept. ARC-UI-033: the press ends the name's typing as a click elsewhere does,
    /// then the name check (NW6) refuses the default name with message 506 as the window's notice. ARC-UI-034: a passed check asks the
    /// host for the character editor (window 14, CM1). ARC-UI-036: the editor's Accept (or Return, CM5) asks the host for the opening
    /// shop (window 15, CM2), whose Accept (or Return) starts the new game as the character with the shop's kit (OS1).</summary>
    public AreaMenuCommand Accept()
    {
        if (NewGameRefusal is not null || QuitPending || Creation is not { } creation) return AreaMenuCommand.Nothing;
        if (Window == AreaMenuWindow.CharacterEditor) { Notice = null; return new(AreaMenuCommandKind.OpenShop); }
        if (Window == AreaMenuWindow.Shop) { Notice = null; return new(AreaMenuCommandKind.StartNewCharacter); }
        if (Window != AreaMenuWindow.NewCharacter) return AreaMenuCommand.Nothing;
        creation.EndName(enter: false);
        if (!creation.AcceptName().Accepted) { Notice = Texts.Text(CharacterNameRules.RefusalMessage); return AreaMenuCommand.Nothing; }
        Notice = null;
        return new(AreaMenuCommandKind.OpenCharacterEditor);
    }

    /// <summary>ARC-UI-034: the character editor the host opened over the new character's draft session (window 14 only).</summary>
    public AreaCreationEditor? Editor { get; private set; }

    /// <summary>ARC-UI-034: the controller's rows in the character editor: Accept, Cancel (the pages' buttons take the pointer).</summary>
    public const int EditorRows = 2;

    /// <summary>ARC-UI-034: opens the character editor (window 14) with the editor the host made over the window's character (from the
    /// new-character window only; otherwise the editor is disposed). The menu owns it from now on.</summary>
    public void OpenCharacterEditor(AreaCreationEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (QuitPending || Window != AreaMenuWindow.NewCharacter || !ReferenceEquals(editor.Creation, Creation) || NewGameRefusal is not null)
        {
            editor.Dispose();
            return;
        }
        Editor = editor;
        Open(AreaMenuWindow.CharacterEditor);
    }

    /// <summary>ARC-UI-036: opens the opening shop (window 15) with the kit the host made from the editor's draft (from the character
    /// editor only, and only a kit made for the window's character). The editor and its draft stay, hidden, for the shop's Back (the original's mode-3 reopening keeps the floor, CM4).</summary>
    public void OpenShop(AreaOpeningKit kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        if (QuitPending || Window != AreaMenuWindow.CharacterEditor || Creation is not { } creation || Editor is null || NewGameRefusal is not null
            || !ReferenceEquals(kit.Creation, creation)) return;
        creation.TakeKit(kit);
        ShopNote = ShopLoading;
        Open(AreaMenuWindow.Shop);
    }

    /// <summary>ARC-UI-034: disposes the editor's draft session (the host calls it whenever the menu ends; leaving window 14 back to
    /// window 12 does it; the opening shop keeps it).</summary>
    public void DropEditor()
    {
        Editor?.Dispose();
        Editor = null;
    }

    /// <summary>ARC-UI-038: disposes the opening shop's session (the host calls it whenever the menu ends without the game taking it).</summary>
    public void DropShop() => Creation?.DropShop();

    /// <summary>ARC-UI-033: a click on the name field: it starts typing (cleared, the old text kept); a second click does nothing.</summary>
    public void BeginName()
    {
        if (QuitPending || Window != AreaMenuWindow.NewCharacter || Creation is not { } creation) return;
        creation.BeginName();
        Notice = null;
    }

    /// <summary>ARC-UI-033: Backspace (true) or Enter (false) while the name is typed; false when the name is not being typed (the key
    /// is then the window's).</summary>
    public bool NameKey(bool backspace)
    {
        if (QuitPending || Window != AreaMenuWindow.NewCharacter || Creation is not { TypingName: true } creation) return false;
        if (backspace) creation.BackspaceName(); else creation.EndName(enter: true);
        Notice = null;
        return true;
    }

    /// <summary>ARC-UI-030: the controller's Confirm in the new-character window: a row's next button (gender, race, background,
    /// portrait), the name field (begins typing, or ends it as Enter does), Accept or Cancel.</summary>
    private AreaMenuCommand ConfirmCreation()
    {
        switch (Focus)
        {
            case 0: PressCreation(AreaNewCharacterButton.GenderNext, Rules); return AreaMenuCommand.Nothing;
            case 1: PressCreation(AreaNewCharacterButton.RaceNext, Rules); return AreaMenuCommand.Nothing;
            case 2: PressCreation(AreaNewCharacterButton.BackgroundNext, Rules); return AreaMenuCommand.Nothing;
            case 3: PressCreation(AreaNewCharacterButton.PortraitNext, Rules); return AreaMenuCommand.Nothing;
            case 4: if (!NameKey(backspace: false)) BeginName(); return AreaMenuCommand.Nothing;
            case 5: return Accept();
            case 6: Back(); return AreaMenuCommand.Nothing;
            default: return AreaMenuCommand.Nothing;
        }
    }

    /// <summary>ARC-UI-030: the rule setting the controller's presses use (the host keeps it in step with its setting).</summary>
    public CharacterCreationRuleProfile Rules { get; set; } = CharacterCreationRuleProfile.Steamwoven;

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
        // The original's options window opens at its Game page (OP4).
        if (target == AreaMenuWindow.Options) { Page = AreaOptionsPage.Game; OptionsFocus = Controls.Count > 0 ? 0 : -1; }
        Focus = target is AreaMenuWindow.NewCharacter or AreaMenuWindow.CharacterEditor or AreaMenuWindow.Shop ? 0 : Shown.Count > 0 ? 0 : -1;
    }
}
