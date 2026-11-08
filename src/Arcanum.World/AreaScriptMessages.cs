namespace Arcanum.World;

/// <summary>
/// ARC-DEV-309 (B21): what a script's print line (action 24) does on screen. The original builds the line's text and hands it, with the
/// action's second number as the entry's first field, to the interface's message window (0x44B390 → 0x460630 → 0x550750 → 0x552770,
/// ledger IS2, PW1 and ML1–ML2, executable 1.0.7.4). So the second number is the window's entry type: 4 a stored game message with sound
/// 3004, 5 the same layout stored without a sound, 6 a transient text that is never stored and is drawn only in the message mode and only
/// when more than three seconds passed since the last stored entry (the log's rule). The installed scripts use 6 in 186 of 189 print
/// lines. Entry types 0 to 3 and 7 to 13 need data a print line does not carry or a drawing the viewer lacks (ML5) and are not posted: NOT READY.
/// Godot-free.
/// </summary>
public static class AreaScriptMessages
{
    /// <summary>The entry the message window gets for a printed line, or null when its type is not posted.</summary>
    public static AreaMessageEntry? Entry(AreaPrintedLine line) => line.Type switch
    {
        4 => AreaMessageEntry.Text(AreaMessageKind.Message, line.Text),
        5 => AreaMessageEntry.Text(AreaMessageKind.Plain, line.Text),
        6 => AreaMessageEntry.Text(AreaMessageKind.Hover, line.Text),
        _ => null,
    };

    /// <summary>The entries of a run's printed lines, in print order.</summary>
    public static IReadOnlyList<AreaMessageEntry> Entries(IEnumerable<AreaPrintedLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(Entry).OfType<AreaMessageEntry>().ToArray();
    }
}
