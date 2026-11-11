using System.Text;

namespace Arcanum.Data.Maps;

/// <summary>
/// ARC-DEV-392: a <see cref="MapTransitionSet"/> as bytes, so a process can keep what it read for the next process
/// (<c>Arcanum.World</c>'s <c>AreaMapTransitionsStore</c>). Every field of the set is written except its <see cref="WorldMap"/>, which the reader is
/// given (the catalogue of the same content builds it far cheaper than the set), and nothing is decided on the way: a set read back equals the
/// set written. Little-endian, length-prefixed UTF-8 strings, no reflection. A reader that finds a truncated or inconsistent buffer, a
/// version it does not know, or a set of another map answers null and never throws on bytes; the caller then reads the content as before.
/// The format is not an interchange format: any change to the records above bumps <see cref="Version"/> (and the store's stamp already includes the
/// assembly's identity, so a rebuilt writer never meets an older file).
/// </summary>
public static class MapTransitionSetCodec
{
    /// <summary>The layout version, written first.</summary>
    public const byte Version = 1;
    private const int MaxCount = 1 << 24;

    /// <summary>The bytes of <paramref name="set"/>.</summary>
    public static byte[] Write(MapTransitionSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Version);
            writer.Write(set.Map.Name);
            writer.Write(set.IsStartMap);
            if (set.Entry is { } entry)
            {
                writer.Write(true);
                writer.Write(entry.Catalogue); writer.Write(entry.Name); PutOptional(writer, entry.Start);
                writer.Write((int)entry.Type); writer.Write(entry.Area);
            }
            else writer.Write(false);
            writer.Write(set.Sites.Count);
            foreach (var site in set.Sites)
            {
                writer.Write((byte)site.Trigger); Put(writer, site.Location);
                if (site.Object is { } owner)
                {
                    writer.Write(true);
                    if (owner.Sector is { } sector) { writer.Write(true); writer.Write(sector.X); writer.Write(sector.Y); } else writer.Write(false);
                    writer.Write(owner.Index); writer.Write(owner.OwnIdSha256); writer.Write(owner.Type);
                    if (owner.Prototype is { } prototype) { writer.Write(true); writer.Write(prototype); } else writer.Write(false);
                }
                else writer.Write(false);
                writer.Write(site.Script);
                writer.Write(site.Chain.Count);
                foreach (var script in site.Chain) writer.Write(script);
                writer.Write(site.Line); writer.Write(site.Fade); writer.Write(site.OneShot);
                writer.Write(site.DestinationCatalogue); writer.Write(site.DestinationRuntime); Put(writer, site.Arrival);
                writer.Write((int)site.Guards);
            }
            writer.Write(set.Issues.Count);
            foreach (var issue in set.Issues)
            {
                writer.Write((byte)issue.Code); writer.Write(issue.Path);
                if (issue.Index is { } index) { writer.Write(true); writer.Write(index); } else writer.Write(false);
                if (issue.Detail is { } detail) { writer.Write(true); writer.Write(detail); } else writer.Write(false);
            }
            writer.Write(set.Placed.Count);
            foreach (var placed in set.Placed)
            {
                writer.Write((byte)placed.Trigger); writer.Write(placed.Sector.X); writer.Write(placed.Sector.Y); writer.Write(placed.Tile);
                Put(writer, placed.Location); writer.Write(placed.LocalFlags); writer.Write(placed.Counters); writer.Write(placed.Script);
            }
        }
        return stream.ToArray();
    }

    /// <summary>The set the bytes hold, over <paramref name="map"/>; null when the bytes are not a whole set of this layout version for this map.</summary>
    public static MapTransitionSet? Read(ReadOnlySpan<byte> bytes, WorldMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadByte() != Version || reader.ReadString() != map.Name) return null;
            var isStartMap = reader.ReadBoolean();
            MapListEntry? entry = null;
            if (reader.ReadBoolean())
                entry = new(reader.ReadInt32(), reader.ReadString(), GetOptional(reader)) { Type = (MapListType)reader.ReadInt32(), Area = reader.ReadInt32() };
            var sites = new List<MapTransitionSite>();
            for (var n = Count(reader, stream, 30); n > 0; n--)
            {
                var trigger = (MapTransitionTrigger)reader.ReadByte(); var at = Get(reader);
                MapTransitionObject? owner = null;
                if (reader.ReadBoolean())
                {
                    WorldSectorCoordinate? sector = reader.ReadBoolean() ? new(reader.ReadInt32(), reader.ReadInt32()) : null;
                    var index = reader.ReadInt32(); var own = reader.ReadString(); var type = reader.ReadInt32();
                    owner = new(sector, index, own, type, reader.ReadBoolean() ? reader.ReadInt32() : null);
                }
                var script = reader.ReadInt32();
                var chain = new int[Count(reader, stream, 4)];
                for (var i = 0; i < chain.Length; i++) chain[i] = reader.ReadInt32();
                var line = reader.ReadInt32(); var fade = reader.ReadBoolean(); var oneShot = reader.ReadBoolean();
                var catalogue = reader.ReadInt32(); var runtime = reader.ReadInt32(); var arrival = Get(reader);
                sites.Add(new(trigger, at, owner, script, Array.AsReadOnly(chain), line, fade, oneShot, catalogue, runtime, arrival, (MapTransitionGuard)reader.ReadInt32()));
            }
            var issues = new List<WorldIssue>();
            for (var n = Count(reader, stream, 4); n > 0; n--)
            {
                var code = (WorldIssueCode)reader.ReadByte(); var path = reader.ReadString();
                int? index = reader.ReadBoolean() ? reader.ReadInt32() : null; var detail = reader.ReadBoolean() ? reader.ReadString() : null;
                issues.Add(new(code, path, index, detail));
            }
            var placed = new List<MapPlacedScript>();
            for (var n = Count(reader, stream, 30); n > 0; n--)
                placed.Add(new((MapTransitionTrigger)reader.ReadByte(), new(reader.ReadInt32(), reader.ReadInt32()), reader.ReadInt32(), Get(reader),
                    reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadInt32()));
            // A whole set and nothing after it.
            if (stream.Position != stream.Length) return null;
            return new(map, entry, sites.AsReadOnly(), issues.AsReadOnly(), placed.AsReadOnly()) { IsStartMap = isStartMap };
        }
        catch (Exception error) when (error is EndOfStreamException or IOException or FormatException or ArgumentException or OverflowException or InvalidCastException)
        {
            return null;
        }
    }

    // A count a buffer cannot hold (each element takes at least minBytes) is a refusal before it allocates anything.
    private static int Count(BinaryReader reader, Stream stream, int minBytes)
    {
        var count = reader.ReadInt32();
        if (count is < 0 or > MaxCount || (long)count * minBytes > stream.Length - stream.Position) throw new FormatException("count");
        return count;
    }

    private static void Put(BinaryWriter writer, WorldLocation location) { writer.Write(location.X); writer.Write(location.Y); }

    private static WorldLocation Get(BinaryReader reader) => new(reader.ReadInt64(), reader.ReadInt64());

    private static void PutOptional(BinaryWriter writer, WorldLocation? location)
    {
        if (location is { } at) { writer.Write(true); Put(writer, at); } else writer.Write(false);
    }

    private static WorldLocation? GetOptional(BinaryReader reader) => reader.ReadBoolean() ? Get(reader) : null;
}
