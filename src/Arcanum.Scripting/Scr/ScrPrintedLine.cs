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
/// one space. The step runs the record's action field and selects generated text for a <c>g:</c>, <c>m:</c> or <c>r:</c>
/// prefix (any case) or a whole text of <c>i:</c>; neither is modelled, so such a line is
/// <see cref="ScrPrintedLineStatus.Unsupported"/>. Otherwise the text, or the alternate text when the triggerer is a critter
/// that is not male, decoded as Windows-1252 with <c>@pcname@</c> and <c>@npcname@</c> substituted; any other token, a
/// control byte or an oversized text is Unsupported.
/// </summary>
public static class ScrPrintedLine
{
    public const int MaxTextBytes = 4096;

    [OriginalFunction(0x44B390, OriginalFunctionScope.Partial)]
    public static ScrPrintedLineText Read(DlgDocument document, int line, bool alternate, DialogSpeakerNames names)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(names);
        if (!document.TryGetRecord(line, out var record) || record is null) return new(ScrPrintedLineStatus.Shown, " ", $"no line {line}: one space");
        if (record.Action.Length > 0) return new(ScrPrintedLineStatus.Unsupported, string.Empty, $"line {line} has an action field");
        var field = alternate ? record.AlternateText : record.Text;
        if (DialogText.Decode(field.Bytes, DialogTextEncoding.Windows1252, MaxTextBytes, out var text) is var failure && failure != DialogTextFailureKind.None)
            return new(ScrPrintedLineStatus.Unsupported, string.Empty, $"line {line}: {failure}");
        if ((text.Length >= 2 && text[1] == ':' && char.ToLowerInvariant(text[0]) is 'g' or 'm' or 'r') || text.Equals("i:", StringComparison.OrdinalIgnoreCase))
            return new(ScrPrintedLineStatus.Unsupported, string.Empty, $"line {line} is generated text");
        if (DialogText.ValidateTokens(text) is var tokens && tokens != DialogTextFailureKind.None)
            return new(ScrPrintedLineStatus.Unsupported, string.Empty, $"line {line}: {tokens}");
        return new(ScrPrintedLineStatus.Shown, DialogText.Substitute(text, names), $"line {line}");
    }
}
