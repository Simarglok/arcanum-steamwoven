namespace Arcanum.World;

/// <summary>
/// ARC-DEV-309 (B21), ARC-SCRIPT-1118: what a script's print line (action 24) does on screen. The original builds the line's text and hands it, with
/// the action's second number as the entry's first field, to the interface's message window (0x44B390 → 0x460630 → 0x550750 → 0x552770,
/// ledger IS2, PW1 and ML1–ML2, executable 1.0.7.4). So the second number is the window's entry type: 4 a stored game message with sound
/// 3004, 5 the same layout stored without a sound, 6 a transient text that is never stored and is drawn only in the message mode and only
/// when more than three seconds passed since the last stored entry (the log's rule). The installed scripts use 6 in 186 of 189 print
/// lines. The entry's other fields are not the script's: the first number is always 0 and the second one is left unset on the stack (ledger PY1),
/// so a type that draws a number shows 0, and a type that reads the second number (0 and 7) or fields beyond the entry (8) draws junk. The types whose
/// draw needs only the line's text and that zero are posted: 1 (the title and <c>intgame.mes</c> 13 with 0, stored, sound 3006), 2 and 3 (the
/// title and the empty body of the curse or blessing of number 0, stored, sounds 3003 and 3002), 12 (a hover text without the second layout). Types 0, 7, 8,
/// 9 to 11 and 13 are not posted: NOT READY (PY1–PY4). Godot-free.
/// </summary>
public static class AreaScriptMessages
{
    /// <summary>The entry the message window gets for a printed line, or null when its type is not posted. <paramref name="texts"/> gives
    /// the poison notice its message (<c>intgame.mes</c> 13); the project's label stands without it.</summary>
    public static AreaMessageEntry? Entry(AreaPrintedLine line, AreaLevelTexts? texts = null) => line.Type switch
    {
        1 => new(AreaMessageKind.PoisonAbsorbed, line.Text,
            AreaLevelMessages.Format(texts?.Text(AreaLevelTexts.InterfacePath, PoisonMessage) ?? "You have absorbed %d unit(s) of poison.", EntryNumber)),
        2 => AreaMessageEntry.Text(AreaMessageKind.Curse, line.Text),
        3 => AreaMessageEntry.Text(AreaMessageKind.Blessing, line.Text),
        4 => AreaMessageEntry.Text(AreaMessageKind.Message, line.Text),
        5 => AreaMessageEntry.Text(AreaMessageKind.Plain, line.Text),
        6 => AreaMessageEntry.Text(AreaMessageKind.Hover, line.Text),
        12 => AreaMessageEntry.Text(AreaMessageKind.ScriptHover, line.Text),
        _ => null,
    };

    /// <summary><c>intgame.mes</c> 13, the poison notice's number line (0x5529C0 case 1).</summary>
    public const int PoisonMessage = 13;

    /// <summary>The first number of the entry a print line builds (0x44B390 stores 0 at the entry's +8): the number the draw formats or looks up.</summary>
    public const int EntryNumber = 0;

    /// <summary>The entries of a run's printed lines, in print order.</summary>
    public static IReadOnlyList<AreaMessageEntry> Entries(IEnumerable<AreaPrintedLine> lines, AreaLevelTexts? texts = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(line => Entry(line, texts)).OfType<AreaMessageEntry>().ToArray();
    }
}
