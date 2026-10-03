using System.Buffers.Binary;
using Arcanum.Data;
using Arcanum.Data.Terrain;

namespace Arcanum.CrashSiteProbe;

internal static partial class Program
{
    // Research equivalence only: preserve every byte except transient sparse-array
    // handles and zero capacity padding. Never use this digest as input identity.
    private static string SemanticHash(byte[] bytes, InstanceInfo instance)
    {
        using var stream = new MemoryStream();
        var end = 0;
        Span<byte> count = stackalloc byte[4];
        foreach (var array in instance.Arrays)
        {
            stream.Write(bytes.AsSpan(end, array.Start - end));
            stream.Write(bytes.AsSpan(array.Start, 9)); // presence, element size/count
            stream.Write(new byte[4]); // freshly allocated bitset handle is not semantic
            stream.Write(bytes.AsSpan(array.Start + 13, array.Size * array.Count));
            var words = array.Words;
            while (words > 0 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(array.WordStart + 4 + (words - 1) * 4)) == 0) words--;
            BinaryPrimitives.WriteInt32LittleEndian(count, words); stream.Write(count);
            stream.Write(bytes.AsSpan(array.WordStart + 4, words * 4));
            end = array.End;
        }
        stream.Write(bytes.AsSpan(end));
        return AreaHash(stream.ToArray());
    }

    private sealed record ScriptAttachment(int sap, uint flags, uint counters, int script);

    private static IReadOnlyList<ScriptAttachment> EffectiveScripts(byte[] bytes, InstanceInfo instance, PrototypeInfo prototype) =>
        instance.ScriptsOverridden ? ScriptAttachments(bytes, instance) : prototype.Scripts;

    private static IReadOnlyDictionary<int, int> EffectiveNumeric(InstanceInfo instance, PrototypeInfo prototype) =>
        prototype.NumericFields.Keys.Union(instance.NumericFields.Keys).Order().ToDictionary(k => k, k => instance.NumericFields.TryGetValue(k, out var value) ? value : prototype.NumericFields[k]);

    private static IReadOnlyList<ScriptAttachment> ScriptAttachments(byte[] bytes, InstanceInfo instance) => DecodeScripts(bytes, instance.Arrays);

    private static IReadOnlyList<ScriptAttachment> DecodeScripts(ReadOnlySpan<byte> bytes, IReadOnlyList<ArraySpan> arrays)
    {
        var result = new List<ScriptAttachment>();
        foreach (var array in arrays.Where(a => a.Field == 31))
        {
            var rank = 0;
            for (var word = 0; word < array.Words; word++)
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(array.WordStart + 4 + word * 4));
                for (var bit = 0; bit < 32; bit++)
                {
                    if ((bits & (1u << bit)) == 0) continue;
                    var offset = array.Start + 13 + rank++ * 12;
                    result.Add(new ScriptAttachment(word * 32 + bit, BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset)), BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4)), BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 8))));
                }
            }
        }
        return result.ToArray();
    }

    private static object VisualChain(DatArchive resource, ResolvedInstance value, PrototypeInfo prototype, int worldX, int worldY)
    {
        if (value.Instance.Type != prototype.Type) throw new InvalidDataException("AREA_PROTOTYPE_TYPE");
        var aid = unchecked((uint)value.CurrentAid); var kind = aid >> 28;
        if (kind is not 4 and not 7) return new { status = "unsupported", reason = $"ART_CLASS_{kind}_MAPPING_STATE_COMPOSITION_NOT_ADMITTED" };
        var category = (int)((aid >> 6) & 31); var number = (int)((aid >> 19) & 511);
        var group = (int)((aid >> 11) & 7); var frame = (int)((aid >> 14) & 31); var palette = (int)((aid >> 4) & 3);
        var basePath = kind == 4 ? "art/scenery/" : "art/container/";
        var mesEntry = AreaUnique(resource, basePath + (kind == 4 ? "scenery.mes" : "container.mes"));
        var mapping = ObjectArt.ParseMes(resource.ReadEntry(mesEntry));
        if (!mapping.TryGetValue(category * 1000 + number, out var filename) || !ObjectArt.IsSafeRelativeArtFilename(filename)) throw new InvalidDataException("AREA_ART_MAPPING");
        var entry = AreaUnique(resource, basePath + filename);
        var bytes = resource.ReadEntry(entry); var metadata = ArtDecoder.Inspect(bytes, new ArtDecoderOptions { MaxInputBytes = MaxDecodedSectorBytes });
        if (group >= metadata.Header.PhysicalGroupCount || frame >= metadata.Header.FramesPerGroup)
            return new { status = "unsupported", reason = "ART_PHYSICAL_SELECTION_NOT_ADMITTED", art_class = kind, category, number, resource_ordinal = entry.Ordinal, resource_sha256 = AreaHash(bytes), group, frame, requested_palette = palette };
        var decoded = ArtDecoder.Decode(bytes, new ArtFrameSelection(group, frame, palette), new ArtDecoderOptions { MaxInputBytes = MaxDecodedSectorBytes });
        var geometry = metadata.GetFrame(group, frame);
        var ox = value.Instance.DirectOffsetX ?? prototype.NumericFields[2]; var oy = value.Instance.DirectOffsetY ?? prototype.NumericFields[3];
        // Deliberately retain research diagnostic placement, not original render rectangles.
        var placement = DiagnosticPlacement.For(worldX - 92928L, worldY - 82560L, ox, oy, geometry.HotX, geometry.HotY);
        return new { status = "resolved", art_class = kind, category, number, mapping_ordinal = mesEntry.Ordinal, resource_ordinal = entry.Ordinal, resource_bytes = bytes.Length, resource_sha256 = AreaHash(bytes), group, frame, requested_palette = palette, effective_palette = decoded.EffectivePaletteSlot, geometry.Width, geometry.Height, geometry.HotX, geometry.HotY, geometry.OffsetX, geometry.OffsetY, offset_x = ox, offset_y = oy, offset_x_inherited = !value.Instance.DirectOffsetX.HasValue, offset_y_inherited = !value.Instance.DirectOffsetY.HasValue, rgba_sha256 = AreaHash(decoded.Rgba.ToArray()), rgba_bytes = decoded.Rgba.Count, placement, placement_status = "diagnostic_only_original_visibility_unverified" };
    }

    private static DatEntry AreaUnique(DatArchive archive, string path)
    {
        var matches = archive.Entries.Where(e => AreaName(e).Equals(path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("AREA_RESOURCE_MISSING_OR_AMBIGUOUS");
        return matches[0];
    }

    private static object AreaSectors(DatArchive module, DatArchive resource, string prototypeRoot, string prefix = "maps/Arcanum1-024-fixed/", bool cave = false)
    {
        var rows = new List<object>(); var selected = new List<object>();
        var source = new TerrainResourceSource(resource, "resource2", "art\\tile\\tilename.mes", "art\\facade\\facadename.mes", TerrainPathProfile.CeNonWindowsMacOs);
        var coordinates = new HashSet<long>(); var members = new List<(int X, int Y)>();
        var entries = module.Entries.Where(e => AreaName(e).StartsWith(prefix, StringComparison.Ordinal) && AreaName(e).EndsWith(".sec", StringComparison.Ordinal)).OrderBy(e => e.Ordinal).ToArray();
        if (cave && entries.Length != 1) throw new InvalidDataException("CAVE_SCOPE_CHANGED");
        foreach (var entry in entries)
        {
            var name = AreaName(entry); var id = long.Parse(Path.GetFileNameWithoutExtension(name), System.Globalization.CultureInfo.InvariantCulture);
            if (!coordinates.Add(id)) throw new InvalidDataException("DUPLICATE_SECTOR_COORDINATE");
            var x = checked((int)(id % 67108864)); var y = checked((int)(id / 67108864));
            var bytes = module.ReadEntry(entry); var slice = SectorSlice.Parse(bytes, false); var framing = SectorFraming.Parse(bytes, slice.LightCount);
            int? townmap = framing.MetadataAndSoundBytes == 24 ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(framing.MetadataAndSoundStart)) : null;
            if (townmap == 2) members.Add((x, y));
            rows.Add(new { id, x, y, ordinal = entry.Ordinal, bytes = bytes.Length, sha256 = AreaHash(bytes), townmap, framing.Placeholder });
            var core = cave || townmap == 2;
            if (!core && !(x >= 1450 && x <= 1454 && y >= 1288 && y <= 1292)) continue;
            var instances = ObjectWire.ParseWindow(bytes.AsSpan(framing.ObjectStart, framing.ObjectBytes), slice.TrailingInt32);
            var prototypes = ObjectWire.ReadReferencedPrototypesFromDirectory(prototypeRoot, instances.Select(v => v.PrototypeId).ToArray());
            var effective = ObjectWire.ResolveEffectiveInstances(instances, prototypes.Prototypes);
            var statics = new List<object>();
            foreach (var value in effective)
            {
                var proto = prototypes.Prototypes.Single(p => p.Id.Matches(value.Instance.PrototypeId));
                var loc = value.Instance.Location; int? wx = null, wy = null;
                if (loc is not null)
                {
                    // Sector-object caller retains the tile bits and rebases by its
                    // containing sector, even when persisted high bits are nonzero.
                    (wx, wy) = RebaseSectorLocation(loc.Value, x, y);
                }
                object visual = new { status = "unresolved", reason = "LOCATION_ABSENT" };
                if (core && wx.HasValue) visual = VisualChain(resource, value, proto, wx.Value, wy!.Value);
                statics.Add(new { value.SourceIndex, type = value.Instance.Type, prototype = value.Instance.PrototypeId.AidValue(), world_x = wx, world_y = wy, current_aid = value.CurrentAid, value.CurrentAidInherited, value.EffectiveFlags, numeric = value.Instance.NumericFields, effective_numeric = EffectiveNumeric(value.Instance, proto), scripts_inherited = !value.Instance.ScriptsOverridden, scripts = EffectiveScripts(bytes.AsSpan(framing.ObjectStart).ToArray(), value.Instance, proto), visual });
            }
            var selector = new TerrainSectorSelector(System.Text.Encoding.ASCII.GetString(entry.RawNameBytes.ToArray()[..^1]), "area-research", x, y, id, checked(x * 64), checked(y * 64), 64, 64, new TerrainEntryExpectation(entry.Ordinal, entry.Flags, entry.DeclaredOutputSize));
            var terrain = TerrainLoader.Inspect(module, selector, source);
            var attachments = new List<object>();
            for (var index = 0; index < framing.TileScriptCount; index++)
            {
                var pos = framing.TileScriptsStart + 4 + index * 24;
                attachments.Add(new { index, words = Enumerable.Range(0, 6).Select(i => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + i * 4))).ToArray() });
            }
            var sectorScript = framing.SectorScriptBytes == 12 ? new ScriptAttachment(-1, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(framing.SectorScriptStart)), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(framing.SectorScriptStart + 4)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(framing.SectorScriptStart + 8))) : null;
            selected.Add(new { id, x, y, ordinal = entry.Ordinal, core, framing, lights = slice.LightCount, roofs = framing.RoofEmpty == 0 ? 256 : 0, tile_classes = slice.ArtTypeCounts, terrain_resolved = terrain.ResolvedRecords.Count, terrain_issues = terrain.Issues.GroupBy(i => i.Code).ToDictionary(g => g.Key.ToString(), g => g.Count()), terrain_unique_entries = terrain.ResolvedRecords.Select(v => v.ResourceIdentity.DatOrdinal).Distinct().Order().ToArray(), static_count = instances.Count, statics, attachments, sector_script = sectorScript, prototypes = prototypes.Inputs.Select(p => p.Fingerprint).ToArray() });
        }
        if (!cave && (rows.Count != 621 || members.Count != 9 || members.Any(p => p.X < 1451 || p.X > 1453 || p.Y < 1289 || p.Y > 1291))) throw new InvalidDataException("AREA_MEMBERSHIP_CHANGED");
        return new { survey = rows, selected, missing_context = Enumerable.Range(1288, 5).SelectMany(y => Enumerable.Range(1450, 5).Select(x => (x, y))).Where(p => !coordinates.Contains(p.x + p.y * 67108864L)).Select(p => new { x = p.x, y = p.y }).ToArray() };
    }
}
