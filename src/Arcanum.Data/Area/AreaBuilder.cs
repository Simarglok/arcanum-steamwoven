using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Arcanum.Data.Maps;
using Arcanum.Data.Objects;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Area;

public sealed partial class AreaDataOwner
{
    private AreaInspection Build(CancellationToken token, AreaOwnership output)
    {
        _budget.BeginPhase(AreaBudgetPhase.I1);
        using var construction = new AreaOwnership(_budget);
        // Candidate array, survey nodes, BFS/ring sets and bounded diagnostics.
        // The module entry count bounds even a malformed selector population.
        construction.Allocate(checked(65536 + Archive(new(AreaSourceRole.Module)).Entries.Count * 16L + _limits.MaxSurvey * 2048L));
        var issues = new List<AreaIssue>();
        var moduleKey = new AreaInputKey(AreaSourceRole.Module); var module = Archive(moduleKey);
        var candidates = module.Entries.Where(e => Name(e).StartsWith(_plan.SectorPrefix, StringComparison.Ordinal) && Name(e).EndsWith(".sec", StringComparison.Ordinal)).ToArray();
        // ARC-DATA-032: a window surveys only its own sector files, which the module archive must hold.
        var window = _plan.Window?.Sectors.ToDictionary(s => s.Coordinate.Packed);
        if (window is not null)
        {
            var own = window.Values.Where(s => s.Template is null).Select(s => _plan.SectorPrefix + s.Coordinate.Packed.ToString(CultureInfo.InvariantCulture) + ".sec").ToHashSet(StringComparer.Ordinal);
            candidates = candidates.Where(e => own.Contains(Name(e))).ToArray();
            if (candidates.Select(Name).Distinct(StringComparer.Ordinal).Count() != candidates.Length) throw Fail(AreaErrorCode.DuplicateIdentity, AreaStage.Selection);
            if (candidates.Length != own.Count) throw Fail(AreaErrorCode.MissingSource, AreaStage.Selection);
        }
        if (candidates.Length > _limits.MaxSurvey) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Selection);

