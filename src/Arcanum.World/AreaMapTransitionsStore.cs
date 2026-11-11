using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-392 (Q7 of the smoke speed diagnosis, O5b's second half): a map's transitions and the map list's destinations kept on the device for the next process.
/// <see cref="AreaMapTransitionsCache"/> (ARC-DEV-368) holds them for one process, so every fresh process, above all every smoke run (one process per run), still
/// read the main map's sector files and scripts again: 1.8 s of its 10 s load. This store writes what a read produced to one small file per map and answers the
/// next process from it, so the read is skipped when nothing it depends on has changed.
/// <para><b>The stamp.</b> A file is named by the SHA-256 of its key and holds the key; it counts only when the whole key is equal. The key is the map, the stack's
/// <see cref="AreaMapTransitionsCache.Identity"/> (mount paths, each archive's size and last write time, the number of indexed names), a digest of every loose file's path,
/// size and last write time (<see cref="ContentStack.LooseFilesFingerprint"/>: the in-process cache accepts that a loose file edited in place is not seen, a file
/// that outlives the process cannot) and the identity of the code that reads and writes the records (the module version ids of <c>Arcanum.Data</c>, which holds
/// the reader, and <c>Arcanum.World</c>, which holds this store and the destination rule, so any rebuilt reader finds no old file). A file whose header, length,
/// payload digest or records are not exactly what they should be is a miss, never an error; every failure of the disk is a miss too, so the store can only
/// make a load faster, never fail or change it. Records read back equal records read fresh (<see cref="MapTransitionSetCodec"/>).</para>
/// <para><b>Where.</b> <c>map-cache/</c> in the device's Steamwoven folder (<see cref="DeviceFolder"/>: <c>~/Library/Application Support/Arcanum Steamwoven Game/</c> on
/// a Mac, <c>%APPDATA%\Arcanum Steamwoven Game\</c> on Windows), not the Godot user folder, which every smoke run replaces with a fresh temporary one. Off until a host
/// names a folder (<see cref="UseDeviceFolder"/>, called once by the viewer), so tests and tools never write into the owner's folder. At most <see cref="MaxFiles"/> files
/// are kept (the oldest by last use go first). The file is written under a temporary name and renamed, so two processes never see a half file.</para>
/// <para><b>Beside the resident caches.</b> The strict build's resident frames and visuals (ARC-DEV-375, ARC-DEV-383) and the editor-record memo (ARC-DEV-388) live in
/// process memory and key by the module's own bytes; they never touch this folder and it never reads them. The master switch <c>ARC_AREA_RESIDENT=0</c> (which makes the
/// map path keep nothing between loads, for before/after timing) turns this store off too, so such a run still reads everything cold; <c>ARC_MAP_CACHE=0</c> turns only this
/// store off, <c>ARC_MAP_CACHE_DIR</c> names another folder.</para>
/// </summary>
public static class AreaMapTransitionsStore
{
    /// <summary>The layout of the file (not of the records: those carry <see cref="MapTransitionSetCodec.Version"/>).</summary>
    internal const byte Version = 1;
    /// <summary>Files kept in the folder; the next write removes the least recently used beyond this.</summary>
    internal const int MaxFiles = 96;
    internal const string Extension = ".mt";
    private static readonly byte[] Magic = "ARMT"u8.ToArray();
    private const int FixedHeader = 8;
    // A file larger than any real set (the main map's is 118 KB) is not read.
    private const long MaxFileBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan TouchAfter = TimeSpan.FromHours(6), StaleTemporary = TimeSpan.FromHours(1);

    private static readonly bool Switch = Environment.GetEnvironmentVariable("ARC_MAP_CACHE") != "0" && AreaResidentVisuals.Enabled;
    private static string? _root;
    private static string? _codeStamp;

    /// <summary>The folder files are kept in; null, the default, keeps nothing on disk. A host sets it once (<see cref="UseDeviceFolder"/>); tests set their own.</summary>
    public static string? Root { get => Volatile.Read(ref _root); internal set => Volatile.Write(ref _root, value); }

    /// <summary>The identity of the code whose records are stored; tests set another to see the stamp work.</summary>
    internal static string? CodeStampOverride { get; set; }

    /// <summary>The device's folder of Steamwoven's own files.</summary>
    public static string DeviceFolder()
    {
        var support = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(support, "Arcanum Steamwoven Game", "map-cache");
    }

    /// <summary>Turns the store on in <c>ARC_MAP_CACHE_DIR</c> or <see cref="DeviceFolder"/> (unless <c>ARC_MAP_CACHE=0</c> or <c>ARC_AREA_RESIDENT=0</c> is set). The
    /// viewer calls it once at start; the folder is created by the first write.</summary>
    public static void UseDeviceFolder()
    {
        if (!Switch) { Root = null; return; }
        Root = Environment.GetEnvironmentVariable("ARC_MAP_CACHE_DIR") is { Length: > 0 } named ? named : DeviceFolder();
    }

    /// <summary>The identity of the code that reads and writes the records: one id per rebuilt assembly.</summary>
    internal static string CodeStamp() => CodeStampOverride ?? (_codeStamp ??=
        $"data={typeof(MapTransitionSet).Assembly.ManifestModule.ModuleVersionId:N};world={typeof(AreaMapTransitionsStore).Assembly.ManifestModule.ModuleVersionId:N};codec={MapTransitionSetCodec.Version};file={Version}");

    internal static string Key(ContentStack stack, WorldMap map) =>
        $"{map.Name}|{CodeStamp()}|{AreaMapTransitionsCache.Identity(stack)}|{stack.LooseFilesFingerprint()}";

    internal static string PathFor(string root, string key) =>
        Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant() + Extension);

    /// <summary>The kept set and destinations of <paramref name="map"/> for the stack as it is now, and the key to write under when there are none (null when the store is off).</summary>
    internal static AreaMapTransitionsCache.Held? TryRead(ContentStack stack, WorldMap map, out string? key)
    {
        key = null;
        if (Root is not { } root) return null;
        try
        {
            key = Key(stack, map);
            var path = PathFor(root, key);
            if (!File.Exists(path) || new FileInfo(path).Length > MaxFileBytes) return null;
            var held = Decode(File.ReadAllBytes(path), key, map);
            if (held is not null) Touch(path);
            return held;
        }
        catch (Exception error) when (Refused(error)) { return null; }
    }

    /// <summary>Keeps a read under <paramref name="key"/>; false when the store is off or the disk refused (nothing is thrown).</summary>
    internal static bool Write(string? key, MapTransitionSet transitions, IReadOnlyDictionary<int, AreaMapDestination> destinations)
    {
        if (key is null || Root is not { } root) return false;
        // A set with a file the content could not read at this moment is not kept: the next process may read it (the other issues are the content's own and repeat).
        if (transitions.Issues.Any(issue => issue.Code == WorldIssueCode.Unreadable)) return false;
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = PathFor(root, key);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, Encode(key, transitions, destinations));
            File.Move(temporary, path, overwrite: true);
            temporary = null;
            Prune(root, path);
            return true;
        }
        catch (Exception error) when (Refused(error)) { return false; }
        finally { if (temporary is not null) try { File.Delete(temporary); } catch (Exception error) when (Refused(error)) { } }
    }

    internal static byte[] Encode(string key, MapTransitionSet transitions, IReadOnlyDictionary<int, AreaMapDestination> destinations)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(destinations.Count);
            foreach (var (runtime, destination) in destinations.OrderBy(pair => pair.Key))
            { writer.Write(runtime); writer.Write(destination.Runtime); writer.Write(destination.Catalogue); writer.Write(destination.Name); }
            writer.Write(MapTransitionSetCodec.Write(transitions));
        }
        var body = payload.ToArray();
        var keyBytes = Encoding.UTF8.GetBytes(key);
        using var file = new MemoryStream();
        using (var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic); writer.Write(Version); writer.Write((byte)0); writer.Write((ushort)0);
            writer.Write(keyBytes.Length); writer.Write(keyBytes);
            writer.Write(body.Length); writer.Write(SHA256.HashData(body)); writer.Write(body);
        }
        return file.ToArray();
    }

    internal static AreaMapTransitionsCache.Held? Decode(byte[] bytes, string key, WorldMap map)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (bytes.Length < FixedHeader || !reader.ReadBytes(4).AsSpan().SequenceEqual(Magic) || reader.ReadByte() != Version) return null;
            reader.ReadByte(); reader.ReadUInt16();
            var keyLength = reader.ReadInt32();
            if (keyLength < 0 || keyLength > stream.Length - stream.Position || !reader.ReadBytes(keyLength).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(key))) return null;
            var bodyLength = reader.ReadInt32(); var digest = reader.ReadBytes(32);
            if (bodyLength < 0 || digest.Length != 32 || bodyLength != stream.Length - stream.Position) return null;
            var body = reader.ReadBytes(bodyLength);
            if (!SHA256.HashData(body).AsSpan().SequenceEqual(digest)) return null;
            using var payload = new MemoryStream(body, writable: false);
            using var inner = new BinaryReader(payload, Encoding.UTF8);
            var count = inner.ReadInt32();
            if (count < 0 || (long)count * 9 > payload.Length - payload.Position) return null;
            var destinations = new Dictionary<int, AreaMapDestination>(count);
            for (var n = 0; n < count; n++)
            {
                var runtime = inner.ReadInt32();
                if (!destinations.TryAdd(runtime, new(inner.ReadInt32(), inner.ReadInt32(), inner.ReadString()))) return null;
            }
            var set = MapTransitionSetCodec.Read(body.AsSpan((int)payload.Position), map);
            return set is null ? null : new(set, destinations);
        }
        catch (Exception error) when (error is EndOfStreamException or IOException or FormatException or ArgumentException or OverflowException) { return null; }
    }

    // A file used again is renewed now and then (at most every few hours), so the pruning below removes the ones nobody reads.
    private static void Touch(string path)
    {
        try { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TouchAfter) File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
        catch (Exception error) when (IsDisk(error)) { }
    }

    private static void Prune(string root, string keep)
    {
        try
        {
            var directory = new DirectoryInfo(root);
            foreach (var stale in directory.EnumerateFiles("*.tmp").Where(f => DateTime.UtcNow - f.LastWriteTimeUtc > StaleTemporary))
                try { stale.Delete(); } catch (Exception error) when (IsDisk(error)) { }
            var files = directory.EnumerateFiles("*" + Extension).OrderByDescending(f => f.FullName == keep).ThenByDescending(f => f.LastWriteTimeUtc).ToList();
            foreach (var old in files.Skip(MaxFiles))
                try { old.Delete(); } catch (Exception error) when (IsDisk(error)) { }
        }
        catch (Exception error) when (IsDisk(error)) { }
    }

    // Whatever the disk, the path or the bytes do, the store answers "nothing" and the load goes on; only memory exhaustion is not its business.
    private static bool Refused(Exception error) => error is not OutOfMemoryException;
    private static bool IsDisk(Exception error) => error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException;
}
