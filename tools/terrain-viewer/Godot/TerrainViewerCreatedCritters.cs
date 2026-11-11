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
        var revision = session.Version.Revision;
        if (revision == _createdCrittersRevision && ReferenceEquals(_createdCrittersHost, host) && _createdCrittersMap == CurrentAreaMap) return;
        _createdCrittersRevision = revision; _createdCrittersHost = host; _createdCrittersMap = CurrentAreaMap;
        var map = new SessionMapId(CurrentAreaMap);
        inventory.Resolver.SetCreated(critters.Maps(map));
        var added = host.SyncCreated(critters.Npcs(map));
        foreach (var (entity, failure) in critters.TakeNewRefusals()) GD.Print($"ARC-DEV-359 created-critter entity={entity} not hosted: {failure}");
        if (added > 0) GD.Print($"ARC-DEV-359 created-critters map={CurrentAreaMap} hosted={added} npcs={host.Scene.Npcs.Count}");
    }

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
}
