using Arcanum.Data;
using Arcanum.Scripting.Dialog;

namespace Arcanum.Scripting.Scr;

public enum ScrPrintedLineStatus
{
    /// <summary>The text to show.</summary>
    Shown,
    /// <summary>The line would do more than show text (an action field or generated text) or its text cannot be decoded;
    /// a caller that cannot model it refuses the run's effects.</summary>
    Unsupported,
}

public sealed record ScrPrintedLineText(ScrPrintedLineStatus Status, string Text, string Detail);

/// <summary>
/// The text a print-line action (<see cref="ScrPrintLine"/>) shows from a loaded DLG, as the original's NPC-line step
/// builds it (ledger IS2, docs/research/item-insert-script.md): the record with the line's number; a missing record shows
/// one space. The step runs the record's action field (a field of separators only holds no operation) and selects generated text for a <c>g:</c>, <c>m:</c> or <c>r:</c>
/// prefix (any case) or a whole text of <c>i:</c> of the record text (whichever field is shown, as the conversation
/// runtime tests NPC markers); neither is modelled here, so such a line is
/// <see cref="ScrPrintedLineStatus.Unsupported"/>. Otherwise the text, or the alternate text when the triggerer is a critter
/// that is not male, decoded as Windows-1252 with <c>@pcname@</c> and <c>@npcname@</c> substituted; any other token, a
/// control byte or an oversized text is Unsupported.
/// </summary>
public static class ScrPrintedLine
{
    public const int MaxTextBytes = 4096;

    /// <summary>ARC-SCRIPT-2965: whether the record of <paramref name="line"/> holds a real operation in its action field (not only separators), which a float
    /// line runs between the PC and the speaker (ledger FL4); a missing line holds none.</summary>
    public static bool HasActionField(DlgDocument document, int line)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.TryGetRecord(line, out var record) && record is not null && record.Action.Bytes.Any(b => !DialogConversationGrammar.IsSeparator(b));
    }

    [OriginalFunction(0x44B390, OriginalFunctionScope.Partial)]
    public static ScrPrintedLineText Read(DlgDocument document, int line, bool alternate, DialogSpeakerNames names)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(names);
        if (!document.TryGetRecord(line, out var record) || record is null) return new(ScrPrintedLineStatus.Shown, " ", $"no line {line}: one space");
        // ARC-SCRIPT-050: a field of separators only (106 of the 130 installed floated lines with a non-empty action field hold one or two spaces) is taken as
        // holding no operation, by the conversation grammar's admission policy (not a reading of the original's scanner).
        if (record.Action.Bytes.Any(b => !DialogConversationGrammar.IsSeparator(b))) return new(ScrPrintedLineStatus.Unsupported, string.Empty, $"line {line} has an action field");
        // As in the conversation runtime: the marker is tested on the record text whatever the field shown, and a marker is
        // ASCII, so the raw prefix decides it.
        var raw = record.Text.Bytes;
        var markerText = raw.Count >= 2 && raw[0] < 0x80 && raw[1] == (byte)':' ? $"{(char)raw[0]}:{(raw.Count > 2 ? "+" : string.Empty)}" : string.Empty;
        var marker = DialogText.Marker(markerText, out var exact);
        if (marker is { Letter: 'g' or 'm' or 'r' } || (marker is { Letter: 'i' } && exact))
            return new(ScrPrintedLineStatus.Unsupported, string.Empty, $"line {line} is generated text");
        var field = alternate ? record.AlternateText : record.Text;
        if (DialogText.Decode(field.Bytes, DialogTextEncoding.Windows1252, MaxTextBytes, out var text) is var failure && failure != DialogTextFailureKind.None)
            return new(ScrPrintedLineStatus.Unsupported, string.Empty, $"line {line}: {failure}");
        if (DialogText.ValidateTokens(text) is var tokens && tokens != DialogTextFailureKind.None)
            return new(ScrPrintedLineStatus.Unsupported, string.Empty, $"line {line}: {tokens}");
        return new(ScrPrintedLineStatus.Shown, DialogText.Substitute(text, names), $"line {line}");
    }

}