        var survey = new SortedDictionary<long, SurveyRow>();
        foreach (var entry in candidates)
        {
            token.ThrowIfCancellationRequested();
            var name = Name(entry); var tail = name[_plan.SectorPrefix.Length..];
            if (tail.Contains('/') || !long.TryParse(tail[..^4], NumberStyles.None, CultureInfo.InvariantCulture, out var packed))
                throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Selection);
            var coordinate = AreaSectorCoordinate.Unpack(packed);
            using var payload = new AreaOwnership(_budget);
            var bytes = ReadEntry(EntryIdentity(moduleKey, entry), token, payload);
            payload.Allocate(checked(bytes.Length * 2L + 32768));
            var framing = ParseSector(bytes);
            var metadata = framing.Framing[TerrainSection.MetadataAndSound];
            var townmap = metadata.Length == 0 ? (int?)null : BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(metadata.Offset));
            if (!survey.TryAdd(packed, new(coordinate, entry, townmap))) throw Fail(AreaErrorCode.DuplicateIdentity, AreaStage.Selection);
        }
        var core = new HashSet<long>(); var queue = new Queue<AreaSectorCoordinate>(); queue.Enqueue(_plan.Start);
        if (_plan.Selection == AreaSectorSelection.AllSectors) { core.UnionWith(survey.Keys); queue.Clear(); }
        if (window is not null) { core.UnionWith(window.Values.Where(s => s.Scope == AreaScope.Core).Select(s => s.Coordinate.Packed)); queue.Clear(); }
        while (queue.TryDequeue(out var coordinate))
        {
            token.ThrowIfCancellationRequested();
            if (core.Contains(coordinate.Packed) || !survey.TryGetValue(coordinate.Packed, out var row) || row.Townmap != _plan.Townmap) continue;
            core.Add(coordinate.Packed);
            foreach (var next in Neighbors(coordinate, diagonal: false)) queue.Enqueue(next);
        }
        if (core.Count == 0) throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Selection);
        // A whole map has no context ring: its sectors without files are the map's own edge.
        var ring = _plan.Selection == AreaSectorSelection.AllSectors ? new HashSet<long>()
            : window is not null ? window.Values.Where(s => s.Scope == AreaScope.Context).Select(s => s.Coordinate.Packed).ToHashSet()
            : core.SelectMany(id => Neighbors(AreaSectorCoordinate.Unpack(id), diagonal: true)).Select(c => c.Packed).Where(id => !core.Contains(id)).ToHashSet();
        if (core.Count + ring.Count > checked(_limits.MaxPresentSectors + 16)) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Selection);
        var present = core.Concat(ring).Where(id => survey.ContainsKey(id) || window?.GetValueOrDefault(id)?.Template is not null).Order().ToArray();
        if (present.Length > _limits.MaxPresentSectors) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Selection);
        _budget.BeginPhase(AreaBudgetPhase.I2);
        // Admit the shared finite wire-schema/hash initialization before the
        // first prototype can trigger it. Per-record reservations describe the
        // warm graph and must not silently rely on another owner's warm-up.
        _budget.Reserve(65536);
        var mappings = ReadMappings(token, construction);
        TerrainLoader.TileNames tiles; TerrainLoader.FacadeNames facades;
        try
        {
            tiles = TerrainLoader.TileNames.Parse(mappings[AreaMappingRole.Tile], originalNames: true);
            facades = TerrainLoader.FacadeNames.Parse(mappings[AreaMappingRole.Facade]);
            _objectTiles = tiles;
        }
        catch (TerrainException) { throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping); }
        var sectors = new List<AreaSectorRecord>(); var terrain = new List<AreaTerrainRecord>(); var objects = new List<AreaObjectRecord>();
        var roofs = new List<AreaRoofRecord>();
        // ARC-RENDER-021: roof names by position in roofname.mes, as the original indexes them (entry 0 must exist).
        IReadOnlyList<string>? roofNames = RoofsAdmitted && mappings.TryGetValue(AreaMappingRole.Roof, out var roofTable)
            ? roofTable.ContainsKey(0) ? roofTable.Values.ToArray() : throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Mapping) : null;
        var visuals = new List<AreaVisualRecord>(); var terrainSelections = new Dictionary<(AreaEntryIdentity, int, int, int), AreaVisualKey>();
        var terrainEntries = new Dictionary<uint, AreaEntryIdentity>();
        var definitions = ReadPrototypes(token, construction);
        var prototypes = new Dictionary<int, ObjectWireRecord>();
        var instanceIds = new InstanceIndex(construction);
        _budget.BeginPhase(AreaBudgetPhase.I3);
        foreach (var packed in present)
        {
            token.ThrowIfCancellationRequested(); var scope = core.Contains(packed) ? AreaScope.Core : AreaScope.Context;
            // ARC-DATA-032: a generated window sector is its template file read like a sector file (ledger SS5, SS6).
            var template = window?.GetValueOrDefault(packed)?.Template;
            var row = template is null ? survey[packed] : null; var coordinate = AreaSectorCoordinate.Unpack(packed);
            var sourceKey = template ?? moduleKey;
            using var payload = new AreaOwnership(_budget);
            var bytes = template is { } templateKey ? ReadFile(templateKey, token, payload) : ReadEntry(EntryIdentity(moduleKey, row!.Entry), token, payload);
            payload.Allocate(checked(bytes.Length * 2L + 32768));
            var parsed = ParseSector(bytes); var framing = parsed.Framing;
            var sectorKey = new AreaSectorKey(Identity(sourceKey).Sha256, _plan.RuntimeMap, packed);
            int? townmap = row?.Townmap;
            if (template is not null && framing[TerrainSection.MetadataAndSound] is { Length: > 0 } templateMetadata)
                townmap = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(templateMetadata.Offset));
            output.Allocate(checked(4096L * 384 + 8192));
            output.Allocate(checked(framing.LightCount * 128L));
            var lights = new List<IReadOnlyList<int>>();
            var lightRange = framing[TerrainSection.Lights];
            for (var l = 0; l < framing.LightCount; l++)
            {
                var words = new int[12]; for (var w = 0; w < 12; w++) words[w] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(lightRange.Offset + 48 * l + 4 * w));
                // A template light moves by the object rule (ledger Wilderness A1): its location is words 2 and 3.
                lights.Add(template is null ? Array.AsReadOnly(words) : WorldCatalog.RelocateLight(words, new(checked((int)coordinate.X), checked((int)coordinate.Y))));
            }
            sectors.Add(new(sectorKey, coordinate, scope, true, row?.Entry.Ordinal, townmap, framing.LightCount, framing.RoofDiscriminator, framing.Placeholder,
                framing.ObjectWindow.TerminalObjectCount, Array.AsReadOnly(framing.Ranges.OrderBy(p => p.Key).Select(p => new AreaByteRange(p.Value.Offset, p.Value.Length)).ToArray()), lights.AsReadOnly(),
                row is null ? null : EntryIdentity(moduleKey, row.Entry), ObjectWireReader.Hash(bytes), template)
                { AptitudeAdjustment = framing[TerrainSection.MetadataAndSound] is { Length: >= 8 } meta ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(meta.Offset + 4)) : 0 });
            // A roof list stays in the sector record (RoofDiscriminator); its roofs are read below when the plan binds the
            // two roof tables (ARC-RENDER-021).
            for (var ordinal = 0; ordinal < 4096; ordinal++)
            {
                var raw = parsed.Words[ordinal]; var lookup = raw >> 28 == 0 ? raw & ~63U : raw;
                var key = new AreaVisualKey(AreaSourceKind.Terrain, sectorKey, ordinal, string.Empty);
                try
                {
                    int group = 0, frame = 0, palette;
                    if (raw >> 28 == 0) palette = (int)((raw >> 4) & 3);
                    else if (raw >> 28 == 11) { frame = (int)((raw >> 1) & 1023); palette = 0; }
                    else throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance, index: ordinal);
                    if (!terrainEntries.TryGetValue(lookup, out var entry))
                    {
                        // Locator-only build-local index. No entry, encoded ART
                        // or pixel payload is cached. Charge selector strings and
                        // index capacity before resolving the first occurrence.
                        var role = raw >> 28 == 0 ? AreaMappingRole.Tile : AreaMappingRole.Facade;
                        construction.Allocate(checked(4096 + mappings[role].Values.Max(v => v.Length) * 16L));
                        var path = raw >> 28 == 0 ? TerrainLoader.TilePath(tiles, lookup).Path : "art/facade/" + Safe(facades.Name(TerrainLoader.FacadeNumber(raw))) + ".art";
                        entry = Resolve(path);
                        terrainEntries.Add(lookup, entry);
                    }
                    var tuple = (entry, group, frame, palette);
                    if (!terrainSelections.TryGetValue(tuple, out var selected))
                    {
                        selected = key;
                        AdmitVisual(visuals);
                        output.Allocate(2048);
                        AddVisual(visuals, MakeVisual(key, raw, lookup, entry, group, group, frame, palette, false, 0, 0, 100, null, token));
                        terrainSelections.Add(tuple, selected);
                    }
                    terrain.Add(new(key, ordinal & 63, ordinal >> 6, checked(coordinate.OriginX + (ordinal & 63)), checked(coordinate.OriginY + (ordinal >> 6)), raw, lookup, scope, selected));
                }
                catch (AreaException error) { _budget.EndFrame(); issues.Add(error.Issue with { SourceIndex = ordinal }); }
                catch (TerrainException) { issues.Add(new(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Mapping, SourceIndex: ordinal)); }
            }
            if (roofNames is not null && scope == AreaScope.Core && framing.RoofDiscriminator == 0)
                AddRoofs(bytes, framing[TerrainSection.Roof], sectorKey, coordinate, roofNames, visuals, roofs, issues, output, token);
            var start = framing.ObjectWindow.Range.Offset; var end = framing.ObjectWindow.Range.End;
            for (var ordinal = 0; ordinal < framing.ObjectWindow.TerminalObjectCount; ordinal++)
            {
                using var wire = new AreaOwnership(_budget);
                var record = ReadWire(bytes.AsSpan(start, end - start), AreaStage.Sector, start, ordinal, ownership: wire);
                instanceIds.Register(record, sourceKey, packed, start, AreaStage.Sector, ordinal, allowNull: true);
                AddObject(record, sourceKey, packed, ordinal, start, scope, coordinate, definitions, prototypes, objects, visuals, mappings, issues, token, output);
                start = checked(start + record.Length);
            }
            if (start != end) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Sector, start, framing.ObjectWindow.TerminalObjectCount);
        }
        foreach (var packed in ring.Except(present).Order())
        {
            var coordinate = AreaSectorCoordinate.Unpack(packed);
            sectors.Add(new(new(Identity(moduleKey).Sha256, _plan.RuntimeMap, packed), coordinate, AreaScope.Context, false, null, null, 0, 1, 0, 0, [], [], null, null));
        }
        _budget.BeginPhase(AreaBudgetPhase.I4);
        var partitions = new int[4]; var aggregate = new Dictionary<string, Correspondence>(StringComparer.Ordinal);
        // ARC-DATA-028: the original applies map.patch to the map's mobile objects as they are loaded, before any sector.
        MapPatchFile? patch = null;
        if (_plan.MapPatch is not null)
        {
            using var patchPayload = new AreaOwnership(_budget);
            try { patch = MapPatchFile.Parse(ReadFile(new(AreaSourceRole.MapPatch), token, patchPayload)); }
            catch (ArgumentOutOfRangeException) { throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Aggregate); }
            // The binding was made from this file: the same unmodelled state, and a header decision for every script it names.
            if (patch.HasUnmodelledLines != _plan.MapPatch.HasUnmodelledLines || patch.ScriptNumbers().Any(n => !_plan.MapPatch.ScriptHeaders.ContainsKey(n)))
                throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Aggregate);
        }
        if (_streams.ContainsKey(new(AreaSourceRole.NormalGameMobile)))
        {
            using var payload = new AreaOwnership(_budget);
            var source = new AreaInputKey(AreaSourceRole.NormalGameMobile); var bytes = ReadFile(source, token, payload);
            if (bytes.Length < 16) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Aggregate);
            var offset = 16; var index = 0;
            while (offset < bytes.Length)
            {
                token.ThrowIfCancellationRequested();
                if (index >= _limits.MaxAggregateRecords) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Aggregate, offset, index);
                using var wire = new AreaOwnership(_budget);
                var record = ReadWire(bytes.AsSpan(offset), AreaStage.Aggregate, offset, index, ownership: wire);
                // Register the complete aggregate once, BEFORE location slicing.
                instanceIds.Register(record, source, null, offset, AreaStage.Aggregate, index, allowNull: false);
                construction.Allocate(512);
                if (!aggregate.TryAdd(record.Own.Sha256, new(record.Type, record.Parent.Number, record.SemanticSha256))) throw Fail(AreaErrorCode.DuplicateIdentity, AreaStage.Aggregate, offset, index);
                var location = record.Fields.TryGetValue(1, out var locationField) ? locationField.Number : null;
                var x = location.HasValue ? (int?)unchecked((int)location.Value) : null;
                var y = location.HasValue ? (int?)unchecked((int)(location.Value >> 32)) : null;
                var scope = x is null or < 0 || y is null or < 0 ? AreaScope.Unknown : core.Contains(new AreaSectorCoordinate(x.Value / 64, y.Value / 64).Packed) ? AreaScope.Core : ring.Contains(new AreaSectorCoordinate(x.Value / 64, y.Value / 64).Packed) ? AreaScope.Context : AreaScope.Outside;
                partitions[(int)scope]++;
                if (scope is AreaScope.Core or AreaScope.Context)
                    AddObject(record, source, null, index, offset, scope, null, definitions, prototypes, objects, visuals, mappings, issues, token, output,
                        patch is not null && record.Own.Entity is { Kind: 2 } entity ? patch.For(MapPatchFile.ObjectGuid(entity)) : null);
                else if (scope == AreaScope.Unknown) issues.Add(new(AreaErrorCode.UnknownLocation, AreaStage.Aggregate, offset, index));
                offset = checked(offset + record.Length); index++;
            }
        }
        _budget.BeginPhase(AreaBudgetPhase.I5);
        construction.Allocate(checked(65536 + module.Entries.Count * 16L));
        var editorIds = new HashSet<string>(StringComparer.Ordinal);
        var editor = module.Entries.Where(e => Name(e).StartsWith(_plan.SectorPrefix, StringComparison.Ordinal) && Name(e).EndsWith(".mob", StringComparison.Ordinal)).ToArray();
        if (editor.Length > _limits.MaxAggregateRecords) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Correspondence);
        foreach (var entry in editor)
        {
            token.ThrowIfCancellationRequested();
            using var payload = new AreaOwnership(_budget);
            var bytes = ReadEntry(EntryIdentity(moduleKey, entry), token, payload);
            using var wire = new AreaOwnership(_budget);
            var record = ReadWire(bytes, AreaStage.Correspondence, 0, entry.Ordinal, ownership: wire);
            construction.Allocate(512);
            if (record.Own.Kind == 0) throw Fail(AreaErrorCode.UnsupportedVersionOrSchema, AreaStage.Correspondence, 0, entry.Ordinal);
            if (record.Length != bytes.Length || !editorIds.Add(record.Own.Sha256) || !aggregate.TryGetValue(record.Own.Sha256, out var other) || other != new Correspondence(record.Type, record.Parent.Number, record.SemanticSha256))
                throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Correspondence, 0, entry.Ordinal);
        }
        if (editor.Length != aggregate.Count) issues.Add(new(AreaErrorCode.InputIdentityMismatch, AreaStage.Correspondence));
        _budget.BeginPhase(AreaBudgetPhase.I6);
        var reconciliation = new AreaReconciliation(survey.Count, core.Count, present.Length - core.Count, ring.Count - (present.Length - core.Count),
            terrain.Count(t => t.Scope == AreaScope.Core), terrain.Count(t => t.Scope == AreaScope.Context), objects.Count(o => o.Key.Kind == AreaSourceKind.Static && o.Scope == AreaScope.Core),
            aggregate.Count, partitions[0], partitions[1], partitions[2], partitions[3], editorIds.Count, prototypes.Count, visuals.Count);
        // ARC-DATA-036: a pinned profile's expected outputs, checked on what the same rules built.
        if (_plan.OriginalMap == OriginalAreaMap.CrashSiteExterior) OriginalProfile.Check(reconciliation, sectors, objects, visuals);
        else if (_plan.OriginalMap == OriginalAreaMap.CrashSiteCave) CaveProfile.Check(reconciliation, sectors, objects, visuals);
        // The plan's endpoints, each a present static core object with its own id, prototype and use script.
        if (_plan.Endpoints.Count > 0)
        {
            construction.Allocate(checked(4096 + 64L * objects.Count)); // the check's index of the core statics
            if (!AreaEndpoints.Check(_plan.Endpoints, objects)) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Appearance);
        }
        // Only the actual new immutable collection copies/sort/endpoints remain
        // here; object/visual graphs were admitted before their construction.
        output.Allocate(checked(8192 + 32L * sectors.Count + 8L * (_plan.Identities.Count + terrain.Count + objects.Count + visuals.Count)));
        var snapshot = new AreaSnapshot(_identity, _generation + 1, _plan, sectors.OrderBy(s => s.Key.Packed), terrain, objects, visuals, reconciliation, _plan.Endpoints,
            roofs, roofs.Count > 0 ? RoofShades(mappings[AreaMappingRole.RoofShade]) : null);
        return new(snapshot, issues, _budget.Usage);
    }

    private void AddObject(ObjectWireRecord record, AreaInputKey input, long? packed, int ordinal, int offset, AreaScope scope, AreaSectorCoordinate? sector,
        Dictionary<int, ObjectWireRecord> definitions, Dictionary<int, ObjectWireRecord> prototypes, List<AreaObjectRecord> objects, List<AreaVisualRecord> visuals,
        Dictionary<AreaMappingRole, SortedDictionary<int, string>> maps, List<AreaIssue> issues, CancellationToken token, AreaOwnership output,
        IReadOnlyList<MapPatchLine>? patchLines = null)
    {
        if (objects.Count >= _limits.MaxObjects) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Appearance, offset, ordinal);

        if (record.Parent.Kind != 1 || record.Parent.Number is not int number) throw Fail(AreaErrorCode.MissingSource, AreaStage.Prototype, offset, ordinal);
        if (!prototypes.TryGetValue(number, out var prototype))
        {
            if (prototypes.Count >= _limits.MaxPrototypes) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Prototype);
            if (!definitions.TryGetValue(number, out prototype)) throw Fail(AreaErrorCode.MissingSource, AreaStage.Prototype, offset, ordinal);
            prototypes.Add(number, prototype);
        }
        if (prototype.Type != record.Type) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Prototype, offset, ordinal);
        // The patched object keeps its raw identity (RawSha256, SemanticSha256); only its fields change.
        MapPatchApplication? patched = null;
        if (patchLines is { Count: > 0 })
        {
            var result = MapPatchApplier.Apply(record, prototype, patchLines, number => _plan.MapPatch!.ScriptHeaders.GetValueOrDefault(number), _plan.MapPatch!.ScriptZeroClears);
            record = result.Record; patched = result.Application;
        }
        using var temporary = new AreaOwnership(_budget);
        temporary.Allocate(checked(4096 + 160L * (prototype.Fields.Count + record.Fields.Count) + maps.Values.SelectMany(m => m.Values).Max(v => v.Length) * 16L));
        var effective = new SortedDictionary<int, ObjectWireValue>(prototype.Fields.ToDictionary(p => p.Key, p => p.Value));
        foreach (var pair in record.Fields) effective[pair.Key] = pair.Value;
        long? directLocation = record.Fields.TryGetValue(1, out var direct) ? direct.Number : null;
        long? x = null, y = null;
        if (directLocation.HasValue)
        {
            if (sector.HasValue) { x = checked(sector.Value.OriginX + (directLocation.Value & 63)); y = checked(sector.Value.OriginY + ((directLocation.Value >> 32) & 63)); }
            else { x = unchecked((int)directLocation.Value); y = unchecked((int)(directLocation.Value >> 32)); if (x < 0 || y < 0) x = y = null; }
        }
        var kind = sector.HasValue ? AreaSourceKind.Static : AreaSourceKind.Mobile;
        var key = new AreaObjectKey(Identity(input).Sha256, _plan.RuntimeMap, kind, packed, ordinal, offset, record.Own.Sha256, record.Length);
        var disposition = scope == AreaScope.Context ? AreaDisposition.ContextOnly : AreaDisposition.MissingMandatoryFact;
        var ready = true; var layersReady = true;
        try
        {
            var flags = unchecked((uint)Number(effective, 18));
            disposition = scope == AreaScope.Context ? AreaDisposition.ContextOnly : (flags & 4096) != 0 ? AreaDisposition.ExteriorSectorInsertionRefusedByInventory :
                (flags & 0x103) != 0 ? AreaDisposition.BodySuppressedBySerializedState : AreaDisposition.BodyEligibleConditionally;
            if (!x.HasValue || !y.HasValue) throw Fail(AreaErrorCode.UnknownLocation, AreaStage.Appearance, offset, ordinal);
            // No body its serialized state hides at T0 is resolved (destroyed 0x1, off 0x2 or
            // don't-draw 0x100; every trap is don't-draw), whatever its type: consumers draw no
            // such body. The original still draws a don't-draw object's overlays, so their
            // admission check below still applies unless the object is destroyed or off.
            var hidden = disposition == AreaDisposition.BodySuppressedBySerializedState;
            if (scope == AreaScope.Core)
            {
                if (!hidden)
                {
                    // Walls (type 0) only where the plan binds both wall-name tables, doors and windows
                    // (type 1) only where it binds the portal names. Scrolls, keys and key rings (types
                    // 10-12) are drawn too: their ground ART is the same item class as every admitted item type.
                    if (record.Type is not 2 and not 3 and not 5 and not 6 and not 7 and not 8 and not 9 and not 10 and not 11 and not 12 and not 13 and not 14 and not 16 &&
                        !(record.Type == 0 && WallsAdmitted) && !(record.Type == 1 && PortalsAdmitted))
                        throw Fail(AreaErrorCode.UnsupportedVersionOrSchema, AreaStage.Appearance, offset, ordinal);
                    var aid = unchecked((uint)Number(effective, 0)); var ox = checked((int)Number(effective, 2)); var oy = checked((int)Number(effective, 3)); var scale = checked((int)Number(effective, 11));
                    // ARC-DATA-038: an ART id that names no ART has no body to draw; the object stays (its step evidence
                    // does not read ART). Walls and portals keep the refusal.
                    if (!HasNoBody(record.Type, aid))
                    {
                        var visualSector = sector ?? new AreaSectorCoordinate(x.Value / 64, y.Value / 64);
                        var vkey = new AreaVisualKey(kind, new(Identity(input).Sha256, _plan.RuntimeMap, visualSector.Packed), ordinal, record.Own.Sha256, Object: key);
                        AdmitVisual(visuals);
                        var request = ObjectRequest(aid, maps, token);
                        output.Allocate(2048);
                        var body = MakeVisual(vkey, aid, request.Lookup, request.Entry, request.Nominal, request.Group, request.Frame, request.Palette, request.Mirror, ox, oy, scale, record, token, request.Policy);
                        AddVisual(visuals, body);
                        // ARC-WORLD-051: every other frame of a door or window's ART, which its open and close steps select at
                        // runtime (ledger LK10). A portal ART has one group; its frame is ART bits 14..18.
                        // ARC-RENDER-035: on a whole map, every other frame of a scenery ART its animation loop plays (ledger
                        // AS1): scenery without scenery flag 0x1 (no auto-animation). Its frame is ART bits 14..18 too.
                        if ((record.Type == 1 || record.Type == 3 && AnimatedSceneryAdmitted && (Optional(effective, SceneryFlagsSlot) & 0x1) == 0)
                            && body.FrameCount is > 1 and <= 32)
                            for (var other = 0; other < body.FrameCount; other++)
                            {
                                if (other == request.Frame) continue;
                                var framed = (aid & ~(31U << 14)) | ((uint)other << 14);
                                AdmitVisual(visuals); output.Allocate(2048);
                                AddVisual(visuals, MakeVisual(vkey with { Layer = record.Type == 1 ? AreaLayerKind.PortalFrame : AreaLayerKind.SceneryFrame, SparseIndex = other }, framed, framed, request.Entry,
                                    request.Nominal, request.Group, other, request.Palette, request.Mirror, ox, oy, scale, record, token, request.Policy));
                            }
                    }
                }
                if (!hidden || (flags & 0x3) == 0)
                    foreach (var (slot, bit, size) in new[] { (5, 0x8000U, 7), (6, 0x8000U, 7), (7, 0x10000U, 4) })
                    {
                        if (!effective.TryGetValue(slot, out var array)) throw Fail(AreaErrorCode.UnsupportedVersionOrSchema, AreaStage.Appearance, offset, ordinal);
                        if ((flags & bit) == 0) continue;
                        for (var index = 0; index < size; index++)
                        {
                            var element = array.Elements.SingleOrDefault(e => e.Key == index);
                            var layerAid = element is null ? 0U : checked((uint)element.Words[0]);
                            if (layerAid == uint.MaxValue) continue;
                            // No eye-candy source role has been admitted by this profile.
                            // Active required references cannot be renderer-Unknown success.
                            layersReady = false;
                            throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance, offset, ordinal);
                        }
                    }
            }
        }
        catch (AreaException error) { _budget.EndFrame(); ready = false; issues.Add(error.Issue with { SourceIndex = ordinal }); }
        output.Allocate(4096); // object/key/list capacity and escaped record/ID hashes
        var directFields = CopyFields(record.Fields, record, direct: true, output);
        var effectiveFields = CopyFields(effective, record, direct: false, output);
        objects.Add(new(key, 119, record.Type, number, record.Parent.Sha256, prototype.Own.Sha256, record.Sha256, record.SemanticSha256, scope,
            directLocation, x, y, disposition, directFields, effectiveFields, ready, x.HasValue && y.HasValue, layersReady, patched));
    }
    private IReadOnlyList<AreaFieldValue> CopyFields(IReadOnlyDictionary<int, ObjectWireValue> fields, ObjectWireRecord instance, bool direct, AreaOwnership output)
    {
        var elements = fields.Values.Sum(v => v.Elements.Count * 160L);
        // 128/field covers field/wrapper/reference copies. Held 320 also
        // includes an existing escaped fingerprint, without charging its
        // allocation twice. Numeric words are independently copied below.
        output.Allocate(checked(4096 + fields.Count * 128L + elements), checked(4096 + fields.Count * 320L + elements));
        return Array.AsReadOnly(fields.Select(p => new AreaFieldValue(p.Key, (AreaPresence)((int)p.Value.Presence + 1),
            direct || instance.Fields.ContainsKey(p.Key) ? AreaValueOrigin.Instance : AreaValueOrigin.Prototype, p.Value.Number,
            Array.AsReadOnly(p.Value.Elements.Select(e => new AreaSparseElement(e.Key, Array.AsReadOnly(e.Words.ToArray()))).ToArray()), p.Value.Fingerprint)).ToArray());
    }
    private ObjectWireRecord ReadWire(ReadOnlySpan<byte> bytes, AreaStage stage, int offset, int index, bool prototype = false, AreaOwnership? ownership = null)
    {
        _budget.Context = new(AreaBudgetCallsite.Wire, _budget.Context.Source, _budget.Context.Ordinal, offset, index);
        try { return ObjectWireReader.Read(bytes, WireLimits, prototype, ownership is null ? _budget.Reserve : ownership.Allocate, maxRecordBytes: 1048576); }
        catch (StaticObjectException error) { throw Fail(error.Code == StaticObjectErrorCode.UnsupportedVersion || error.Code == StaticObjectErrorCode.UnsupportedType ? AreaErrorCode.UnsupportedVersionOrSchema : error.Code == StaticObjectErrorCode.LimitExceeded ? AreaErrorCode.BudgetExceeded : AreaErrorCode.MalformedRecord, stage, offset, index); }
        catch (OverflowException) { throw Fail(AreaErrorCode.MalformedRecord, stage, offset, index); }
    }
    private static TerrainLoader.ParsedSector ParseSector(byte[] bytes)
    {
        try { return TerrainLoader.ParseSector(bytes, new TerrainLoaderOptions()); }
        catch (TerrainException error) { throw Fail(error is TerrainLimitExceededException ? AreaErrorCode.BudgetExceeded : AreaErrorCode.MalformedRecord, AreaStage.Sector); }
    }
    /// <summary>The first type-specific slot: a scenery's scenery flags.</summary>
    private const int SceneryFlagsSlot = 36;
    private static long Optional(IReadOnlyDictionary<int, ObjectWireValue> fields, int slot) =>
        fields.TryGetValue(slot, out var value) && value.Presence == ObjectWirePresence.Value && value.Number is { } number ? number : 0;
    private static long Number(IReadOnlyDictionary<int, ObjectWireValue> fields, int slot) => fields.TryGetValue(slot, out var value) && value.Presence == ObjectWirePresence.Value && value.Number.HasValue ? value.Number.Value : throw Fail(AreaErrorCode.UnsupportedVersionOrSchema, AreaStage.Appearance);
    private Dictionary<int, ObjectWireRecord> ReadPrototypes(CancellationToken token, AreaOwnership ownership)
    {
        var definitions = new Dictionary<int, ObjectWireRecord>();
        var rawIds = new HashSet<string>(StringComparer.Ordinal);
        var supplied = _plan.Identities.Where(i => i.Key.Role == AreaSourceRole.Prototype).ToArray();
        if (supplied.Length > _limits.MaxPrototypes) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Prototype);
        foreach (var identity in supplied.OrderBy(i => i.Key.Number))
        {
            token.ThrowIfCancellationRequested();
            using var payload = new AreaOwnership(_budget);
            var bytes = ReadFile(identity.Key, token, payload);
            var record = ReadWire(bytes, AreaStage.Prototype, 0, identity.Key.Number, prototype: true, ownership);
            ownership.Allocate(512);
            if (!rawIds.Add(record.Own.Sha256) || !definitions.TryAdd(record.Own.Number!.Value, record))
                throw Fail(AreaErrorCode.AmbiguousSource, AreaStage.Prototype, 0, identity.Key.Number);
            if (record.Length != bytes.Length) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Prototype, 0, identity.Key.Number);
        }
        foreach (var identity in supplied)
            if (!definitions.TryGetValue(identity.Key.Number, out var record) || record.Own.Number != identity.Key.Number)
                throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Prototype, 0, identity.Key.Number);
        return definitions;
    }
    // Source occurrence and optional permanent entity are independent domains.
    // Sequential framing supplies monotone non-overlapping extents per payload;
    // sourceIndex is a checked witness, never the occurrence's sole identity.
    private sealed class InstanceIndex(AreaOwnership ownership)
    {
        private readonly Dictionary<(AreaInputKey, long?), (int End, int Order)> _ends = [];
        private readonly HashSet<string> _raw = new(StringComparer.Ordinal);
        private readonly HashSet<ObjectEntityId> _entities = [];
        internal void Register(ObjectWireRecord record, AreaInputKey source, long? sector, int start, AreaStage stage, int order, bool allowNull)
        {
            if (record.Length <= 0 || start < 0) throw Fail(AreaErrorCode.MalformedRecord, stage, start, order);
            if (record.Own.Kind == 0 && !allowNull) throw Fail(AreaErrorCode.UnsupportedVersionOrSchema, stage, start, order);
            var payload = (source, sector); var end = checked(start + record.Length);
            if (_ends.TryGetValue(payload, out var prior) && (start != prior.End || order != checked(prior.Order + 1)))
                throw Fail(AreaErrorCode.DuplicateIdentity, stage, start, order);
            if (!_ends.ContainsKey(payload) && order != 0) throw Fail(AreaErrorCode.MalformedRecord, stage, start, order);
            ownership.Allocate(512); // dictionary/hash capacity and copied semantic projection
            _ends[payload] = (end, order);
            if (record.Own.Entity is { } entity && (!_raw.Add(record.Own.Sha256) || !_entities.Add(entity)))
                throw Fail(AreaErrorCode.DuplicateIdentity, stage, start, order);
        }
    }
    private static IEnumerable<AreaSectorCoordinate> Neighbors(AreaSectorCoordinate coordinate, bool diagonal)
    {
        for (var y = -1; y <= 1; y++) for (var x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0 || !diagonal && Math.Abs(x) + Math.Abs(y) != 1) continue;
            var nx = coordinate.X + x; var ny = coordinate.Y + y;
            if (nx >= 0 && ny >= 0 && nx < 67108864 && ny < 67108864) yield return new(nx, ny);
        }
    }
    private string Name(DatEntry entry)
    {
        if (_names.TryGetValue(entry, out var name)) return name;
        name = entry.RawNameBytes.Any(b => b > 127) ? string.Empty : string.Create(entry.RawNameBytes.Count - 1, entry.RawNameBytes, static (chars, raw) =>
        {
            for (var i = 0; i < chars.Length; i++)
            {
                var c = (char)raw[i];
                chars[i] = c == '\\' ? '/' : c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
            }
        });
        _names.Add(entry, name); return name;
    }
    private static string Safe(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token != token.Trim() || token.Any(c => c < 32 || c >= 127 || c is '/' or '\\' or ':') || token is "." or "..")
            throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Mapping);
        return token;
    }
    private AreaEntryIdentity Unique(AreaInputKey source, string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        var matches = _indexes.TryGetValue(source, out var index) && index.TryGetValue(normalized, out var found) ? found : [];
        if (matches.Length == 0) throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
        if (matches.Length != 1) throw Fail(AreaErrorCode.AmbiguousSource, AreaStage.Mapping);
        return _entryIdentities[(source, matches[0].Ordinal)];
    }
    /// <summary>ARC-RENDER-021: the roof list of one core sector of a whole map (256 roof art words after the list's own
    /// word, sector slot <c>x/4 + 16·(y/4)</c>; <c>0xFFFFFFFF</c> is no roof). A roof art is class 10 with the number in
    /// bits 19–27 (the position of its name in <c>roofname.mes</c>), the frame in 14–18 (0–8), the fill bit 13, the fade
    /// bit 12, the palette in 4–5 and the mirror flag in bit 0 (pieces 9–12 are frames 0–3 mirrored); a word with other
    /// bits, a frame above 8 or a mirrored frame above 3 is refused for its slot alone.</summary>
    private void AddRoofs(byte[] bytes, TerrainByteRange range, AreaSectorKey sector, AreaSectorCoordinate coordinate, IReadOnlyList<string> names,
        List<AreaVisualRecord> visuals, List<AreaRoofRecord> roofs, List<AreaIssue> issues, AreaOwnership output, CancellationToken token)
    {
        if (range.Length != 4 + 256 * 4) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Sector, range.Offset);
        for (var slot = 0; slot < 256; slot++)
        {
            token.ThrowIfCancellationRequested();
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(range.Offset + 4 + 4 * slot));
            if (raw == uint.MaxValue) continue;
            var index = 4096 + slot;
            try
            {
                var frame = (int)((raw >> 14) & 31); var mirrored = (raw & 1) != 0;
                if (raw >> 28 != 10 || (raw & 0xFCEU) != 0 || frame > 8 || (mirrored && frame > 3))
                    throw Fail(AreaErrorCode.UnsupportedArtClassOrState, AreaStage.Appearance, index: index);
                var number = (int)((raw >> 19) & 511);
                if (number >= names.Count) throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping, index: index);
                var entry = Resolve("art/roof/" + Safe(names[number]) + ".art");
                var key = new AreaVisualKey(AreaSourceKind.Terrain, sector, index, string.Empty, AreaLayerKind.Roof);
                AdmitVisual(visuals); output.Allocate(2048);
                // The fill and fade bits are state, not appearance: the visual's lookup leaves them out.
                AddVisual(visuals, MakeVisual(key, raw, raw & ~0x3000U, entry, 0, 0, frame, (int)((raw >> 4) & 3), mirrored, 0, 0, 100, null, token));
                roofs.Add(new(sector, slot, checked(coordinate.OriginX + 4 * (slot & 15)), checked(coordinate.OriginY + 4 * (slot >> 4)), raw,
                    frame + (mirrored ? 9 : 0), (raw & 0x2000) != 0, (raw & 0x1000) != 0, key));
            }
            catch (AreaException error) { _budget.EndFrame(); issues.Add(error.Issue with { SourceIndex = index }); }
        }
    }
    /// <summary>The three roof shade values (entries 0, 1 and 2 read like C <c>atoi</c>, kept as the byte the original stores).</summary>
    private static AreaRoofShades RoofShades(SortedDictionary<int, string> table)
    {
        byte Value(int key) => table.TryGetValue(key, out var text) ? unchecked((byte)Atoi(text)) : throw Fail(AreaErrorCode.MissingSource, AreaStage.Mapping);
        return new(Value(0), Value(1), Value(2));
    }
    private static int Atoi(string text)
    {
        var i = 0; while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') i++;
        var negative = i < text.Length && text[i] == '-'; if (i < text.Length && text[i] is '+' or '-') i++;
        var value = 0;
        for (; i < text.Length && char.IsAsciiDigit(text[i]); i++) value = unchecked(value * 10 + (text[i] - '0'));
        return unchecked(negative ? -value : value);
    }
    private bool RoofsAdmitted => _plan.Mappings.Any(m => m.Role == AreaMappingRole.Roof) && _plan.Mappings.Any(m => m.Role == AreaMappingRole.RoofShade);
    private void AdmitVisual(List<AreaVisualRecord> visuals)
    { if (visuals.Count >= _limits.MaxVisuals) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Frame); }
    private static void AddVisual(List<AreaVisualRecord> visuals, AreaVisualRecord visual) => visuals.Add(visual);
    private sealed record SurveyRow(AreaSectorCoordinate Coordinate, DatEntry Entry, int? Townmap);
    private sealed record Correspondence(int Type, int? Prototype, string Semantic);
}
