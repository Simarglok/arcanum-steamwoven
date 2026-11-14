using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-352 (E1-b of the missing-systems plan; ARC-RES-101, ledger SUM1-SUM16): the viewer's half of the Summon component for objects. The cast host reads each summoned prototype's type and flags
// from the module's prototype file, creates on the shown map and asks the overlay host whether an eye candy plays; the eye candies' callbacks (goals 47/48 at the action frame, 58/59 at the end) and
// the adds that could not play call the spell run back; a created scenery that animates (Wall of Fire's piece) gets its loop, goal 1 or the burning goal 77 with its summoner as the burn's source,
// and is drawn at its loop's frame; a summoned object that a Destroy line removed loses its loop and its overlay runs.
public partial class TerrainViewerApp
{
    // The facts of each summoned prototype by file number, read once per module (null: it does not read), and the reader they were read with.
    private readonly Dictionary<int, SessionSummonPrototype?> _summonPrototypes = [];
    private object? _summonPrototypesRead;
    // The callbacks to give the spell runs: the eye candies' own (taken from the overlay host) and the adds that could not play after all, as (run, phase).
    private readonly List<(int Run, int Phase)> _eyeCandyCallbacksNow = [];
    // The session whose created sceneries the loops have seen, the entities seen (a loop started again on a new host gets no source: SUM4; a new session, a load, starts afresh, as the original saves the run
    // with its source) and those whose prototype gives no loop (not asked again).
    private GameSessionOwner? _createdLoopSession;
    private readonly HashSet<string> _createdLoopSeen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _createdLoopNone = new(StringComparer.Ordinal);

    /// <summary>ARC-DEV-352: the type, object flags and scenery flags of the module's prototype file <paramref name="file"/>, or null when it does not read.</summary>
    private SessionSummonPrototype? SummonPrototypeOf(int file)
    {
        if (_createdPrototypeRead is not { } read) return null;
        if (!ReferenceEquals(_summonPrototypesRead, read)) { _summonPrototypes.Clear(); _summonPrototypesRead = read; }
        if (_summonPrototypes.TryGetValue(file, out var known)) return known;
        var fields = ObjectSoundReader.ReadPrototype(read, file).Fields;
        SessionSummonPrototype? facts = fields is null ? null : new(fields.Type, unchecked((int)fields.Flags), unchecked((int)fields.SceneryFlags));
        _summonPrototypes[file] = facts;
        return facts;
    }

    /// <summary>ARC-DEV-352: the cast host summons on the shown map with the module's prototypes and asks the overlay host whether an eye candy plays (its callbacks).</summary>
    private void WireSummon(SessionSpellCaster caster)
    {
        caster.SummonPrototypes = SummonPrototypeOf;
        caster.SummonMap = new SessionMapId(CurrentAreaMap);
        caster.EyeCandyPlays = index => EyeCandyHostNow()?.Plays(index) ?? false;
        // ARC-RENDER-1990: a weapon's own hit effect is added at once on the struck critter (the add's answer vetoes the blood and the hit effect that follow).
        caster.WeaponEyeCandyAdd = (victim, entry, limit) => EyeCandyHostNow()?.Play(victim, entry, 0, -1, null, limit) ?? false;
        caster.CritterSummoned = CritterSummonedHere;
    }

    // The creation notices and inventories queued for the summoned critters, for the smokes.
    private readonly List<string> _summonedCrittersForSelfTest = [];

