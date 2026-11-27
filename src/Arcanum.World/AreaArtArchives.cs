using Arcanum.Data;

namespace Arcanum.World;

/// <summary>
/// ARC-RENDER-3414: the art archives the body loaders read (<see cref="AreaBodyMoveArt"/>), each kept open with its file records by
/// name. A load used to parse the archive's directory and build the name table again for every body, and a map window swap loads a
/// few dozen bodies in one frame. An archive whose length or write time changed is opened again; what a loader checks (the standing
/// entry the published snapshot names) is unchanged. One owner on one thread, which disposes it: an archive handed out is
/// closed by a later call that finds its file changed, reaches the limit, forgets it or disposes the set.
/// </summary>
public sealed class AreaArtArchives : IDisposable
{
    /// <summary>The most archives held at once; one more closes all of them first (an installation names far fewer).</summary>
    public const int MaxArchives = 16;

    private readonly object _gate = new();
    private readonly Dictionary<string, AreaArtArchive> _held = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>The archives parsed since this was made (diagnostics and tests).</summary>
    public int Opened { get; private set; }

    /// <summary>The held archive of <paramref name="path"/>, opened now when it is not held or its file changed. A failed open is not kept
    /// (its exception goes to the caller).</summary>
    internal AreaArtArchive Get(string path, DatReaderOptions options)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var file = new FileInfo(path);
            var stamp = (file.Length, file.LastWriteTimeUtc);
            if (_held.TryGetValue(path, out var held))
            {
                if (held.Stamp == stamp) return held;
                _held.Remove(path);
                held.Archive.Dispose();
            }
            if (_held.Count >= MaxArchives) Close();
            var archive = DatArchive.Open(path, options);
            try { held = new(archive, stamp); }
            catch { archive.Dispose(); throw; }
            Opened++;
            return _held[path] = held;
        }
    }

    /// <summary>Closes the held archive of <paramref name="path"/>, if any: a loader drops one whose read failed, so the next load
    /// opens the file again.</summary>
    internal void Forget(string path)
    {
        lock (_gate)
            if (_held.Remove(path, out var held)) held.Archive.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }
    }

    private void Close()
    {
        foreach (var held in _held.Values) held.Archive.Dispose();
        _held.Clear();
    }
}

/// <summary>One open art archive of <see cref="AreaArtArchives"/>.</summary>
internal sealed class AreaArtArchive
{
    private const uint DirectoryRecord = 0x400, IgnoredRecord = 0x800;

    internal AreaArtArchive(DatArchive archive, (long Length, DateTime Written) stamp)
    {
        Archive = archive;
        Stamp = stamp;
        // One pass over the file records (no directory or ignored ones); a name held twice is ambiguous and never chosen.
        var named = new Dictionary<string, DatEntry?>(archive.Entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if ((entry.Flags & (DirectoryRecord | IgnoredRecord)) != 0) continue;
            var name = AreaFallenPoses.Name(entry);
            if (!named.TryAdd(name, entry)) named[name] = null;
        }
        Named = named;
    }

    internal DatArchive Archive { get; }
    internal (long Length, DateTime Written) Stamp { get; }
    /// <summary>The file records by name; null for a name held twice.</summary>
    internal IReadOnlyDictionary<string, DatEntry?> Named { get; }
}
