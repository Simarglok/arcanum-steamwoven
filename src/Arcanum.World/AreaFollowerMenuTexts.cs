using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Party;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-1786 (ledger FP8): the words of the follower order menu's eight rows. The original reads them from <c>mes\follower_ui.mes</c>
/// messages 0–7 (in the player's language) and skips a row whose message is missing; the rows keep their own places (the draw loop 0x56B970 steps the row's y by 18 for every index, drawn or not), so a skipped row
/// leaves a gap. When the file cannot be read at all (no game folder, the self-test) the project's English words stand in, with the
/// reason in <see cref="Detail"/>. Godot-free.
/// </summary>
public sealed class AreaFollowerMenuTexts
{
    /// <summary>The message table of the rows' words.</summary>
    public const string MesPath = "mes/follower_ui.mes";
    private readonly IReadOnlyDictionary<int, string> _messages;

    private AreaFollowerMenuTexts(IReadOnlyDictionary<int, string> messages, bool fromGame, string detail)
    {
        _messages = messages; FromGame = fromGame; Detail = detail;
    }

    /// <summary>True when the words come from the installed <c>follower_ui.mes</c> (a missing message then skips its row).</summary>
    public bool FromGame { get; }

    public string Detail { get; }

    /// <summary>The project's English words, one per item (the original's retail English file's, as the ledger lists them).</summary>
    public static string ProjectText(FollowerMenuItem item) => item switch
    {
        FollowerMenuItem.Walk => "Walk",
        FollowerMenuItem.Attack => "Attack",
        FollowerMenuItem.StayClose => "Stay Close",
        FollowerMenuItem.SpreadOut => "Spread Out",
        FollowerMenuItem.BackOff => "Back Off",
        FollowerMenuItem.Wait => "Wait",
        FollowerMenuItem.Inventory => "Inventory",
        FollowerMenuItem.CharacterSheet => "Character Sheet",
        _ => throw new ArgumentOutOfRangeException(nameof(item)),
    };

    /// <summary>No game folder (the self-test) or an unreadable file: the project's English words for every row.</summary>
    public static AreaFollowerMenuTexts Project(string reason) =>
        new(Enum.GetValues<FollowerMenuItem>().ToDictionary(item => (int)item, ProjectText), false, reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>Texts over a message lookup (tests): only the numbers it answers have a row, as for the installed file.</summary>
    public static AreaFollowerMenuTexts From(Func<int, string?> messages, string detail)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(detail);
        var kept = new Dictionary<int, string>();
        foreach (var item in Enum.GetValues<FollowerMenuItem>())
            if (messages((int)item) is { } text) kept[(int)item] = text;
        return new(kept, true, detail);
    }

    /// <summary>Reads <c>mes\follower_ui.mes</c> from the sources the original reads it in (game start); an unreadable file gives the project's words with the reason.</summary>
    public static AreaFollowerMenuTexts Load(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (AreaStartingHero.Table(stack, MesPath, ContentLoadPhase.GameStart, required: true, out var detail) is not { } table)
            return Project($"{MesPath} not read ({detail})");
        return From(number => table.Find(number) is { } message
            && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } text } ? text : null, $"{MesPath} read");
    }

    /// <summary>The words of an item's row, or null when its message is missing (FP8: the row is skipped).</summary>
    public string? Text(FollowerMenuItem item) => _messages.GetValueOrDefault((int)item);

    /// <summary>The rows the menu draws, in the original's order: the items whose message exists.</summary>
    public IReadOnlyList<FollowerMenuItem> Rows => Enum.GetValues<FollowerMenuItem>().Where(item => _messages.ContainsKey((int)item)).ToArray();
}
