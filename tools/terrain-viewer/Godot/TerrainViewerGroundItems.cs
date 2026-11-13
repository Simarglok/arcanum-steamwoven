using System.Security.Cryptography;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Lighting;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
        _createdPrototypeRead = null; _createdPrototypeNumbers = []; _createdPrototypes.Clear();
        var found = AreaContentRoot.Discover(module);
        if (found.Status != AreaContentRootStatus.Ready) { _extraArtFailure = $"{found.Status}"; }
        else if (ContentStack.OpenShared(found.Plan!) is { Status: ContentStackStatus.Ready, Stack: { } stack })
        {
            var prototypes = ContentPrototypes.Open(stack);
            _junkPileArt = Arcanum.Data.Audio.ObjectSoundReader.ReadPrototype(prototypes.Read, Arcanum.Simulation.Items.JunkPileRules.Prototype).Fields?.CurrentArt;
            // ARC-RENDER-041: the pool of blood's art (prototype 4048).
            _bloodPoolArt = Arcanum.Data.Audio.ObjectSoundReader.ReadPrototype(prototypes.Read, BloodPoolRules.Prototype).Fields?.CurrentArt;
            _extraArt = new(stack);
            // ARC-DEV-347: the prototype reader of the created scenery and eye-candy objects (their art and flags are read by prototype number, once).
            _createdPrototypeRead = prototypes.Read; _createdPrototypeNumbers = prototypes.Numbers; _createdPrototypes.Clear();
        }
        else _extraArtFailure = "content stack did not open";
        GD.Print($"ARC-RENDER-033 extra-art {(_extraArt is null ? "unavailable " + _extraArtFailure : "ready")}");
        GD.Print($"ARC-WORLD-063 junk-pile-art {(_junkPileArt is { } pileArt ? $"0x{pileArt:X8}" : "unread")}");
        GD.Print($"ARC-RENDER-041 blood-pool-art {(_bloodPoolArt is { } poolArt ? $"0x{poolArt:X8}" : "unread")}");
        return _extraArt;
    }

    // ARC-WORLD-081: the item prototypes scripts create, read from the same module's content stack on first use.
    private AreaPrototypeItems? _prototypeItems;
    private string? _prototypeItemsModule;

    /// <summary>ARC-WORLD-081: the item prototypes of the module the area was read from, opened on first use; null when its content stack
    /// does not open (said once).</summary>
    private AreaPrototypeItems? PrototypeItems()
    {
        var module = _arguments?.ModulePath;
        if (module is null) return null;
        if (_prototypeItemsModule == module) return _prototypeItems;
        _prototypeItems?.Dispose(); _prototypeItems = null; _prototypeItemsModule = module;
        var found = AreaContentRoot.Discover(module);
        if (found.Status == AreaContentRootStatus.Ready && ContentStack.OpenShared(found.Plan!) is { Status: ContentStackStatus.Ready, Stack: { } stack }) _prototypeItems = new(stack);
        GD.Print($"ARC-WORLD-081 prototype-items {(_prototypeItems is null ? "unavailable " + found.Status : "ready")}");
        return _prototypeItems;
    }

    /// <summary>ARC-DEV-099 (MIG8, SA4): attaches the module's prototype script arrays to <paramref name="session"/>, wherever the viewer builds,
    /// carries or restores one (the attachment is not state and is not saved), so a made item reads its prototype's array until its first write
    /// whether or not a script has created an item yet. The source reads the prototype when asked (opened on first use, kept), so a session
    /// without the module's content answers none, which is the older behaviour.</summary>
    private void AttachAreaPrototypeScripts(GameSessionOwner session)
    {
        session.AttachItemScripts(prototype => PrototypeItems()?.Item(prototype)?.Scripts);
        // ARC-WORLD-1283: and their no-drop flag, which the remove check of a transfer reads on a kit or made item.
        session.AttachItemNoDrop(prototype => PrototypeItems()?.Item(prototype)?.NoDrop == true);
    }

    /// <summary>
    /// The shown map's bodies drawn from extra art, recomputed when the session or the map changes: a scenery or trap the session
    /// busted takes its destroyed ART (field 24) at its own anchor, and every item lying on the map's ground away from a T0 body
    /// (spilled from a container, dropped by an NPC, placed by the hero) is drawn at its cell from its ground ART, except a thrown item
    /// still in flight (ARC-COMBAT-017). An art id that does not read leaves the body as it was (T0, or not drawn) and is said once.
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
        // ARC-RENDER-041: a pool playing its frames changes its body without a revision, and a pool decays as game time passes
        // (which moves no revision either).
        // ARC-DEV-380: a computation of the ground bodies from nothing (a map's first, and after a reset) is timed (steps print as ground.NAME).
        using var lap = PerfProbe.Enabled && _groundStamp is null ? PerfProbe.Lap("ground") : null;
        lap?.Step("stamp");
        var stamp = (area, session, session.Version.Revision, _inventoryHost, _areaKitArt, ObjectTargets(), _schematicData?.Data, BloodPoolFramesStamp(),
            session.QueryBloodPools(new SessionMapId(CurrentAreaMap)).Count, _createdPrototypeRead is not null);
        if (Equals(stamp, _groundStamp)) return false;
        lap?.Step("frames");
        var frames = (AreaFrameOverlay)AreaFrames();
        var overlay = _areaOverlay!.Value;
        var bodies = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        var only = new Dictionary<AreaObjectKey, (string, CellCoord, AreaObjectRecord)>();
        var placedItems = session.QueryPlacedItems().ToDictionary(p => p.Entity, StringComparer.Ordinal);
        AreaExtraFrame? Frame(uint aid, string what)
        {
            if (ExtraArt() is not { } art) return null;
            var load = art.Load(aid);
            if (load.Frame is { } frame) { frames.Add(frame.Key, frame.Frame); _extraVisuals[frame.Visual.Key] = frame.Visual; return frame; }
            if (_groundReported.Add($"{what}:{aid}")) GD.Print($"ARC-RENDER-033 extra-art {what} art=0x{aid:X8} status={load.Status} detail={load.Detail}");
            return null;
        }
        lap?.Step("busted");
        // ARC-SCRIPT-1735 (ledger NS5): a scenery whose art id a script's change art number wrote is drawn from the new art where its T0 body stood (the destroyed ART of a busted one below wins).
        if (ObjectTargets() is { } artTargets)
            foreach (var (entity, aid) in session.QueryObjectArts(new SessionMapId(area.Snapshot.RuntimeMap)))
            {
                if (artTargets.KeyOfEntity(entity) is not { } key || !overlay.Bodies.TryGetValue(key, out var command) || !overlay.Visuals.TryGetValue(command.Visual, out var visual)
                    || area.Snapshot.Objects.FirstOrDefault(row => row.Key == key) is not { Type: SceneryType } row || aid == visual.RawArt) continue;
                if (Frame(aid, "object-art") is not { } frame) continue;
                bodies[key] = AreaScenePlan.Repose(command, row, visual, frame.Key, frame.Frame.Width, frame.Frame.Height, frame.Visual.HotX, frame.Visual.HotY);
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
        lap?.Step("extra-art");
        if (lap is not null) _ = ExtraArt(); // measuring only: the first open is timed apart from the items
        lap?.Step("items");
        // The items on the ground away from a T0 body.
        var map = _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == area.Snapshot.RuntimeMap);
        var current = session.QueryMapInventory(new SessionMapId(CurrentAreaMap));
        if (current is not null)
            foreach (var ground in current.Ground)
            {
                var entity = ground.Item.Entity;
                // ARC-COMBAT-017: a thrown item in flight is hidden (object flag 0x2) until it lands; its projectile is drawn instead.
                if (session.QueryItemInFlight(entity)) continue;
                AreaObjectRecord? row = null;
                if (map?.TryGetRow(entity, out var own) == true) row = own;
                else if (placedItems.GetValueOrDefault(entity) is { Origin: { } origin }
                    && _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == origin.RuntimeMap) is { } home && home.TryGetRow(entity, out var taken)) row = taken;
                // A T0 body standing on the item's own cell already draws it.
                if (row is not null && row.Key.RuntimeMap == area.Snapshot.RuntimeMap && overlay.Bodies.ContainsKey(row.Key)
                    && row.WorldX == ground.Cell.X && row.WorldY == ground.Cell.Y) continue;
                // ARC-WORLD-070: a made item (an assembly product left on the tile) takes its prototype's art from the schematic data;
                // ARC-WORLD-081: an item a script created, and a made item the schematic data does not know (a picked-up herb dropped again),
                // takes its prototype's own art.
                var placedItem = row is null ? placedItems.GetValueOrDefault(entity) : null;
                uint? aid = row is not null ? Number(row, CurrentArtSlot) : KitArt(entity)
                    ?? (placedItem is { Source: SessionPlacedSource.Made } made ? _schematicData?.Data?.ArtOf(made.Kind.Prototype) : null)
                    ?? (placedItem is { Source: SessionPlacedSource.Made or SessionPlacedSource.Created } lying ? PrototypeItems()?.Item(lying.Kind.Prototype)?.Art : null);
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
                if (ExtraArt() is null || _junkPileArt is not { } pileArt || Frame(pileArt, "junk-pile") is not { } frame)
                {
                    if (_groundReported.Add("nopile:" + pile.Entity)) GD.Print($"ARC-WORLD-063 junk-pile entity={pile.Entity} not drawn: no pile art");
                    continue;
                }
                var key = PlacedKey(area.Snapshot.RuntimeMap, pile.Entity);
                bodies[key] = AreaScenePlan.Place(area.Plan.Chart, key, frame.Visual, pile.Cell.X, pile.Cell.Y, true);
                only[key] = (pile.Entity, pile.Cell, new AreaObjectRecord(key, 0, ContainerType, Arcanum.Simulation.Items.JunkPileRules.Prototype, "", "", "", "",
                    AreaScope.Core, null, pile.Cell.X, pile.Cell.Y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true));
            }
        lap?.Step("blood");
        AddBloodPoolBodies(area, bodies, Frame);
        lap?.Step("created");
        // ARC-DEV-347 (E1-a): the scenery and eye-candy objects the session created, drawn from their prototypes' art; a scenery is picked.
        AddCreatedObjectBodies(area, bodies, only, Frame);
        // ARC-DEV-370 (E1-c2): the critters the session created, drawn from their prototypes' art and picked like map NPCs.
        lap?.Step("critters");
        AddCreatedCritterBodies(area, frames, bodies, only);
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
        // ARC-DEV-347: a created scenery is picked and named but is no item to take.
        if (SessionCreatedObject.Number(placed.Entity) is not null) return null;
        return new(SessionJunkPile.Number(placed.Entity) is not null ? AreaInventoryTargetKind.JunkPile : AreaInventoryTargetKind.GroundItem, placed.Entity, placed.Row);
    }
}
