using System.Buffers.Binary;
using Arcanum.Data;

namespace Arcanum.CrashSiteProbe;

internal static partial class Program
{
    private static object AreaCave(DatArchive module, DatArchive resource, string prototypeRoot)
    {
        var catalogue = ObjectArt.ParseMes(module.ReadEntry(AreaUnique(module, "rules/maplist.mes")));
        if (!Enumerable.Range(5000, 79).All(catalogue.ContainsKey)) throw new InvalidDataException("CAVE_CATALOGUE_SEQUENCE");
        var map = catalogue[5078].Split(',')[0].Trim();
        if (map.Length == 0 || map.Contains("..", StringComparison.Ordinal) || map.Any(c => c is '/' or '\\' or '\0')) throw new InvalidDataException("CAVE_UNSAFE_SELECTOR");
        var prefix = "maps/" + map + "/";
        var jumpEntry = AreaUnique(module, prefix + "map.jmp"); var jump = module.ReadEntry(jumpEntry);
        if (jump.Length != 4 || BinaryPrimitives.ReadInt32LittleEndian(jump) != 0) throw new InvalidDataException("CAVE_JUMP_CHANGED");
        var mobiles = new List<object>();
        var entries = module.Entries.Where(e => AreaName(e).StartsWith(prefix, StringComparison.Ordinal) && AreaName(e).EndsWith(".mob", StringComparison.Ordinal)).OrderBy(e => e.Ordinal).ToArray();
        if (entries.Length != 39) throw new InvalidDataException("CAVE_MOBILE_SCOPE_CHANGED");
        foreach (var entry in entries)
        {
            var bytes = module.ReadEntry(entry);
            var instance = ObjectWire.ParseNext(bytes, out var consumed);
            if (consumed != bytes.Length) throw new InvalidDataException("CAVE_LOOSE_TRAILING_BYTES");
            var prototypes = ObjectWire.ReadReferencedPrototypesFromDirectory(prototypeRoot, [instance.PrototypeId]);
            var prototype = prototypes.Prototypes.Single();
            var value = ObjectWire.ResolveEffectiveInstances([instance], prototypes.Prototypes).Single();
            int? x = instance.Location.HasValue ? unchecked((int)instance.Location.Value) : null;
            int? y = instance.Location.HasValue ? unchecked((int)(instance.Location.Value >> 32)) : null;
            object visual = x.HasValue && y.HasValue ? VisualChain(resource, value, prototype, x.Value, y.Value) : new { status = "unresolved", reason = "LOCATION_ABSENT" };
            mobiles.Add(new { ordinal = entry.Ordinal, bytes = bytes.Length, sha256 = AreaHash(bytes), type = instance.Type, prototype = instance.PrototypeId.AidValue(), world_x = x, world_y = y, current_aid = value.CurrentAid, value.CurrentAidInherited, value.EffectiveFlags, effective_numeric = EffectiveNumeric(instance, prototype), scripts_inherited = !instance.ScriptsOverridden, scripts = EffectiveScripts(bytes, instance, prototype), visual, prototypes = prototypes.Inputs.Select(p => p.Fingerprint).ToArray() });
        }
        return new { catalogue = 5078, runtime = 79, sectors = AreaSectors(module, resource, prototypeRoot, prefix, true), mobiles, mobile_source_status = "editor_loose_observed_game_aggregate_not_verified", jump_ordinal = jumpEntry.Ordinal, jump_sha256 = AreaHash(jump), jump_count = 0 };
    }
}
