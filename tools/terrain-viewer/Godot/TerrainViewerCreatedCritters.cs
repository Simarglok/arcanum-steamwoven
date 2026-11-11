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
        // ARC-DEV-371: the hero left the map these critters were made on: the original's encounter creatures are destroyed when their sector is no longer loaded, and a map change unloads them all.
        if (_createdCrittersMap != 0 && _createdCrittersMap != CurrentAreaMap && ReferenceEquals(_createdCrittersSession, session))
            DestroyEncounterCritters(session, new SessionMapId(_createdCrittersMap), "left-map");
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

    // ARC-DEV-386 (E1-c4, OM16, RK14): the critters a script created whose inventory is not generated yet. The original's create finish runs the restock body at once (a living NPC with an inventory source and
    // no PC leader gets its goods); the viewer queues the entity from the script's callback and generates from the frame (the restock host needs the barter data, which loads in the background, and the
    // call is the session's, never made from inside a run's callback). A critter the session no longer holds, or one of a map left, is dropped; a save taken before the frame keeps it empty (a limit).
    private readonly SortedSet<string> _createdInventoryPending = new(StringComparer.Ordinal);

    private GameSessionOwner? _createdInventorySession;
    private readonly Dictionary<string, SessionRegenerateResult> _createdInventoryDone = new(StringComparer.Ordinal);

    private void QueueCreatedInventory(string entity) => _createdInventoryPending.Add(entity);

    /// <summary>Generates the inventory of each queued created critter once the restock host is ready (the barter data loaded or failed); one regeneration per critter, logged.</summary>
    private void TakeCreatedInventories(GameSessionOwner session)
    {
        // A replaced session (a load) has none of the queued critters: the queue and the results belong to one session.
        if (!ReferenceEquals(_createdInventorySession, session)) { _createdInventorySession = session; _createdInventoryPending.Clear(); _createdInventoryDone.Clear(); }
        if (_createdInventoryPending.Count == 0 || RestockHostNow() is not { } restock || _inventoryHost is not { } inventory || _createdPrototypeRead is not { } read) return;
        // The resolver answers the restock host for a critter created since the last sync.
        var map = new SessionMapId(CurrentAreaMap);
        inventory.Resolver.SetCreated(CreatedCrittersOf(session, read).Maps(map));
        foreach (var entity in _createdInventoryPending.ToArray())
        {
            // A critter the session no longer holds is dropped; one of another map than the shown one waits until its map is shown (the regeneration runs on the actor's map).
            if (session.QueryCreatedObject(entity) is not { } held) { _createdInventoryPending.Remove(entity); continue; }
            if (held.Map != map) continue;
            _createdInventoryPending.Remove(entity);
            // RK3 (0x463C60): the body does nothing for an NPC without an inventory source, a dead one or one with a PC leader (the source is read first, 0x4641A0); only then it clears and generates.
            if (!restock.GeneratesAtCreation(map, entity))
            {
                GD.Print($"ARC-DEV-386 created-critter-inventory entity={entity} nothing (no inventory source, dead, led by the PC or not a holder here: source={restock.Facts(map, entity)?.InventorySource.ToString() ?? "-"})");
                continue;
            }
            var result = session.RegenerateInventory(session.Version, entity, AreaEvents);
            var done = result.Regeneration;
            _createdInventoryDone[entity] = result;
            GD.Print($"ARC-DEV-386 created-critter-inventory entity={entity} disposition={result.Disposition} failure={result.Failure} outcome={done?.Outcome} coins={done?.Coins} items={done?.Created.Count ?? 0} " +
                $"refused={done?.Refused ?? 0} skipped={done?.Skipped ?? 0} wield-steps={done?.Wield?.Steps.Count ?? 0}");
        }
    }

    /// <summary>ARC-DEV-371 (PG1): applies the destroys the AI host's ticks queued for encounter creatures (a tick holds the session, so this runs from the frame, not from a sync that a script's callback may reach).</summary>
    private void DrainEncounterDestroys(GameSessionOwner session, AreaAiHost host)
    {
        foreach (var gone in host.TakeDestroyedCreated())
        {
            var destroyed = session.DestroyCreatedObject(session.Version, gone);
            GD.Print($"ARC-DEV-371 encounter-creature-destroyed entity={gone} disposition={destroyed.Disposition} failure={destroyed.Failure}");
        }
    }

    /// <summary>The critters of <paramref name="map"/> that carry the encounter flag are destroyed in the session.</summary>
    private void DestroyEncounterCritters(GameSessionOwner session, SessionMapId map, string why)
    {
        foreach (var made in session.QueryCreatedObjects(map).Where(o => o.Kind == SessionCreatedKind.Critter && (session.QueryObjectFlags(o.Entity).CritterSet & SessionCreatedObject.EncounterCritterFlag) != 0).ToArray())
        {
            var destroyed = session.DestroyCreatedObject(session.Version, made.Entity);
            GD.Print($"ARC-DEV-371 encounter-creature-destroyed entity={made.Entity} why={why} disposition={destroyed.Disposition} failure={destroyed.Failure}");
        }
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
        _traitBodies.TryGetValue(key, out var monster) ? monster
        : _areaOverlay is { } overlay && overlay.Bodies.TryGetValue(key, out var own) ? own : _groundBodies.GetValueOrDefault(key);

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
            // ARC-MAGIC-053 (SUM5): a summoned creature is made with OF_DONTDRAW set and shows when the summoning animation's callback clears it (the ground bodies are computed again with every session revision).
            if (CreatedHidden(session, npc.Entity)) continue;
            if (CreatedCritterBody(area, frames, extra, npc) is not { } made) continue;
            var key = npc.Row.Key;
            bodies[key] = made.Command;
            only[key] = (npc.Entity, new CellCoord(checked((int)npc.Row.WorldX!.Value), checked((int)npc.Row.WorldY!.Value)), npc.Row);
        }
    }

    /// <summary>
    /// The stand body of a created critter at its row's cell: its visual (kept among the extra visuals), the pose's frame (added to <paramref name="frames"/>) and the command that draws it, under the key of its row
    /// (placed upright as a map NPC's T0 body is: the visual's unmirrored hotspot, flipped by its Mirrored flag). Null when the art does not read (said once; the critter stays in the session and acts).
    /// ARC-MAGIC-050: the body of a created critter that joined the party is this command too (<see cref="CreatedCompanionTemplates"/>).
    /// </summary>
    private (AreaDrawCommand Command, AreaVisualRecord Visual, AreaBodyPose Pose)? CreatedCritterBody(AreaLoadResult area, AreaFrameOverlay frames, AreaExtraArt extra, AreaAiNpc npc)
    {
        if (npc.Row.WorldX is not { } x || npc.Row.WorldY is not { } y || Number(npc.Row, CurrentArtSlot) is not { } aid) return null;
        if (!_createdCritterArt.TryGetValue(aid, out var load))
        {
            _createdCritterArt[aid] = load = AreaCreatedCritterArt.Load(aid, area.ArtSources, extra.MappedName);
            GD.Print($"ARC-DEV-370 created-critter-art art=0x{aid:X8} status={load.Status} detail={load.Detail}");
        }
        if (load.Visual is not { } visual || load.Pose is not { } pose) return null;
        frames.Add(pose.Key, pose.Frame);
        _extraVisuals[visual.Key] = visual;
        return (AreaScenePlan.Place(area.Plan.Chart, npc.Row.Key, visual, x, y, false) with { Frame = pose.Key }, visual, pose);
    }

    // ARC-MAGIC-050 (TI12): a critter the session created that is in the party (following or waiting, not free) is a companion of the shown map like any other: it walks by the group's follow drive, shares the
    // follower portraits and the loyalty and carry rules, and goes with the PC across a change of map (the session moves the whole object, GameSessionOwner.RehomeCreatedCritters). Its body template is the
    // created critter's own stand body (CreatedCritterBody) with the follow facts of its AI row (AreaCompanionData), standing on the map it is on. The group is rebuilt (BindAreaCompanions) when the set of
    // such critters changes; the signature below is the set the last rebuild looked at, taken only once the art, the prototypes and the companion data were there to look with.
    private (GameSessionOwner Session, int Map, string Entities)? _createdCompanionLooked;

    /// <summary>The created critters of the shown map that are in the party: their entities in creation order.</summary>
    // A dead one is no body of the group (the fallen pose draws it, as for a map companion); its death changes the set, so the group is rebuilt without it.
    private string CreatedPartyEntities(GameSessionOwner session) => string.Join(",", session.QueryCreatedObjects(new SessionMapId(CurrentAreaMap))
        .Where(made => made.Kind == SessionCreatedKind.Critter && session.QueryCompanion(made.Entity).Standing != Arcanum.Session.CompanionStanding.Free
            && !CreatedHidden(session, made.Entity) && _combatHost?.Target(session, made.Entity) is not { Dead: true }).Select(made => made.Entity));

    /// <summary>ARC-MAGIC-053 (SUM5): a created critter that carries OF_DONTDRAW (a summoned creature until its summoning animation's callback) is not drawn, as a body of the ground or of the party; the flag going changes the set the party's bodies are built for.</summary>
    private static bool CreatedHidden(GameSessionOwner session, string entity) => (session.QueryObjectFlags(entity).Object(0) & Arcanum.Simulation.Magic.SpellTargetObject.DontDraw) != 0;

    /// <summary>Rebuilds the companions when the created critters in the party are not the ones the group was last built for (a joined pet, one that left, a load). The group's own walk state is recorded first.</summary>
    private void SyncCreatedCompanions(GameSessionOwner session)
    {
        if (_readyMapName is null || _readyArea is null || _areaCompanions is null || _companionData?.Data is null || _createdPrototypeRead is null || ExtraArt() is null) return;
        var wanted = (session, CurrentAreaMap, CreatedPartyEntities(session));
        var looked0 = _createdCompanionLooked;
        if (_createdCompanionLooked is { } looked && ReferenceEquals(looked.Session, session) && looked.Map == wanted.CurrentAreaMap && looked.Entities == wanted.Item3) return;
        // Not yet part of the group: nothing to record for it, and nothing to rebuild when none is wanted and none was built.
        _createdCompanionLooked = wanted;
        if (wanted.Item3.Length == 0 && looked0 is null) return;
        RecordAreaCompanions("created companions changed");
        BindAreaCompanions();
    }

    /// <summary>The companion templates of the created critters of the shown map that are in the party, each with the frames its body draws (see <see cref="SyncCreatedCompanions"/>). Empty until the art, the prototypes
    /// and the companion data are ready.</summary>
    private IReadOnlyList<(AreaCompanionTemplate Template, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> Frames)> CreatedCompanionTemplates(GameSessionOwner session, AreaGridSession grid)
    {
        if (_readyArea is not { } area || ExtraArt() is not { } extra || _createdPrototypeRead is not { } read || _companionData?.Data is null) return [];
        var map = new SessionMapId(CurrentAreaMap);
        var critters = CreatedCrittersOf(session, read);
        var templates = new List<(AreaCompanionTemplate, IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame>)>();
        foreach (var npc in critters.Npcs(map))
        {
            if (session.QueryCompanion(npc.Entity).Standing == Arcanum.Session.CompanionStanding.Free || CreatedHidden(session, npc.Entity) || SessionCreatedObject.Number(npc.Entity) is not { } number) continue;
            if (CreatedCritterBody(area, (AreaFrameOverlay)AreaFrames(), extra, npc) is not { } body) continue;
            var template = AreaCompanionTemplate.ForCreated(number, npc.Row, MapCompanionFacts, map, grid.Chart, body.Command, body.Visual);
            if (template is null) { GD.Print($"ARC-MAGIC-050 created-companion entity={npc.Entity} no follow facts (the AI row or fields do not read)"); continue; }
            if (SpeakerDead(template.Speaker)) continue;
            templates.Add((template, new Dictionary<AreaFrameKey, DecodedAreaFrame> { [body.Pose.Key] = body.Pose.Frame }));
        }
        _createdCompanionLooked = (session, CurrentAreaMap, CreatedPartyEntities(session));
        return templates;
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
        // ARC-DEV-394: a stage run earlier in the process can leave the world's test time set (RestoreWorldTestTime keeps it at the session's time), and then ProcessWorld does not run in a
        // native smoke, so the frame never reaches ProcessAreaAi, which hands the session's created critters to the AI host (SyncCreatedCritters). The frames of this stage ask for the host
        // themselves, which is that same sync, so the stage does not depend on what ran before it.
        async Task Settle(int frames = 3)
        {
            for (var i = 0; i < frames; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AreaAiHostNow(); SyncAreaInventory(); }
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
        // ARC-DEV-405: as in the created-critter stage (ARC-DEV-394), a stage run earlier in the process can leave the world's test time set, and then ProcessWorld does not run in a native smoke: the frame
        // never reaches ProcessAreaAi, which hands the created critters to the AI host and runs the tile notifications of their creation (RunNpcTilePending). The frames of this stage run that AI pass
        // themselves, so the stage does not depend on what ran before it.
        async Task Settle(int frames = 3)
        {
            for (var i = 0; i < frames; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); ProcessAreaAi(); SyncAreaInventory(); }
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
            // ARC-DEV-386 (E1-c4): each is a holder of the map's seed and its inventory was generated by the creation's restock body (the frame takes it once the barter data is loaded).
            for (var wait = 0; wait < 900 && _createdInventoryPending.Count > 0; wait++) await Settle(1);
            Require(_createdInventoryPending.Count == 0, "the inventories of the created critters were taken (the restock host is ready)");
            var current = session.QueryMapInventory(map);
            Require(critters.All(c => current?.Holders.Any(h => h.Entity == c.Entity && h.Kind == SessionHolderKind.Npc) == true), "each is an NPC holder of the map's seed");
            Require(_createdInventoryDone.Values.All(r => r.Disposition != GameSessionDisposition.Rejected), "no restock body was refused");
            var goods = critters.Select(c => $"{c.Entity}:source={(CreatedCrittersRow(c.Entity) is { } row ? Arcanum.Data.Combat.CombatFieldReader.Read(row).Fields?.Critter?.InventorySource ?? -1 : -2)}" +
                $",outcome={(_createdInventoryDone.TryGetValue(c.Entity, out var r) ? r.Regeneration?.Outcome.ToString() ?? "-" : "none")},items={current!.Holders.First(h => h.Entity == c.Entity).Items.Count}").ToArray();
            GD.Print($"ARC-DEV-386 script-critters inventories {string.Join(" ", goods)}");
            // The twig's critters have no inventory source; the first critter prototype of the module that has one is made next to the hero, as a script's create object makes it, and its goods are checked.
            var withSource = Enumerable.Range(1, 40000).Select(n => (Number: n, Row: CreatedObjectRows.Critter(_createdPrototypeRead, n + AreaPrototypeItems.PrototypeOffset, SessionCreatedObject.CritterPrefix + "1", CurrentAreaMap, 0, 0).Row))
                .Where(pair => pair.Row is not null && (Arcanum.Data.Combat.CombatFieldReader.Read(pair.Row).Fields?.Critter?.InventorySource ?? 0) != 0).Cast<(int Number, AreaObjectRecord? Row)>().FirstOrDefault();
            if (withSource.Row is null) GD.Print("ARC-DEV-386 script-critters no critter prototype with an inventory source was found");
            else
            {
                var source = Arcanum.Data.Combat.CombatFieldReader.Read(withSource.Row).Fields!.Critter!.InventorySource;
                var made = session.CreateCritterNear(session.Version, withSource.Number + AreaPrototypeItems.PrototypeOffset, map, new(hero.Cell.X, hero.Cell.Y), AreaDropMapNow()!);
                Require(made.Object is not null, "a critter with an inventory source is made");
                var stocked = made.Object!;
                QueueCreatedInventory(stocked.Entity);
                for (var wait = 0; wait < 900 && _createdInventoryPending.Count > 0; wait++) await Settle(1);
                _createdInventoryDone.TryGetValue(stocked.Entity, out var stock);
                Require(_createdInventoryPending.Count == 0 && stock is not null && stock.Disposition == GameSessionDisposition.Changed, "its restock body ran");
                var items = session.QueryMapInventory(map)!.Holders.First(h => h.Entity == stocked.Entity).Items;
                GD.Print($"ARC-DEV-386 script-critters stocked prototype={withSource.Number} source={source} outcome={stock!.Regeneration?.Outcome} coins={stock.Regeneration?.Coins} items={items.Count} wield-steps={stock.Regeneration?.Wield?.Steps.Count ?? 0}");
                Require(session.DestroyCreatedObject(session.Version, stocked.Entity).Disposition == GameSessionDisposition.Changed, "the stocked critter is destroyed");
                Require(session.QueryMapInventory(map)!.Holders.All(h => h.Entity != stocked.Entity) && session.QueryNpcGold(stocked.Entity) is null, "its holder and its goods went with it");
            }
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
