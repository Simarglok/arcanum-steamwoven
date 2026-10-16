using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Arcanum.Data.Lighting;

/// <summary>Bounded MES subset for the lighting and startup rule files: <c>{key}{value}</c> pairs,
/// whitespace, <c>//</c> comments (whole-line or trailing) and the bare 1..12 AM/PM hour notes
/// observed after hourly rows. Whitespace is ASCII space, tab, CR and LF only. Anything else fails;
/// there is no universal MES implementation here. A row of only spaces/tabs counts as empty.</summary>
internal static class LightingMes
{
    internal const int MaxPayloadBytes = 1 << 20;
    internal const int MaxRecords = 8192;
    internal const int MaxValueChars = 4096;

    internal static LightingFailure TryParse(ReadOnlySpan<byte> payload, out SortedDictionary<int, string> records)
    {
        records = new SortedDictionary<int, string>();
        if (payload.Length > MaxPayloadBytes) return LightingFailure.LimitExceeded;
        var text = Encoding.Latin1.GetString(payload); var at = 0;
        while (at < text.Length)
        {
            var c = text[at];
            if (c is ' ' or '\t' or '\r' or '\n') { at++; continue; }
            if (c == '/' && at + 1 < text.Length && text[at + 1] == '/') { at = LineEnd(text, at); continue; }
            if (c != '{') return LightingFailure.MesGrammar;
            var keyEnd = text.IndexOf('}', at + 1);
            if (keyEnd < 0 || keyEnd == at + 1 || keyEnd - at - 1 > 9) return LightingFailure.MesGrammar;
            for (var i = at + 1; i < keyEnd; i++) if (!char.IsAsciiDigit(text[i])) return LightingFailure.MesGrammar;
            var key = int.Parse(text.AsSpan(at + 1, keyEnd - at - 1), NumberStyles.None, CultureInfo.InvariantCulture);
            at = keyEnd + 1;
            while (at < text.Length && text[at] is ' ' or '\t') at++;
            if (at == text.Length || text[at] != '{') return LightingFailure.MesGrammar;
            var valueEnd = text.IndexOf('}', at + 1);
            if (valueEnd < 0) return LightingFailure.MesGrammar;
            if (valueEnd - at - 1 > MaxValueChars) return LightingFailure.LimitExceeded;
            var value = text[(at + 1)..valueEnd];
            if (value.Contains('{') || value.Contains('\n') || value.Contains('\r')) return LightingFailure.MesGrammar;
            if (records.Count == MaxRecords) return LightingFailure.LimitExceeded;
            if (!records.TryAdd(key, value)) return LightingFailure.MesGrammar;
            at = valueEnd + 1;
            while (at < text.Length && text[at] is ' ' or '\t') at++;
            if (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                var end = at; while (end < text.Length && text[end] is not (' ' or '\t' or '\r' or '\n')) end++;
                if (!IsHourNote(text.AsSpan(at, end - at))) return LightingFailure.MesGrammar;
                at = end;
            }
        }
        return LightingFailure.None;
    }

    private static int LineEnd(string text, int at)
    {
        var end = text.IndexOf('\n', at); return end < 0 ? text.Length : end + 1;
    }

    private static bool IsHourNote(ReadOnlySpan<char> token) =>
        token.Length is 3 or 4 && (token.EndsWith("AM", StringComparison.Ordinal) || token.EndsWith("PM", StringComparison.Ordinal)) &&
        int.TryParse(token[..^2], NumberStyles.None, CultureInfo.InvariantCulture, out var hour) && hour is >= 1 and <= 12 &&
        token[0] != '0';

    internal static bool TryInteger(string value, out int result) =>
        int.TryParse(value.Trim(' ', '\t'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

/// <summary>FR-LIGHT-003/004: one hourly ambient scheme. Strict project policy (ARC-RES-018 proposal):
/// all 24 keys present, nonempty rows exactly six integers 0..255 in outdoor-RGB, indoor-RGB order,
/// hours 0 and 23 explicit, each interior empty run filled per channel as
/// <c>left + distance * trunc((right - left) / span)</c> from its own bracketing anchors.</summary>
public static class LightSchemeParser
{
    public static LightingFailure TryParse(int schemeId, ReadOnlySpan<byte> payload, out LightSchemeTable? table)
    {
        table = null;
        var failure = LightingMes.TryParse(payload, out var records);
        if (failure != LightingFailure.None) return failure;
        var rows = new int[24][]; var explicitHours = new bool[24];
        for (var hour = 0; hour < 24; hour++)
        {
            if (!records.TryGetValue(hour, out var value)) return LightingFailure.MissingHour;
            if (value.Trim(' ', '\t').Length == 0) continue;
            var parts = value.Split(',');
            if (parts.Length != 6) return LightingFailure.ChannelCount;
            var row = new int[6];
            for (var channel = 0; channel < 6; channel++)
                if (!LightingMes.TryInteger(parts[channel], out row[channel]) || row[channel] is < 0 or > 255) return LightingFailure.ChannelRange;
            rows[hour] = row; explicitHours[hour] = true;
        }
        if (!explicitHours[0] || !explicitHours[23]) return LightingFailure.EdgeHourEmpty;
        var left = 0;
        for (var right = 1; right < 24; right++)
        {
            if (!explicitHours[right]) continue;
            var span = right - left;
            for (var hour = left + 1; hour < right; hour++)
            {
                var filled = new int[6];
                for (var channel = 0; channel < 6; channel++)
                    filled[channel] = rows[left][channel] + (hour - left) * ((rows[right][channel] - rows[left][channel]) / span);
                rows[hour] = filled;
            }
            left = right;
        }
        var hours = new AmbientPair[24];
        for (var hour = 0; hour < 24; hour++)
        {
            var r = rows[hour];
            hours[hour] = new(new((byte)r[0], (byte)r[1], (byte)r[2]), new((byte)r[3], (byte)r[4], (byte)r[5]));
        }
        table = new LightSchemeTable(schemeId, LightingMes.Hash(payload), hours, explicitHours);
        return LightingFailure.None;
    }
}
