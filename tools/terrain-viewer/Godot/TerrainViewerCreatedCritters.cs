using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Data.Objects;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.Session;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-359 (E1-c): the critters the session created on the shown map are rows the world reads by identity (the record resolver, the combat host) and NPCs the AI host runs, wherever they stand:
    // a created critter has no window to give its row, so it is built from its prototype (the module's prototype reader, set with the extra art) and handed to both after every change of the
    // session. Derived data: a load or a rebuilt host reads the session's critters again. ARC-DEV-370 (E1-c2): the body is drawn, animated and picked like a map NPC's (below).
    private AreaCreatedCritters? _createdCritters;
    private GameSessionOwner? _createdCrittersSession;
    private AreaAiHost? _createdCrittersHost;
    private long _createdCrittersRevision = -1;
    private int _createdCrittersMap;

    /// <summary>Hands the shown map's created critters to the resolver and the AI host when the session, the host or the map changed since the last call (the session's revision moves with every change, so
    /// an idle frame is one comparison). A critter whose prototype does not read as a critter is said once and left out; it stays in the session.</summary>
    private void SyncCreatedCritters(GameSessionOwner session, AreaAiHost host)
    {
        if (_createdPrototypeRead is not { } read || _inventoryHost is not { } inventory) return;
        var critters = CreatedCrittersOf(session, read);
        // ARC-DEV-371 (PG1): an encounter creature the AI's tick destroyed is destroyed in the session here, outside the tick that holds it; the sync below drops it from the host.
        foreach (var gone in host.TakeDestroyedCreated())
        {
            var destroyed = session.DestroyCreatedObject(session.Version, gone);
            GD.Print($"ARC-DEV-371 encounter-creature-destroyed entity={gone} disposition={destroyed.Disposition} failure={destroyed.Failure}");
        }
        var revision = session.Version.Revision;
        if (revision == _createdCrittersRevision && ReferenceEquals(_createdCrittersHost, host) && _createdCrittersMap == CurrentAreaMap) return;
        _createdCrittersRevision = revision; _createdCrittersHost = host; _createdCrittersMap = CurrentAreaMap;
        var map = new SessionMapId(CurrentAreaMap);
        inventory.Resolver.SetCreated(critters.Maps(map));
        var added = host.SyncCreated(critters.Npcs(map));
        foreach (var (entity, failure) in critters.TakeNewRefusals()) GD.Print($"ARC-DEV-359 created-critter entity={entity} not hosted: {failure}");
        if (added > 0) GD.Print($"ARC-DEV-359 created-critters map={CurrentAreaMap} hosted={added} npcs={host.Scene.Npcs.Count}");
    }

    // ARC-DEV-371: whether a script's prototype number names a critter prototype (a file that reads as an NPC's), per module (the cache goes with the prototype reader).
    private Dictionary<int, bool> _scriptCritterPrototypes = [];
    private Func<int, Arcanum.Data.Characters.PrototypeFile>? _scriptCritterPrototypesRead;

    /// <summary>ARC-DEV-371 (PG1, PG8): the script's prototype number (the file is the number + 20) names a critter prototype the session can create.</summary>
    private bool ScriptCritterPrototype(int number)
    {
        if (_createdPrototypeRead is not { } read || number < 0 || number > SessionCreatedObject.MaxPrototype - AreaPrototypeItems.PrototypeOffset) return false;
        if (!ReferenceEquals(_scriptCritterPrototypesRead, read)) { _scriptCritterPrototypes = []; _scriptCritterPrototypesRead = read; }
        if (_scriptCritterPrototypes.TryGetValue(number, out var known)) return known;
        var built = CreatedObjectRows.Critter(read, number + AreaPrototypeItems.PrototypeOffset, SessionCreatedObject.CritterPrefix + "1", CurrentAreaMap, 0, 0);
        return _scriptCritterPrototypes[number] = built.Row is not null;
    }

    /// <summary>ARC-DEV-371: the base record of a critter the session created on the shown map (null for any other entity).</summary>
    private AreaObjectRecord? CreatedCrittersRow(string entity) =>
        _areaSession is { } session && _createdPrototypeRead is { } read && SessionCreatedObject.KindOf(entity) == SessionCreatedKind.Critter
            ? CreatedCrittersOf(session, read).Row(new SessionMapId(CurrentAreaMap), entity) : null;

    private AreaCreatedCritters CreatedCrittersOf(GameSessionOwner session, Func<int, Arcanum.Data.Characters.PrototypeFile> read)
    {
        if (_createdCritters is not null && ReferenceEquals(_createdCrittersSession, session)) return _createdCritters;
        _createdCrittersSession = session; _createdCrittersRevision = -1;
        return _createdCritters = new(session, read);
    }

    // ARC-DEV-370 (E1-c2): the body art of each critter art id the session's created critters name (read once per set of art archives, kept across window moves: a refusal is kept and said once). The T0
    // bodies drawn for the created critters of the shown map are among the ground bodies (the AI movers move them as they move a map NPC's, AiMoverCommands).
    private Dictionary<uint, AreaCreatedCritterArtLoad> _createdCritterArt = [];
    private IReadOnlyDictionary<AreaInputKey, string> _createdCritterArtSources = new Dictionary<AreaInputKey, string>();

    /// <summary>The T0 body of a hosted NPC of the shown map: a map NPC's from the T0 plan, a created critter's from the ground bodies (no map NPC's key is among them).</summary>
    private AreaDrawCommand? NpcT0Body(AreaObjectKey key) =>
        _areaOverlay is { } overlay && overlay.Bodies.TryGetValue(key, out var own) ? own : _groundBodies.GetValueOrDefault(key);

    /// <summary>The visual of a body of the shown map: a map NPC's own (its T0 plan) or a created critter's.</summary>
    private bool TryBodyVisual(AreaVisualKey key, out AreaVisualRecord visual)
    {
        if (_areaOverlay is { } overlay && overlay.Visuals.TryGetValue(key, out var own)) { visual = own; return true; }
        return _extraVisuals.TryGetValue(key, out visual!) && visual.Policy == AreaCreatedCritterArt.Policy;
    }

    /// <summary>
    /// ARC-DEV-370 (E1-c2): each critter the session created on the shown map, drawn at its creation cell from its prototype's art (its stand pose, facing and palette), under the key of its row, and made
    /// pickable (its row joins the hover index as a map NPC's does, so the name, the cursor and the attack read it). The AI host moves it from there (the movers shift this command by the projected offset
    /// of the cell it stands on, <see cref="AnimateAiMovers"/> gives it the frame of its step, facing and attack), and the fallen pose draws over it once it is down (<see cref="SyncAreaCombatBodies"/>).
    /// A critter whose art does not read is said once and not drawn (it stays in the session and acts).
    /// </summary>
    private void AddCreatedCritterBodies(AreaLoadResult area, AreaFrameOverlay frames, Dictionary<AreaObjectKey, AreaDrawCommand> bodies,
        Dictionary<AreaObjectKey, (string, CellCoord, AreaObjectRecord)> only)
    {
        // The extra art opens the module's prototype reader, so it is asked first.
        if (_areaSession is not { } session || ExtraArt() is not { } extra || _createdPrototypeRead is not { } read) return;
        // The loads hold while the art archives are the same (a window move keeps them).
        if (_createdCritterArtSources.Count != area.ArtSources.Count || area.ArtSources.Any(pair => !_createdCritterArtSources.TryGetValue(pair.Key, out var held) || held != pair.Value)) { _createdCritterArt = []; _createdCritterArtSources = area.ArtSources; }
        var map = new SessionMapId(CurrentAreaMap);
        foreach (var npc in CreatedCrittersOf(session, read).Npcs(map))
        {
            if (npc.Row.WorldX is not { } x || npc.Row.WorldY is not { } y || Number(npc.Row, CurrentArtSlot) is not { } aid) continue;
            if (!_createdCritterArt.TryGetValue(aid, out var load))
            {
                _createdCritterArt[aid] = load = AreaCreatedCritterArt.Load(aid, area.ArtSources, extra.MappedName);
                GD.Print($"ARC-DEV-370 created-critter-art art=0x{aid:X8} status={load.Status} detail={load.Detail}");
            }
            if (load.Visual is not { } visual || load.Pose is not { } pose) continue;
            frames.Add(pose.Key, pose.Frame);
            _extraVisuals[visual.Key] = visual;
            var key = npc.Row.Key;
            // Placed upright at the creation cell as a map NPC's T0 body is (the visual's unmirrored hotspot, flipped by its Mirrored flag); the frame is the pose's.
            var command = AreaScenePlan.Place(area.Plan.Chart, key, visual, x, y, false) with { Frame = pose.Key };
            bodies[key] = command;
            only[key] = (npc.Entity, new CellCoord(checked((int)x), checked((int)y)), npc.Row);
        }
    }

    /// <summary>The one-row maps of the critters the session created on the shown map (the holders and rows the combat host and the fallen poses read); none without a session or the module's prototypes.</summary>
    private IReadOnlyList<AreaInventoryMap> CreatedCritterMaps() =>
        _areaSession is { } session && _createdPrototypeRead is { } read ? CreatedCrittersOf(session, read).Maps(new SessionMapId(CurrentAreaMap)) : [];

    /// <summary>
    /// ARC-DEV-370 (E1-c2) native observation (stage "created-critter", only when named, with --hero, on a map window with the inventory active): the session creates a critter by prototype next to the hero
    /// (the first critter prototype of the module's data whose art is a human, monster or unique NPC body that reads from the window's archives and whose row builds); the AI host holds it, its body is drawn
    /// at its cell from its art, picked (the hover index holds its row and the pick over its pixels is its key), moved by the AI's movers with a walk frame (a mover step is given the host directly: no AI
    /// plan is asked for), and gone from the picture once the session destroys it. The map's own state is left as it was.
    /// </summary>
    private async Task RunAreaCreatedCritterStageAsync()
    {
        for (var wait = 0; wait < 1800 && (ExtraArt() is null || _createdPrototypeRead is null || !InventoryActive); wait++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || !InventoryActive || ExtraArt() is not { } extra || _createdPrototypeRead is not { } read)
        { GD.Print("ARC-DEV-370 native-smoke created-critter SKIP (needs --hero, an area session, the inventory and the module's content)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-370 created-critter smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_CREATED_CRITTER_SMOKE_FAILED");
        }
        var map = new SessionMapId(CurrentAreaMap);
        // The first critter prototype with a body that reads: a human body (class 2) first, any other class when the data has no such prototype.
        (int Number, uint Art)? chosen = null;
        foreach (var wantClass in new uint[] { 2, 12, 13 })
        {
            foreach (var number in _createdPrototypeNumbers)
            {
                if (ObjectSoundReader.ReadPrototype(read, number).Fields is not { } fields || fields.Type != Arcanum.Data.Combat.CombatFieldReader.NpcType || fields.CurrentArt >> 28 != wantClass) continue;
                if (AreaCreatedCritterArt.Load(fields.CurrentArt, area.ArtSources, extra.MappedName).Status != AreaCreatedCritterArtStatus.Ready) continue;
                if (CreatedObjectRows.Critter(read, number, "oid:critter:1", area.Snapshot.RuntimeMap, 1, 1).Row is null) continue;
                chosen = (number, fields.CurrentArt); break;
            }
            if (chosen is not null) break;
        }
        if (chosen is not { } found) { GD.Print("ARC-DEV-370 native-smoke created-critter NONE (the module's data holds no critter prototype whose body reads from the window's archives)"); return; }
        if (HeroPoolPlace() is not { } hero) { GD.Print("ARC-DEV-370 native-smoke created-critter SKIP (the hero has no committed cell)"); return; }

        var aiBefore = _aiForSmoke;
        _aiForSmoke = true;
        async Task Settle(int frames = 3)
        {
            for (var i = 0; i < frames; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); SyncAreaInventory(); }
        }
        SessionCreatedObject? made = null;
        try
        {
            for (var wait = 0; wait < 600 && AreaAiHostNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(AreaAiHostNow() is { } host, "the AI host is built");
            host = AreaAiHostNow()!;
            var cell = new CellCoord(hero.Cell.X + 2, hero.Cell.Y);
            made = session.CreateObject(session.Version, SessionCreatedKind.Critter, found.Number, map, cell).Object;
            Require(made is not null, "the session created a critter");
            await Settle(6);
            var npc = host.Scene.Npcs.FirstOrDefault(n => n.Entity == made!.Entity);
            Require(npc is not null, "the AI host holds the created critter");
            var key = npc!.Row.Key;
            GD.Print($"ARC-DEV-370 created-critter entity={made!.Entity} prototype={found.Number} art=0x{found.Art:X8} cell={cell.X},{cell.Y} art-status={_createdCritterArt.GetValueOrDefault(found.Art)?.Status}");
            Require(_groundBodies.TryGetValue(key, out var body) && body.MapX == cell.X && body.MapY == cell.Y, "the critter is drawn at its cell");
            Require(body!.Identity == key && body.ArtClass == (int)(found.Art >> 28) && AreaFrames().ContainsKey(body.Frame), "its body command is its row's, of its art class, with its frame in the map's repository");
            Require(TryBodyVisual(body.Visual, out var visual) && visual.RawArt == found.Art && !visual.Mirrored, "its visual is the prototype's art");
            Require(_groundOnly.TryGetValue(key, out var picked) && picked.Entity == made.Entity && picked.Row.PrototypeNumber == found.Number, "the critter is pickable under its row");
            Require(HoverIndex() is { } index && index.Contains(key), "the hover index holds its row");
            Require(AreaInventoryTargetOf(key) is null, "a click on the critter is no pickup");
            Require(AreaMovingCommands()?.Count(c => c.Identity == key) == 1, "one moving command draws the critter");
            var point = await HoverPointAsync(this, body);
            Require(point is { } at && AreaObjectAtScreen(at) == key, "the pick over the critter's own pixels is its key");
            Require(AreaTargetOf(key) is { } target && target.Build.Combat is not null, "the combat host reads the critter as a target");

            // The movers: one step to the next cell is drawn with a walk frame and the critter's cell (no AI plan is asked for: the step is given to the host's movers).
            var walkBefore = _bodyWalkDraws;
            var next = new CellCoord(cell.X + 1, cell.Y);
            host.Movers.Step(made.Entity, key, npc.Origin, next, host.NowMs, 4000);
            var walked = AiMoverCommands().FirstOrDefault(c => c.Identity == key);
            Require(walked is not null && walked.MapX == next.X && walked.MapY == next.Y, "the mover draws the critter at its next cell");
            Require(_bodyWalkDraws > walkBefore, "the critter's step is drawn with a walk frame");
            Require(AreaMovingCommands()?.Count(c => c.Identity == key) == 1 && AreaMovingCommands()!.First(c => c.Identity == key).Frame != body.Frame, "the walk frame replaces the stand frame");
            host.Movers.Remove(made.Entity);
            Require(AreaMovingCommands()!.First(c => c.Identity == key).Frame == body.Frame, "the stand frame is drawn again once the mover is gone");

            Require(session.DestroyCreatedObject(session.Version, made.Entity).Disposition == GameSessionDisposition.Changed, "the critter is destroyed");
            await Settle(6);
            Require(!_groundBodies.ContainsKey(key) && !_groundOnly.ContainsKey(key) && AreaMovingCommands()?.Any(c => c.Identity == key) != true, "it is neither drawn nor picked once destroyed");
            Require(host.Scene.Npcs.All(n => n.Entity != made.Entity), "the AI host dropped it");
            GD.Print("ARC-DEV-370 native-smoke created-critter PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = area.Snapshot.RuntimeMap, Prototype = found.Number, Art = $"0x{found.Art:X8}" }));
        }
        finally
        {
            if (made is not null && session.QueryCreatedObject(made.Entity) is not null) session.DestroyCreatedObject(session.Version, made.Entity);
            if (AreaAiHostNow() is { } after) after.Movers.Remove(made?.Entity ?? "");
            SyncAreaInventory();
            _aiForSmoke = aiBefore;
            if (!aiBefore) { RetireAiHost(); _aiHostStamp = null; }
        }
    }

    // The installed "snapping twig" tile script (130 tiles of the start map): it creates eight critters of one prototype next to the PC (docs/world/template-sector-plants-runtime.md).
    private const int SnappingTwigScript = 1629;

    /// <summary>
    /// ARC-DEV-371 (E1-c3) native observation (stage "script-critters", only when named, with --hero, on a map window with the inventory active): the installed tile script 1629 (the snapping twig, which
    /// creates critters next to the PC through the script action create object) runs for the hero as a tile's script does, through the map's own placed-script runner. The stage checks that each critter it made is
    /// a created critter of the session next to the hero (within three tiles, PG12), carries the encounter flag 0x20000 (PG1), is held by the AI host with its row, and that the tile notification of its creation
    /// ran (nothing is left queued), then destroys them. NONE when the run made no critter (the script's own conditions).
    /// </summary>
    private async Task RunAreaScriptCrittersStageAsync()
    {
        for (var wait = 0; wait < 1800 && (ExtraArt() is null || _createdPrototypeRead is null || !InventoryActive); wait++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || !InventoryActive || _createdPrototypeRead is null || _mapTransfers is null)
        { GD.Print("ARC-DEV-371 native-smoke script-critters SKIP (needs --hero, an area session, the inventory, the map's transfers and the module's content)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-371 script-critters smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SCRIPT_CRITTERS_SMOKE_FAILED");
        }
        if (HeroPoolPlace() is not { } hero) { GD.Print("ARC-DEV-371 native-smoke script-critters SKIP (the hero has no committed cell)"); return; }
        var map = new SessionMapId(CurrentAreaMap);
        var aiBefore = _aiForSmoke;
        _aiForSmoke = true;
        async Task Settle(int frames = 3)
        {
            for (var i = 0; i < frames; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); SyncAreaInventory(); }
        }
        var before = session.QueryCreatedObjects(map).Select(o => o.Entity).ToHashSet(StringComparer.Ordinal);
        try
        {
            for (var wait = 0; wait < 600 && AreaAiHostNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(AreaAiHostNow() is not null, "the AI host is built");
            // Where the map's own snapping-twig tiles are (for the owner's re-check): the nearest ones to the hero.
            var twigs = _mapTransfers.ScriptTiles.Where(cell => _mapTransfers.TileScript(cell) is { Script: SnappingTwigScript })
                .OrderBy(cell => Math.Abs(cell.X - hero.Cell.X) + Math.Abs(cell.Y - hero.Cell.Y)).ToArray();
            GD.Print($"ARC-DEV-371 script-critters twig-tiles={twigs.Length} hero={hero.Cell.X},{hero.Cell.Y} nearest={string.Join(";", twigs.Take(5).Select(c => $"{c.X},{c.Y}"))}");
            var placed = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.TileEntry, new(0, 0), -1, new(hero.Cell.X, hero.Cell.Y), 0, 0, SnappingTwigScript);
            var ran = _mapTransfers.PlacedRunner?.Invoke(placed, _ => "a teleport is not part of this stage");
            Require(ran is not null, "the map has a placed-script runner");
            GD.Print($"ARC-DEV-371 script-critters run outcome={ran!.Outcome} detail={ran.Detail}");
            var critters = session.QueryCreatedObjects(map).Where(o => o.Kind == SessionCreatedKind.Critter && !before.Contains(o.Entity)).ToArray();
            if (critters.Length == 0) { GD.Print($"ARC-DEV-371 native-smoke script-critters NONE (script {SnappingTwigScript} made no critter here: {ran.Outcome} {ran.Detail})"); return; }
            Require(ran.Outcome == AreaScriptedUseOutcome.NoTeleport, "the run applied");
            Require(critters.Select(c => c.Prototype).Distinct().Count() == 1 && ScriptCritterPrototype(critters[0].Prototype - AreaPrototypeItems.PrototypeOffset), "the critters are of one critter prototype");
            Require(critters.All(c => Math.Abs(c.Cell.X - hero.Cell.X) <= 3 && Math.Abs(c.Cell.Y - hero.Cell.Y) <= 3), "each critter is within three tiles of the hero (PG12)");
            Require(critters.All(c => session.QueryObjectFlags(c.Entity).CritterSet == SessionCreatedObject.EncounterCritterFlag), "each carries the encounter flag 0x20000 (PG1)");
            await Settle(6);
            var host = AreaAiHostNow()!;
            Require(critters.All(c => host.Scene.Npcs.Any(n => n.Entity == c.Entity) && host.IsEncounterCreature(c.Entity)), "the AI host holds each as an encounter creature");
            Require(critters.All(c => CreatedCrittersRow(c.Entity) is not null), "each has its row");
            Require(_npcTilePending.Count == 0, "the tile notifications of the creations ran");
            GD.Print($"ARC-DEV-371 script-critters made={critters.Length} prototype={critters[0].Prototype} cells={string.Join(";", critters.Select(c => $"{c.Cell.X},{c.Cell.Y}"))} tile-runs={_npcTileRuns.Count(r => critters.Any(c => c.Entity == r.Npc))}");
            foreach (var critter in critters) Require(session.DestroyCreatedObject(session.Version, critter.Entity).Disposition == GameSessionDisposition.Changed, "a critter is destroyed");
            await Settle(6);
            Require(critters.All(c => host.Scene.Npcs.All(n => n.Entity != c.Entity)) && session.QueryAllObjectFlags().All(f => critters.All(c => c.Entity != f.Entity)), "the host dropped them and their flags went");
            GD.Print("ARC-DEV-371 native-smoke script-critters PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = area.Snapshot.RuntimeMap, Script = SnappingTwigScript, Made = critters.Length, Prototype = critters[0].Prototype }));
        }
        finally
        {
            foreach (var made in session.QueryCreatedObjects(map).Where(o => o.Kind == SessionCreatedKind.Critter && !before.Contains(o.Entity)).ToArray()) session.DestroyCreatedObject(session.Version, made.Entity);
            SyncAreaInventory();
            _aiForSmoke = aiBefore;
            if (!aiBefore) { RetireAiHost(); _aiHostStamp = null; }
        }
    }
}
