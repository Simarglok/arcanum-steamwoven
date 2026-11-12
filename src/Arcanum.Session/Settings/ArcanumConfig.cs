using System.Text;

namespace Arcanum.Session.Settings;

/// <summary>How a settings file read: absent (<see cref="Missing"/>), read (<see cref="Read"/>, the usable lines),
/// not a settings text (<see cref="Invalid"/>: longer than <see cref="ArcanumConfig.MaxFileBytes"/>; the next write
/// replaces it) or a file that exists but cannot be read (<see cref="Unreadable"/>, never overwritten by an ordinary write).</summary>
public enum ArcanumConfigReadStatus { Missing, Read, Invalid, Unreadable }

public enum ArcanumConfigSaveStatus { Saved, RefusedUnreadable, Failed }

/// <summary>One <c>name=value</c> line.</summary>
public sealed record ArcanumConfigEntry(string Name, string Value);

/// <summary>A change of one setting: <paramref name="Value"/> null removes the line.</summary>
public readonly record struct ArcanumConfigChange(string Name, string? Value);

/// <summary>The lines of a settings file in file order. <see cref="Dropped"/> counts the lines that were not usable (no <c>=</c>,
/// an empty or invalid name, an empty or invalid value, too long). A repeated name (compared ignoring case) keeps its first position
/// and the value of its last line.</summary>
public sealed record ArcanumConfigRead(ArcanumConfigReadStatus Status, IReadOnlyList<ArcanumConfigEntry> Entries, int Dropped)
{
    public static ArcanumConfigRead Empty(ArcanumConfigReadStatus status) => new(status, [], 0);

    /// <summary>The value of <paramref name="name"/> (compared ignoring case), or null when the file has no such line.</summary>
    public string? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var entry in Entries)
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)) return entry.Value;
        return null;
    }

    public bool Has(string name) => Get(name) is not null;
}

/// <summary>
/// ARC-UI-1130: the one settings file (<c>arcanum.cfg</c>), in the original's format. The original's store
/// (OP8, OP9, <c>docs/research/ledger/ui-audio.md</c>; load 0x438B50, set 0x438DF0, save 0x438C20) reads a text file line by line, cuts each
/// line at its first <c>=</c>, trims both parts, ignores a line whose name or value is empty or that has no <c>=</c>, matches names
/// ignoring case, and writes every entry back as <c>name=value</c> lines, a line each. This class reads and writes the same
/// shape, and adds what the player's own file needs (PROJECT, none of it from the original): lines and files have limits, a write goes to a temporary
/// file in the same folder and replaces the file by a rename, a write changes only the named lines (every other line, from this build or a
/// newer one, is kept in its place; a line that is not a usable pair is dropped, as the original's rewrite drops it), and a new line goes
/// to the end of the file (the original puts a new entry first). The file is the project's own: the original's <c>arcanum.cfg</c> in the
/// player's installation is never read or written.
/// <para>Two game instances that save different groups at the same moment can lose one group's change (a read, change and write without a lock); the settings are for one running game at a time.</para>
/// <para>The host picks the path (Godot's <c>user://</c> for a player, a test path, or none for an automated run). Reading and writing never
/// throw for the file's state; a caller error (an invalid name or value) throws.</para>
/// </summary>
public static class ArcanumConfig
{
    public const string FileName = "arcanum.cfg";
    /// <summary>The longest name (printable ASCII without <c>=</c>).</summary>
    public const int MaxNameChars = 64;
    /// <summary>The longest value; the original reads a line through a 256 byte buffer, which the project's settings (a game folder path,
    /// a stored launcher object) do not fit.</summary>
    public const int MaxValueChars = 64 * 1024;
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxEntries = 512;

    // A byte that is not UTF-8 becomes U+FFFD, and a line holding one is dropped alone (Parse): one bad byte never costs the other lines.
    private static readonly UTF8Encoding LenientUtf8 = new(false, throwOnInvalidBytes: false);

    /// <summary>A name the file can hold: 1 to <see cref="MaxNameChars"/> printable ASCII characters, no <c>=</c>, no leading or trailing space.</summary>
    public static bool ValidName(string? name)
        => name is { Length: > 0 and <= MaxNameChars } && name.All(c => c is >= ' ' and <= '~' and not '=') && name[0] != ' ' && name[^1] != ' ';