    /// <summary>
    /// ARC-MAGIC-053 (E1-e, SUM5, TG9): a critter the Summon component made on the shown map is handed to the resolver and the AI host at once (the record's next components, the ObjFlag and EyeCandy lines on
    /// <c>Tgt_Summoned</c>, find it by identity), its inventory is queued (0x442260 runs the restock for every created object) and its tile notification is queued as every creation's is: the creation finish ends with the location
    /// notification for a type 15 or 16 object (ARC-DEV-402, <see cref="QueueCreationNotice"/>), so the script of the tile it stands on runs with it as the triggerer. Without an AI host the resolver alone is told (the
    /// AI host reads the session's critters when it is built).
    /// </summary>
    private void CritterSummonedHere(string entity)
    {
        if (_areaSession is not { } owner || owner.QueryCreatedObject(entity) is not { } made || made.Map != new SessionMapId(CurrentAreaMap)) return;
        if (AreaAiHostNow() is { } ai) SyncCreatedCritters(owner, ai);
        else if (_createdPrototypeRead is { } read && _inventoryHost is { } inventory) inventory.Resolver.SetCreated(CreatedCrittersOf(owner, read).Maps(new SessionMapId(CurrentAreaMap)));
        QueueCreatedInventory(entity, beforeLeader: true);
        QueueCreationNotice(entity, made.Cell);
        _summonedCrittersForSelfTest.Add(entity);
        if (_summonedCrittersForSelfTest.Count > 64) _summonedCrittersForSelfTest.RemoveAt(0);
        GD.Print($"ARC-MAGIC-053 summoned-critter entity={entity} prototype={made.Prototype} cell={made.Cell.X},{made.Cell.Y} summoner={made.Summoner ?? "-"} run={made.Run?.ToString() ?? "-"}");
    }

    /// <summary>ARC-DEV-352 (EC11): an add the cast host was told would play (so its run waits) that the overlay host refused after all calls back at once, as one that does not play.</summary>
    private void EyeCandyNotPlayed(int index, int run, uint play)
    {
        if ((play & (SessionSpellCaster.PlayCallback | SessionSpellCaster.PlayEndCallback)) == 0 || _spellCaster is null || EyeCandyHostNow()?.Plays(index) != true) return;
        _eyeCandyCallbacksNow.Add((run, (play & SessionSpellCaster.PlayCallback) != 0 ? 3 : 4));
    }

    /// <summary>ARC-DEV-352 (EC11, EC14): per frame, the eye-candy callbacks reached since the last frame call their runs back (the CALLBACK or END_CALLBACK action), in order; the log follows.</summary>
    private void ProcessEyeCandyCallbacks()
    {
        if (ShownEyeCandyHost is { Callbacks.Count: > 0 } shown)
        {
            _eyeCandyCallbacksNow.AddRange(shown.Callbacks);
            shown.Callbacks.Clear();
        }
        var calls = _eyeCandyCallbacksNow.ToArray();
        _eyeCandyCallbacksNow.Clear();
        if (_spellCaster is not { } caster || !ReferenceEquals(caster.Owner, _areaSession) || _areaSession is not { } session) return;
        var acted = false;
        foreach (var (run, phase) in calls)
        {
            var result = caster.EyeCandyCallback(run, phase);
            acted |= result is not null;
            GD.Print($"ARC-DEV-352 eye-candy-callback run={run} phase={phase} acted={result?.Disposition.ToString() ?? "no"}");
        }
        // A run that can only be waiting for a callback no overlay run will give (its runs were given up with a map or window, or cut from a save) is called back after two frames without one.
        var host = ShownEyeCandyHost;
        foreach (var run in session.QuerySpellRuns().Runs.Select(r => r.Id).ToArray())
        {
            if (caster.AwaitedCallback(run) is not { } phase || host?.AwaitsCallback(run) == true) { _orphanCallbacks.Remove(run); continue; }
            var frames = _orphanCallbacks[run] = _orphanCallbacks.GetValueOrDefault(run) + 1;
            if (frames < 2) continue;
            _orphanCallbacks.Remove(run);
            var result = caster.EyeCandyCallback(run, phase);
            acted |= result is not null;
            GD.Print($"ARC-DEV-352 eye-candy-orphan run={run} phase={phase} acted={result?.Disposition.ToString() ?? "no"}");
        }
        foreach (var gone in _orphanCallbacks.Keys.Where(run => session.QuerySpellRuns()[run] is null).ToArray()) _orphanCallbacks.Remove(gone);
        if (acted) FlushSpellLog(null);
    }

    // The runs seen waiting for a callback with no overlay run to give it, and for how many frames.
    private readonly Dictionary<int, int> _orphanCallbacks = [];

