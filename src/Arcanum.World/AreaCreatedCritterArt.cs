using Arcanum.Data;
using Arcanum.Data.Area;

namespace Arcanum.World;

public enum AreaCreatedCritterArtStatus
{
    Ready,
    /// <summary>The art id is no critter (2), monster (12) or unique NPC (13) art.</summary>
    NotACritterArt,
    /// <summary>The art id names no file (a rule the owner does not admit, or a monster or unique NPC number the name table has no row for).</summary>
    NameUnresolved,
    /// <summary>No archive of the window's art sources holds the file.</summary>
    MissingFile,
    /// <summary>The file is held twice by one archive, or does not read or decode.</summary>
    Unreadable,
}

/// <summary>The outcome of <see cref="AreaCreatedCritterArt.Load"/>: the visual and the first pose of the body, or why none (<see cref="Detail"/> names the file).</summary>
public sealed record AreaCreatedCritterArtLoad(AreaCreatedCritterArtStatus Status, AreaVisualRecord? Visual, AreaBodyPose? Pose, string Detail);

/// <summary>
/// ARC-DEV-370 (E1-c2): the body of a critter no map row carries (a critter the session created at run time): from its prototype's art id the file the original names (N1, the owner's critter name rules),
/// read from the first archive of the window's art sources that holds it, as the visual and the pose a map NPC's body has at T0. The visual stands for the body's archive entry exactly as the loader's
/// does (the entry identity the move ARTs find the body's other animations by, <see cref="AreaBodyMoveArt.Load"/>), so the walk, run, armed and fallen poses read for a created critter the way they do for a
/// map NPC. The visual is a map NPC's: the art id's own facing and frame, the frame's unmirrored hotspot, and <see cref="AreaVisualRecord.Mirrored"/> for a facing of 1 to 3 (the plan's command flips by it, the
/// pose's own hotspot is the mirrored one). Not done: the missing-art repair (N2, MA1) of the art id, which needs the masks of the game's file list: a name whose file is missing is reported, not repaired.
/// Godot-free; reads files (the archives' indexes and one entry), so it is called once per art id and kept.
/// </summary>
public static class AreaCreatedCritterArt
{
    public const string Policy = "steamwoven-created-critter-v1";
    private const uint DirectoryRecord = 0x400, IgnoredRecord = 0x800;
    private static readonly DatReaderOptions Options = new() { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 };

    /// <summary>The visual key of a created critter's body: one per art id, on the static source kind of runtime map 0 (no map's sector).</summary>
    public static AreaVisualKey VisualKey(uint aid) => new(AreaSourceKind.Static, new("created-critter", 0, 0), unchecked((int)aid), Policy);

    /// <param name="aid">The prototype's current art id.</param>
    /// <param name="artSources">The window's art archives (<see cref="AreaLoadResult.ArtSources"/>), searched in their order.</param>
    /// <param name="name">The name tables of the monster and unique NPC art ids (null when a row is missing).</param>
    public static AreaCreatedCritterArtLoad Load(uint aid, IReadOnlyDictionary<AreaInputKey, string> artSources, Func<AreaMappingRole, int, string?> name)
    {
        ArgumentNullException.ThrowIfNull(artSources); ArgumentNullException.ThrowIfNull(name);
        if (aid >> 28 is not (2 or 12 or 13)) return Refused(AreaCreatedCritterArtStatus.NotACritterArt, $"art class {aid >> 28}");
        string path;
        try { path = AreaDataOwner.CritterArtPath(aid, name); }
        catch (AreaException error) { return Refused(AreaCreatedCritterArtStatus.NameUnresolved, $"art 0x{aid:X8} {error.Message}"); }
        var nominal = (int)((aid >> 11) & 7); var frame = (int)((aid >> 14) & 31); var palette = (int)((aid >> 4) & 3); var animation = (int)((aid >> 6) & 31);
        foreach (var (key, archivePath) in artSources)
        {
            if (!File.Exists(archivePath)) continue;
            try
            {
                using var archive = DatArchive.Open(archivePath, Options);
                var held = archive.Entries.Where(e => (e.Flags & (DirectoryRecord | IgnoredRecord)) == 0 && string.Equals(AreaFallenPoses.Name(e), path, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (held.Length == 0) continue;
                if (held.Length > 1) return Refused(AreaCreatedCritterArtStatus.Unreadable, $"{path} is held {held.Length} times by {key}");
                var entry = held[0];
                var identity = new AreaEntryIdentity(key, entry.Ordinal, entry.Flags, entry.StoredSize, entry.DeclaredOutputSize, entry.Offset, AreaBodyArt.Digest(entry.RawNameBytes.ToArray()));
                var art = new AreaBodyArt(animation, AreaFallenPoses.Name(entry), archive.ReadEntry(entry), identity, palette);
                var pose = art.Pose(nominal, frame);
                // As a map NPC's visual: the unmirrored frame hotspot and the Mirrored flag of a facing of 1 to 3 (the plan's command flips by it; the move poses then flip by the object's own flip only).
                var visual = new AreaVisualRecord(VisualKey(aid), aid, aid, identity, pose.Key.PayloadSha256, pose.Key.ViewLength, nominal, pose.FlipX ? 8 - nominal : nominal, pose.Key.Group, pose.Key.Frame, palette,
                    pose.Key.Palette, pose.FlipX, pose.Frame.Width, pose.Frame.Height, pose.FlipX ? pose.Frame.Width - pose.HotX - 1 : pose.HotX, pose.HotY, 0, 0, 0, 0, 100, 0, pose.Frame.Rgba.Length,
                    pose.Key.RgbaSha256, Policy);
                return new(AreaCreatedCritterArtStatus.Ready, visual, pose, path);
            }
            catch (Exception error) when (error is DatException or ArtException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException)
            {
                return Refused(AreaCreatedCritterArtStatus.Unreadable, $"{path} {error.GetType().Name} {error.Message}");
            }
        }
        return Refused(AreaCreatedCritterArtStatus.MissingFile, path);
    }

    private static AreaCreatedCritterArtLoad Refused(AreaCreatedCritterArtStatus status, string detail) => new(status, null, null, detail);
}