    /// <summary>A value the file can hold: not empty, no control character, no leading or trailing white space, at most <see cref="MaxValueChars"/>.</summary>
    public static bool ValidValue(string? value)
        => value is { Length: > 0 and <= MaxValueChars } && !value.Any(char.IsControl) && !char.IsWhiteSpace(value[0]) && !char.IsWhiteSpace(value[^1]);

    /// <summary>The lines of <paramref name="text"/>.</summary>
    public static ArcanumConfigRead Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new List<ArcanumConfigEntry>();
        var dropped = 0;
        if (text.StartsWith('\uFEFF')) text = text[1..];
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.Contains('\uFFFD', StringComparison.Ordinal)) { dropped++; continue; }
            var split = line.IndexOf('=', StringComparison.Ordinal);
            if (split < 0) { dropped++; continue; }
            var name = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (!ValidName(name) || !ValidValue(value)) { dropped++; continue; }
            var existing = entries.FindIndex(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) entries[existing] = entries[existing] with { Value = value };
            else if (entries.Count < MaxEntries) entries.Add(new(name, value));
            else dropped++;
        }
        return new(ArcanumConfigReadStatus.Read, entries.AsReadOnly(), dropped);
    }

    /// <summary>The file text of <paramref name="entries"/>: <c>name=value</c> and a line feed each.</summary>
    public static string Format(IEnumerable<ArcanumConfigEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var text = new StringBuilder();
        foreach (var entry in entries) text.Append(entry.Name).Append('=').Append(entry.Value).Append('\n');
        return text.ToString();
    }

    public static ArcanumConfigRead Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return ArcanumConfigRead.Empty(ArcanumConfigReadStatus.Missing);
            if (file.Length > MaxFileBytes) return ArcanumConfigRead.Empty(ArcanumConfigReadStatus.Invalid);
            return Parse(LenientUtf8.GetString(File.ReadAllBytes(path)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return ArcanumConfigRead.Empty(ArcanumConfigReadStatus.Unreadable);
        }
    }

    /// <summary>Applies <paramref name="changes"/> to the file at <paramref name="path"/> and writes it. A file that exists but cannot be read
    /// is not replaced (<see cref="ArcanumConfigSaveStatus.RefusedUnreadable"/>) unless <paramref name="replaceUnreadable"/> (the player's
    /// reset); a file that is not a settings text (<see cref="ArcanumConfigReadStatus.Invalid"/>) is replaced, its usable lines lost. An
    /// invalid name or value throws; a name repeated in <paramref name="changes"/> takes its last value.</summary>
    public static ArcanumConfigSaveStatus Save(string path, IEnumerable<ArcanumConfigChange> changes, bool replaceUnreadable = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(changes);
        var list = changes.ToArray();
        foreach (var change in list)
            if (!ValidName(change.Name) || change.Value is not null && !ValidValue(change.Value))
                throw new ArgumentException($"The setting '{change.Name}' cannot be stored.", nameof(changes));
        var current = Read(path);
        if (current.Status == ArcanumConfigReadStatus.Unreadable && !replaceUnreadable) return ArcanumConfigSaveStatus.RefusedUnreadable;
        var entries = current.Entries.ToList();
        foreach (var change in list)
        {
            var index = entries.FindIndex(entry => string.Equals(entry.Name, change.Name, StringComparison.OrdinalIgnoreCase));
            if (change.Value is null)
            {
                if (index >= 0) entries.RemoveAt(index);
            }
            else if (index >= 0) entries[index] = entries[index] with { Value = change.Value };
            else if (entries.Count < MaxEntries) entries.Add(new(change.Name, change.Value));
            else return ArcanumConfigSaveStatus.Failed;
        }
        return Write(path, Format(entries));
    }

    /// <summary>A write that was cut off by a crash leaves its temporary file; one older than an hour is removed before the next write.</summary>
    private static void DeleteStaleTemporaries(string full)
    {
        try
        {
            foreach (var stale in Directory.EnumerateFiles(Path.GetDirectoryName(full)!, Path.GetFileName(full) + ".*.tmp"))
                if (File.GetLastWriteTimeUtc(stale) < DateTime.UtcNow.AddHours(-1)) File.Delete(stale);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static ArcanumConfigSaveStatus Write(string path, string text)
    {
        string? temporary = null;
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(text);
            if (bytes.Length > MaxFileBytes) return ArcanumConfigSaveStatus.Failed;
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            DeleteStaleTemporaries(full);
            temporary = $"{full}.{Guid.NewGuid():N}.tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, full, overwrite: true);
            temporary = null;
            return ArcanumConfigSaveStatus.Saved;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return ArcanumConfigSaveStatus.Failed;
        }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
