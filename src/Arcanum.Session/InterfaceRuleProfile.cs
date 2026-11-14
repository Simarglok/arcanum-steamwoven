namespace Arcanum.Session;

/// <summary>
/// The interface settings of owner decision D8 (docs/world/rule-modes.md): what the game shows the player where the original
/// shows something that does not match what it does.
/// </summary>
public sealed record InterfaceRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ledger CUA1 (ARC-UI-056): in combat mode the original's cursor refresh (0x553990) shows the called-shot cursor by the held
    /// key, <c>,</c> the head (art 818), <c>.</c> the leg picture (819) and <c>/</c> the arm picture (820), while the attack aims
    /// <c>.</c> at the arm (location 2) and <c>/</c> at the leg (location 3, ledger FA4). Steamwoven shows the picture of the
    /// location the attack aims at.
    /// </summary>
    public bool CalledShotCursorsSwapped { get; init; }

    /// <summary>
    /// Ledger CH6 (ARC-UI-1114): the original's combat interface shows no turn order, no list of participants and no name of whoever
    /// acts (only the action-point lamps and, on another participant's subturn, three running lights). Steamwoven adds a list of the
    /// round's participants with the current one marked (owner decision 2026-10-28, a Steamwoven addition); Original hides it.
    /// </summary>
    public bool TurnOrderListHidden { get; init; }

    /// <summary>
    /// ARC-UI-2019: the original's dialog reads only mouse messages (dialog-conversation-runtime, F0): it shows no digit before a reply
    /// and a digit key chooses nothing. Steamwoven adds the number of each reply (as the diagnostic panel always drew it) and the choice of
    /// a reply by its digit key (a Steamwoven addition, owner request 2026-11-14); Original keeps neither.
    /// </summary>
    public bool DialogRepliesUnnumbered { get; init; }

    /// <summary>
    /// ARC-UI-2025, ledger DH3: the original's dialog reply band picks the pressed row as y / 20 although the rows are drawn every 19 px, so the
    /// click bands of the lower rows lie up to 4 px lower than the rows seen (an arithmetic slip of the original, never an intended
    /// feature). Steamwoven picks the row whose drawn pixels the pointer is on (owner classification proposed with the fix, 2026-11-14);
    /// Original keeps the slip.
    /// </summary>
    public bool DialogReplyRowsDrift { get; init; }

    /// <summary>
    /// ARC-UI-2023 (audit of ARC-UI-1997): the original's dialog window shows only the PC's reply rows; the only ways out are a DLG reply or a
    /// window or event that pops the dialog mode (0x5678D0). The viewer's diagnostic panel adds a header (NPC, script, turn, DLG node), an outcome
    /// line, the typed-effects ledger, a hint line, a Leave button and a controller-cancel leave; Steamwoven keeps that chrome (project
    /// diagnostics, owner decision on its fate pending), Original shows none of it.
    /// </summary>
    public bool DialogDiagnosticChromeHidden { get; init; }

    /// <summary>The original with every recorded fix and addition; the default setting.</summary>
    public static readonly InterfaceRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly InterfaceRuleProfile Original = new() { Name = "original", CalledShotCursorsSwapped = true, TurnOrderListHidden = true, DialogRepliesUnnumbered = true, DialogReplyRowsDrift = true, DialogDiagnosticChromeHidden = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<InterfaceRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static InterfaceRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