    /// <summary>ARC-DEV-352: a summoned object a Destroy line removed loses its loop and its overlay runs at once (the destroy 0x43CCA0 takes the object off the map).</summary>
    private void TakeSummonEvent(SpellCastEvent entry)
    {
        // A freed run's overlay runs call nobody back (the next run in its slot is not theirs).
        if (entry.Kind == SpellCastEventKind.Freed) { ShownEyeCandyHost?.ForgetRun(entry.Run); return; }
        if (entry.Kind != SpellCastEventKind.SummonDestroyed || entry.Object is not { } entity || _areaSession is not { } session) return;
        ShownSceneryHost?.RemoveCreated(entity);
        session.KillGoals(entity);
        GD.Print($"ARC-DEV-352 summon-destroyed entity={entity} run={entry.Run}");
    }

    /// <summary>
    /// ARC-DEV-352 (GT77, SUM4): the created sceneries of the shown map that animate get their loops on the scenery host the first time it sees them (the summoner as the burn's source only on the object's
    /// first loop: a loop started again, a window shown again or a reload, has none, as the original's restarted run), and those the session no longer holds lose theirs. Returns whether a loop came or went.
    /// </summary>
    private bool SyncCreatedSceneryLoops(GameSessionOwner session, AreaSceneryHost host)
    {
        if (!ReferenceEquals(_createdLoopSession, session)) { _createdLoopSession = session; _createdLoopSeen.Clear(); _createdLoopNone.Clear(); }
        var changed = false;
        var made = session.QueryCreatedObjects(new SessionMapId(CurrentAreaMap));
        foreach (var gone in host.CreatedLoops.Where(entity => made.All(m => m.Entity != entity)).ToArray()) changed |= host.RemoveCreated(gone);
        foreach (var scenery in made)
        {
            if (scenery.Kind != SessionCreatedKind.Scenery || host.Owns(scenery.Entity) || _createdLoopNone.Contains(scenery.Entity)) continue;
            // The module's content opens after the map: an object is tried again until it reads (only a prototype that reads and gives no loop is not asked again).
            if (ExtraArt() is null || _createdPrototypeRead is null) continue;
            if (CreatedPrototype(scenery.Prototype) is not { } facts || SummonPrototypeOf(scenery.Prototype) is not { } prototype || ExtraArt()?.Load(facts.Art).Frame is not { } frame)
            { _createdLoopNone.Add(scenery.Entity); continue; }
            var first = _createdLoopSeen.Add(scenery.Entity);
            var added = host.AddCreated(scenery.Entity, scenery.Cell, frame.FramesPerGroup, frame.FramesPerSecond, facts.Flags, unchecked((uint)prototype.SceneryFlags), scenery.SpellFlags,
                first ? scenery.Summoner : null);
            if (!added) _createdLoopNone.Add(scenery.Entity);
            changed |= added;
            GD.Print($"ARC-DEV-352 created-loop entity={scenery.Entity} prototype={scenery.Prototype} frames={frame.FramesPerGroup} fps={frame.FramesPerSecond} burning={(prototype.SceneryFlags & 0x10) != 0} "
                + $"source={(first ? scenery.Summoner ?? "-" : "-")} loop={added} run={session.QueryGoals().MainRunOf(scenery.Entity)?.Top.Goal.ToString() ?? "-"}");
        }
        return changed;
    }

    /// <summary>ARC-DEV-352: the art of a created scenery at its loop's frame (the frame bits 14..18 of a scenery art), or its prototype's art when it has no loop.</summary>
    private uint CreatedArtNow(string entity, uint art) =>
        ShownSceneryHost?.CreatedFrame(entity) is { } frame and >= 0 and < 32 ? (art & ~(31U << 14)) | ((uint)frame << 14) : art;

    /// <summary>ARC-DEV-352: the cell of a created object on the shown map (an eye candy's owner), or null.</summary>
    private CellCoord? CreatedCell(GameSessionOwner owner, string entity) =>
        SessionCreatedObject.KindOf(entity) is SessionCreatedKind.Scenery or SessionCreatedKind.EyeCandy && owner.QueryCreatedObject(entity) is { } made && made.Map == new SessionMapId(CurrentAreaMap) ? made.Cell : null;
}
