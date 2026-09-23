using Arcanum.Data;

namespace Arcanum.Godot;

public sealed record ViewerEntry(DatEntry Entry, string DisplayName);

public static class ViewerState
{
    public const uint EntryCapBytes = 64u * 1024u * 1024u;

    public static DatReaderOptions ReaderOptions { get; } = new()
    {
        MaxStoredEntryBytes = EntryCapBytes,
        MaxDecodedEntryBytes = EntryCapBytes,
    };

    public static ArtDecoderOptions DecoderOptions { get; } = new()
    {
        MaxInputBytes = checked((int)EntryCapBytes),
    };

    public static bool IsArtEntry(DatEntry entry)
    {
        var name = entry.RawNameBytes;
        return name.Count >= 5 && name[^1] == 0 &&
            IsAsciiIgnoreCase(name[^5], (byte)'.') &&
            IsAsciiIgnoreCase(name[^4], (byte)'a') &&
            IsAsciiIgnoreCase(name[^3], (byte)'r') &&
            IsAsciiIgnoreCase(name[^2], (byte)'t');
    }

    public static string DisplayName(DatEntry entry)
    {
        var name = entry.RawNameBytes;
        var length = name.Count > 0 && name[^1] == 0 ? name.Count - 1 : name.Count;
        var parts = new System.Text.StringBuilder(length + 16);
        for (var index = 0; index < length; index++)
        {
            var value = name[index];
            if (value is >= 0x20 and <= 0x7e)
            {
                parts.Append((char)value);
            }
            else
            {
                parts.Append($"\\x{value:X2}");
            }
        }

        parts.Append(" [ordinal ").Append(entry.Ordinal).Append(']');
        return parts.ToString();
    }

    public static bool MatchesFilter(ViewerEntry entry, string filter) =>
        string.IsNullOrWhiteSpace(filter) || entry.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        entry.Entry.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal);

    public static IReadOnlyList<ViewerEntry> ArtEntries(DatArchive archive) =>
        archive.Entries.Where(IsArtEntry).Select(entry => new ViewerEntry(entry, DisplayName(entry))).ToArray();

    private static bool IsAsciiIgnoreCase(byte value, byte expected) =>
        value == expected || (value | 0x20) == expected;
}
