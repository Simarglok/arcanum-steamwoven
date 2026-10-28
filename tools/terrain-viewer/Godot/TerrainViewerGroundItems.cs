using System.Security.Cryptography;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Lighting;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-RENDER-033: the art no T0 body drew (destroyed ART, items lying away from a T0 body), read from the game's content stack
    // once per module; the bodies drawn from it on the shown map; and the items the hero placed, which have no map row, by the key
    // they are drawn and picked under.
    private AreaExtraArt? _extraArt;
    private string? _extraArtModule, _extraArtFailure;
    private IReadOnlyList<uint?>? _areaKitArt;
    private Dictionary<AreaObjectKey, AreaDrawCommand> _groundBodies = [];
    private Dictionary<AreaVisualKey, AreaVisualRecord> _extraVisuals = [];
    private Dictionary<AreaObjectKey, (string Entity, CellCoord Cell, AreaObjectRecord Row)> _groundOnly = [];
    private object? _groundStamp;
    private readonly HashSet<string> _groundReported = new(StringComparer.Ordinal);
    private const int ItemType = 14, SceneryType = 3, TrapType = 17, DestroyedArtSlot = 24, FlagsSlot = 18, CurrentArtSlot = 0, ContainerType = 2;
    // ARC-WORLD-063: the junk pile prototype's current art (3043), read when the content stack opens; null when it does not read.
    private uint? _junkPileArt;

    /// <summary>The extra art of the module the area was read from, opened on first use; null when its content stack does not open
    /// (said once).</summary>
    private AreaExtraArt? ExtraArt()
    {
        var module = _arguments?.ModulePath;
        if (module is null) return null;
        if (_extraArtModule == module) return _extraArt;
        _extraArt?.Dispose(); _extraArt = null; _extraArtModule = module; _extraArtFailure = null;
        var found = AreaContentRoot.Discover(module);
        if (found.Status != AreaContentRootStatus.Ready) { _extraArtFailure = $"{found.Status}"; }
        else if (ContentStack.Open(found.Plan!) is { Status: ContentStackStatus.Ready, Stack: { } stack })
        {
            _junkPileArt = Arcanum.Data.Audio.ObjectSoundReader.ReadPrototype(ContentPrototypes.Open(stack).Read, Arcanum.Simulation.Items.JunkPileRules.Prototype).Fields?.CurrentArt;
            _extraArt = new(stack);
        }
        else _extraArtFailure = "content stack did not open";
        GD.Print($"ARC-RENDER-033 extra-art {(_extraArt is null ? "unavailable " + _extraArtFailure : "ready")}");
        GD.Print($"ARC-WORLD-063 junk-pile-art {(_junkPileArt is { } pileArt ? $"0x{pileArt:X8}" : "unread")}");
        return _extraArt;
    }

    /// <summary>
    /// The shown map's bodies drawn from extra art, recomputed when the session or the map changes: a scenery or trap the session
    /// busted takes its destroyed ART (field 24) at its own anchor, and every item lying on the map's ground away from a T0 body
    /// (spilled from a container, dropped by an NPC, placed by the hero) is drawn at its cell from its ground ART. An art id that does
    /// not read leaves the body as it was (T0, or not drawn) and is said once.
    /// </summary>
    /// <returns>Whether the bodies were recomputed.</returns>
    private bool SyncAreaGroundBodies()
    {
        if (_readyArea is not { } area || _areaSession is not { } session || !AttackMapActive && !InventoryActive)
        {
            _groundStamp = null;
            if (_groundBodies.Count == 0) return false;
            _groundBodies = []; _groundOnly = [];
            return true;
        }
        // The inventory host (its attached seeds), the kit's art and the object targets change without a session revision.
        var stamp = (area, session, session.Version.Revision, _inventoryHost, _areaKitArt, ObjectTargets(), _schematicData?.Data);
        if (Equals(stamp, _groundStamp)) return false;
        var frames = (AreaFrameOverlay)AreaFrames();
        var overlay = _areaOverlay!.Value;
        var bodies = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        var only = new Dictionary<AreaObjectKey, (string, CellCoord, AreaObjectRecord)>();
        var placedItems = session.QueryPlacedItems();
        AreaExtraFrame? Frame(uint aid, string what)
        {
            if (ExtraArt() is not { } art) return null;
            var load = art.Load(aid);
            if (load.Frame is { } frame) { frames.Add(frame.Key, frame.Frame); _extraVisuals[frame.Visual.Key] = frame.Visual; return frame; }
            if (_groundReported.Add($"{what}:{aid}")) GD.Print($"ARC-RENDER-033 extra-art {what} art=0x{aid:X8} status={load.Status} detail={load.Detail}");
            return null;
        }
        // A busted scenery or trap: its destroyed ART where its T0 body stood.
        if (ObjectTargets() is { } targets)
            foreach (var key in targets.Busted(session))
            {
                if (!overlay.Bodies.TryGetValue(key, out var command) || !overlay.Visuals.TryGetValue(command.Visual, out var visual)
                    || area.Snapshot.Objects.FirstOrDefault(row => row.Key == key) is not { Type: SceneryType or TrapType } row) continue;
                if (Number(row, DestroyedArtSlot) is not { } destroyed || AreaDataOwner.NamesNoArt(destroyed) || destroyed == visual.RawArt) continue;
                if (Frame(destroyed, "destroyed") is not { } frame) continue;
                bodies[key] = AreaScenePlan.Repose(command, row, visual, frame.Key, frame.Frame.Width, frame.Frame.Height, frame.Visual.HotX, frame.Visual.HotY);
            }
        // The items on the ground away from a T0 body.
        var map = _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == area.Snapshot.RuntimeMap);
        var current = session.QueryMapInventory(new SessionMapId(CurrentAreaMap));
        if (current is not null)
            foreach (var ground in current.Ground)
            {
                var entity = ground.Item.Entity;
                AreaObjectRecord? row = null;
                if (map?.TryGetRow(entity, out var own) == true) row = own;
                else if (placedItems.FirstOrDefault(p => p.Entity == entity) is { Origin: { } origin }
                    && _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == origin.RuntimeMap) is { } home && home.TryGetRow(entity, out var taken)) row = taken;
                // A T0 body standing on the item's own cell already draws it.
                if (row is not null && row.Key.RuntimeMap == area.Snapshot.RuntimeMap && overlay.Bodies.ContainsKey(row.Key)
                    && row.WorldX == ground.Cell.X && row.WorldY == ground.Cell.Y) continue;
                // ARC-WORLD-070: a made item (an assembly product left on the tile) takes its prototype's art from the schematic data.
                uint? aid = row is not null ? Number(row, CurrentArtSlot) : KitArt(entity)
                    ?? (placedItems.FirstOrDefault(p => p.Entity == entity) is { Source: SessionPlacedSource.Made } made ? _schematicData?.Data?.ArtOf(made.Kind.Prototype) : null);
                if (aid is not { } art)
                {
                    if (_groundReported.Add("noart:" + entity)) GD.Print($"ARC-RENDER-033 ground-item entity={entity} not drawn: no ground art known");
                    continue;
                }
                if (Frame(art, "ground") is not { } frame) continue;
                var key = row?.Key is { } rowKey && rowKey.RuntimeMap == area.Snapshot.RuntimeMap ? rowKey : PlacedKey(area.Snapshot.RuntimeMap, entity);
                // An item lies flat when its flags say so (OF_FLAT); the hero's kit items carry no map flags and stand upright.
                var flat = row is not null && Number(row, FlagsSlot) is { } flags && (flags & 0x4) != 0;
                bodies[key] = AreaScenePlan.Place(area.Plan.Chart, key, frame.Visual, ground.Cell.X, ground.Cell.Y, flat);
                if (row is null || row.Key != key)
                    only[key] = (entity, ground.Cell, new AreaObjectRecord(key, 0, row?.Type ?? ItemType, row?.PrototypeNumber ?? 0, "", "", "", "",
                        AreaScope.Core, null, ground.Cell.X, ground.Cell.Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
                        row?.EffectiveFields ?? [], true, true, true));
            }
        // ARC-WORLD-063: each junk pile the session made is drawn at its cell from the pile prototype's art (flat: its object flags carry
        // OF_FLAT, as every placed pile of the interiors does) and opened as a container; its items are not drawn.
        if (current is not null)
            foreach (var pile in current.JunkPiles)
            {
                if (_junkPileArt is not { } pileArt || Frame(pileArt, "junk-pile") is not { } frame)
                {
                    if (_groundReported.Add("nopile:" + pile.Entity)) GD.Print($"ARC-WORLD-063 junk-pile entity={pile.Entity} not drawn: no pile art");
                    continue;
                }
                var key = PlacedKey(area.Snapshot.RuntimeMap, pile.Entity);
                bodies[key] = AreaScenePlan.Place(area.Plan.Chart, key, frame.Visual, pile.Cell.X, pile.Cell.Y, true);
                only[key] = (pile.Entity, pile.Cell, new AreaObjectRecord(key, 0, ContainerType, Arcanum.Simulation.Items.JunkPileRules.Prototype, "", "", "", "",
                    AreaScope.Core, null, pile.Cell.X, pile.Cell.Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true));
            }
        _groundBodies = bodies; _groundOnly = only;
        // A placed item without a row of this map is named and picked under its own key.
        if (_hoverIndex is { } index && ReferenceEquals(_hoverIndexArea, area)) foreach (var placed in only.Values) index.Admit(placed.Item3);
        _groundStamp = stamp;
        return true;
    }

    private uint? KitArt(string entity) =>
        entity.StartsWith("kit:", StringComparison.Ordinal) && int.TryParse(entity.AsSpan(4), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var number) && number >= 1 && _areaKitArt is { } kit && number <= kit.Count ? kit[number - 1] : null;

    /// <summary>The key a placed item without a row of the shown map is drawn and picked under (the entity's digest on this map).</summary>
    private static AreaObjectKey PlacedKey(int runtimeMap, string entity) =>
        new("placed", runtimeMap, AreaSourceKind.Mobile, null, 0, 0, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(entity))).ToLowerInvariant(), 0);

    private static uint? Number(AreaObjectRecord row, int slot) =>
        row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number is { } value ? unchecked((uint)value) : null;

    /// <summary>The light of a body drawn from extra art: at its cell with its art's channel (null for any other body).</summary>
    private AreaLitColors? GroundLight(AreaDrawCommand command, AmbientPair pair, AreaLocalLighting? local)
    {
        if (!_extraVisuals.TryGetValue(command.Visual, out var visual)) return null;
        var channel = command.Unlit ? AreaAmbientChannel.Unlit : AreaAmbientPolicy.Channel(visual.RawArt);
        if (local is not null) return local.Body(command, visual, channel, pair);
        var color = AreaLocalLighting.Ambient(channel, pair);
        return new AreaLitColors(color, color);
    }

    /// <summary>The ground or loot target under a picked key: the inventory host's (an item lying away from its T0 cell is walked to
    /// at its ground cell), or a placed item without a map row.</summary>
    private AreaInventoryTarget? AreaInventoryTargetOf(AreaObjectKey key)
    {
        if (_inventoryHost!.Target(_areaSession!, CurrentAreaMap, key) is { } target)
            return target.Kind == AreaInventoryTargetKind.GroundItem && _groundBodies.TryGetValue(key, out var drawn)
                ? target with { Row = target.Row with { WorldX = drawn.MapX, WorldY = drawn.MapY } } : target;
        if (!_groundOnly.TryGetValue(key, out var placed)) return null;
        return new(SessionJunkPile.Number(placed.Entity) is not null ? AreaInventoryTargetKind.JunkPile : AreaInventoryTargetKind.GroundItem, placed.Entity, placed.Row);
    }
}
