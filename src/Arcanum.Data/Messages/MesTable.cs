using System.Text;

namespace Arcanum.Data.Messages;

/// <summary>
/// ARC-DATA-027: the one MES grammar for the data readers that need a table of number → text (lighting rules, terrain
/// name tables, step rules, the area owner's name tables). It is <see cref="MesFile"/>, the original's grammar: text
/// outside braces is ignored, and the shapes the original truncates, stops on or resolves arbitrarily are refused. The text
/// is the stored bytes as Latin-1 characters (each CR LF read as LF); callers parse numbers and names from it.
/// </summary>
internal static class MesTable
{
    /// <summary>Parses <paramref name="payload"/>; <paramref name="table"/> is empty unless the status is
    /// <see cref="MesParseStatus.Parsed"/>.</summary>
    internal static MesParseStatus Read(ReadOnlySpan<byte> payload, MesReaderOptions options, out SortedDictionary<int, string> table)
    {
        table = new SortedDictionary<int, string>();
        var parsed = MesFile.Parse(payload, options);
        if (parsed.Status != MesParseStatus.Parsed) return parsed.Status;
        foreach (var message in parsed.File!.Messages) table.Add(message.Number, Encoding.Latin1.GetString(message.Bytes.Span));
        return MesParseStatus.Parsed;
    }

    /// <summary>A limit refusal rather than a grammar one.</summary>
    internal static bool IsLimit(MesParseStatus status) =>
        status is MesParseStatus.TooLarge or MesParseStatus.TooManyMessages or MesParseStatus.TextTooLong;
}
