using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;

namespace Arcanum.World;

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
    /// <summary>ARC-UI-3120 (ledger MW32): the title line of every menu button's hover text (ledger MW32); the hint of a button is its table's hint number (7002, 7003, ...).</summary>
    public const int ButtonHintTitle = 6999;
    private const int FirstHint = 7000, HintCount = 100;
    private const int MaxLabel = 64;
    private const int MaxQuestion = 256;
    private static readonly IReadOnlyDictionary<int, string> ProjectText = WithSaveMessages(new Dictionary<int, string>
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
    });

    /// <summary>ARC-DEV-168: the save and load messages of the message window (<see cref="AreaSaveMessages"/>) are read from the same file.</summary>
    private static IReadOnlyDictionary<int, string> WithSaveMessages(Dictionary<int, string> menu)
    {
        foreach (var (number, text) in AreaSaveMessages.ProjectText) menu[number] = text;
        return menu;
    }
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
        var opened = ContentStack.OpenShared(found.Plan!);
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
        // ARC-UI-3120: the buttons' hover texts have no project text: the original's or none.
        foreach (var number in Enumerable.Range(FirstHint, HintCount).Append(ButtonHintTitle))
            if (table.Find(number) is { } hint && MesText.Decode(hint, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 } hintText } && hintText.Length <= MaxQuestion)
                kept[number] = hintText;
        return new(number => kept.GetValueOrDefault(number), true, $"{MainMenuPath}: {kept.Count} of {ProjectText.Count} texts");
    }

    /// <summary>The text of a message: the game's when it has it, else the project's.</summary>
    public string Text(int message) => _original(message) is { Length: > 0 } text ? text : ProjectText.GetValueOrDefault(message, $"[{message}]");

    /// <summary>ARC-UI-3120: the game's text of a message with no project text (a button's hover title and hint), or null when the game has none (or no game folder).</summary>
    public string? Original(int message) => _original(message) is { Length: > 0 } text ? text : null;

    /// <summary>The hotkey of a button: the first character of its hotkey message, upper-cased (MM3); null when the message is empty.</summary>
    public char? Hotkey(AreaMenuButton button)
    {
        ArgumentNullException.ThrowIfNull(button);
        var text = Text(button.HotkeyMessage);
        return text.Length > 0 && !char.IsControl(text[0]) ? char.ToUpperInvariant(text[0]) : null;
    }
}
