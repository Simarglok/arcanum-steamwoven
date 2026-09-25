using Arcanum.Data;
using Godot;

namespace Arcanum.Godot;

public sealed record ViewerEntry(DatEntry Entry, string DisplayName);

public static class ViewerState
{
    public const uint EntryCapBytes = 64u * 1024u * 1024u;
    public const int MaxRequestedPaletteSlot = 3;

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

    public static void ResetEntrySelectors(SpinBox group, SpinBox frame, SpinBox palette)
    {
        group.MaxValue = 0;
        frame.MaxValue = 0;
        palette.MaxValue = 0;
        group.Value = 0;
        frame.Value = 0;
        palette.Value = 0;
    }

    public static void ConfigureEntrySelectors(SpinBox group, SpinBox frame, SpinBox palette, ArtMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        group.MaxValue = metadata.Header.PhysicalGroupCount - 1;
        frame.MaxValue = metadata.Header.FramesPerGroup - 1;
        // Once inspected metadata has committed, retain all requested slots so an
        // absent slot zero can recover through an explicit palette selection.
        palette.MaxValue = MaxRequestedPaletteSlot;
        group.Value = 0;
        frame.Value = 0;
        palette.Value = 0;
    }

    private static bool IsAsciiIgnoreCase(byte value, byte expected) =>
        value == expected || (value | 0x20) == expected;
}
