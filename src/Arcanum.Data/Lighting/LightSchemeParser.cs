using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Lighting;

/// <summary>The lighting readers' MES access. ARC-DATA-027: the files are read with the original grammar
/// (<see cref="MesFile"/>: text outside braces is ignored, multi-line text is kept, the shapes the original
/// truncates or resolves arbitrarily are refused), so a file the game reads is not refused for its comments or
/// notes. A refused shape is <see cref="LightingFailure.MesGrammar"/>, a limit <see cref="LightingFailure.LimitExceeded"/>.
/// Text is kept as Latin-1 characters of the stored bytes; callers parse numbers and names from it.</summary>
internal static class LightingMes
{
    internal const int MaxPayloadBytes = 1 << 20;
    internal const int MaxRecords = 8192;
    private static readonly MesReaderOptions Options = new() { MaxBytes = MaxPayloadBytes, MaxMessages = MaxRecords };

    internal static LightingFailure TryParse(ReadOnlySpan<byte> payload, out SortedDictionary<int, string> records)
    {
        records = new SortedDictionary<int, string>();
        if (payload.Length > MaxPayloadBytes) return LightingFailure.LimitExceeded;
        var status = MesTable.Read(payload, Options, out records);
        return status == MesParseStatus.Parsed ? LightingFailure.None : MesTable.IsLimit(status) ? LightingFailure.LimitExceeded : LightingFailure.MesGrammar;
    }

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
