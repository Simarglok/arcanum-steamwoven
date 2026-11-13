using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Animation;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-142 (MIG13 S9a, docs/architecture/goal-table.md §3.5, the S9 read GT75–GT78): the host of the shown map's animated scenery as owners of goal runs. A scenery whose loop the start rule admits
/// (<see cref="AreaSceneryAnimation"/>) owns a run of goal 1 <c>ag_animate_loop</c>, or of goal 77 for a burning scenery (scenery flag 0x10), started when the map is shown (the original starts it when the object
/// enters the object list, 0x433580). The run is the loop: its begin stores the frame delay, then it looks at the distance (every third look measures it again) and steps one frame per delay while the local player
/// character is within 30 tiles, or sleeps 800 ms when it is away. The runs step on the animation clock inside the frame step (<see cref="IGoalStepHost"/>), so the loops stop with the Esc pause and take a round's
/// time jump; a step that moves a frame marks the host (<see cref="TakeChanged"/>) and the viewer redraws the scenery's body. This host answers the scenery ports (<see cref="IAiSceneryWorld"/>) from the loops.
/// ARC-DEV-144 (S9c): goal 77's fire damage burns the critters on the scenery's tile through <see cref="AreaFireBurner"/> (3 fire, once per round and doubled in a round).
/// <b>Not ready:</b> the loop's ambient sound of a one-frame ART and of a created scenery (the begin node's sound of the map's own loops is <see cref="TakeSoundBegins"/>, ARC-RENDER-1126), the restart of a loop after a cleared off flag (the original's restart sites 0x43F710, GT78: the viewer changes none at run time; <see cref="Restart"/> is
/// the rule for it; an art change, ARC-RENDER-1781, goes through <see cref="Reframe"/>), and a loop whose object is stoned later (the spell flag is read at the start only). A loop whose run a table-exhaustion sweep ended stays still until its sector is shown again, as in the original (GT98).
/// ARC-DEV-352 (E1-b, SUM4, GT77): a scenery the session created at run time (a Summon's piece, <see cref="SessionCreatedObject"/>) gets its loop through <see cref="AddCreated"/> when the viewer first shows it (the original starts the
/// main run when the object enters the object list) and loses it through <see cref="RemoveCreated"/> when it is destroyed. Its burn carries the summoner as the source (the run's bottom frame slot 4, SUM4): the summoner and the NPCs led by a PC
/// summoner are spared. The source is the host's, not the saved run's; the viewer gives it on the first loop of the object in a session (after a load too: the original saves the run with its slot) and not to a loop it starts again
/// on a new host (a window shown again, the sector reload), and <see cref="Start"/> drops it from a loop that ran before (a sweep's restart), as the original's restarted run loses it.
/// </summary>
public sealed class AreaSceneryHost : IAiSceneryWorld, IGoalStepHost
{
    private readonly GameSessionOwner _session;
    private readonly AreaSceneryAnimation _animation;
    private readonly Func<CellCoord?> _player;
    private readonly Dictionary<string, AreaSceneryLoop> _byEntity;
    private readonly StartPorts _startPorts;
    private readonly AreaFireBurner? _fire;
    // ARC-DEV-352: the created sceneries' loops, by entity, with whether they burn and the burn's source.
    private readonly Dictionary<string, (SceneryLoop Loop, bool Burning, string? Source, bool Begun)> _created = new(StringComparer.Ordinal);
    // ARC-RENDER-1781: the object offset of a scenery whose loop was killed by an art change (the object keeps the offset its frame deltas moved it by, AS6); a loop that takes over later starts from it.
    private readonly Dictionary<string, (int X, int Y)> _resting = new(StringComparer.Ordinal);
    private bool _changed, _createdChanged;
    // ARC-RENDER-1126: the map's loops whose begin node ran since the last take (the loop sound's decision point).
    private readonly List<string> _soundBegins = [];

    /// <param name="player">The local player character's tile (the distance test of the loop), or null when it has none.</param>
    /// <param name="fire">The burn of goal 77's fire damage (GT94), or null when nothing burns (the damage is counted only).</param>
    public AreaSceneryHost(GameSessionOwner session, AreaSceneryAnimation animation, Func<CellCoord?> player, AreaFireBurner? fire = null)
    {
        _fire = fire;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _animation = animation ?? throw new ArgumentNullException(nameof(animation));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _byEntity = new(StringComparer.Ordinal);
        foreach (var loop in animation.Loops) _byEntity[loop.Entity] = loop;
        _startPorts = new();
    }

    /// <summary>The session this host's loops belong to.</summary>
    public GameSessionOwner Session => _session;

    /// <summary>The loops this host animates.</summary>
    public AreaSceneryAnimation Animation => _animation;

    /// <summary>The runs this host stepped (a test and the diagnostic line read it).</summary>
    public int Steps { get; private set; }

    /// <summary>The loop frames stepped so far.</summary>
    public long FramesStepped { get; private set; }

    /// <summary>Goal 77's fire-damage steps reached (<see cref="AreaFireBurner"/> does the damage).</summary>
    public long FireFrames { get; private set; }

    /// <summary>The runs the table refused to start (a full table).</summary>
    public int Refused { get; private set; }

    /// <summary>True when a start found the table held by a pass in progress: <see cref="Start"/> is to be called again on a later frame (the loops that started stay, the others are tried again).</summary>
    public bool StartPending { get; private set; }

    /// <summary>The loops whose run is in the table now.</summary>
    public int Running => _byEntity.Keys.Count(entity => _session.QueryGoals().MainRunOf(entity) is not null);

    /// <summary>Whether <paramref name="entity"/> is an animated scenery of this host (a created one included).</summary>
    public bool Owns(string entity) => _byEntity.ContainsKey(entity) || _created.ContainsKey(entity);

    /// <summary>ARC-DEV-352: the created sceneries this host loops.</summary>
    public IReadOnlyCollection<string> CreatedLoops => _created.Keys;

    /// <summary>ARC-DEV-352: the frame a created scenery's loop shows, or null when it has no loop here.</summary>
    public int? CreatedFrame(string entity) => _created.TryGetValue(entity, out var made) ? made.Loop.Frame : null;

    /// <summary>ARC-DEV-352: the burn's source of a created scenery's loop (its summoner on the loop's first start), or null.</summary>
    public string? CreatedSource(string entity) => _created.TryGetValue(entity, out var made) ? made.Source : null;

    /// <summary>
    /// ARC-DEV-352 (GT77, SUM4): a scenery created at run time on <paramref name="cell"/> gets its loop when the start rule admits it (<see cref="SceneryAnimationRules.Starts"/> from the prototype's flags, the object's spell
    /// flags and the art's frames), a run of goal 1, or 77 for scenery flag 0x10 (the starter 0x433580), unless it has a main run already (a restored one), with <paramref name="source"/> as the burn's source. The frames do not
    /// move the object (no deltas). False when the rule refuses or the entity has a loop here already; a start the table refused is counted (<see cref="Refused"/>) or, for a pass in progress, tried again by <see cref="Start"/>.
    /// </summary>
    public bool AddCreated(string entity, CellCoord cell, int frames, int framesPerSecond, uint objectFlags, uint sceneryFlags, uint spellFlags, string? source)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (_created.ContainsKey(entity) || _byEntity.ContainsKey(entity)) return false;
        if (!SceneryAnimationRules.Starts(SceneryAnimationRules.SceneryType, objectFlags, sceneryFlags, spellFlags, frames)) return false;
        var burning = (sceneryFlags & AreaSceneryAnimation.BurningFlag) != 0;
        _created[entity] = (new SceneryLoop(cell.X, cell.Y, new (int, int)[frames], 0, SceneryAnimationRules.PauseMs(framesPerSecond)), burning, source, false);
        if (StartOne(entity, burning) || _session.QueryGoals().MainRunOf(entity) is not null) _created[entity] = _created[entity] with { Begun = true };
        _createdChanged = true;
        return true;
    }

    /// <summary>ARC-DEV-352: a created scenery that is gone (destroyed) loses its loop and its run ends at level 6. False when it had none here.</summary>
    public bool RemoveCreated(string entity)
    {
        if (!_created.Remove(entity)) return false;
        foreach (var run in _session.QueryGoals().RunsOf(entity).Where(r => IsLoopRun(r)).ToArray()) _session.InterruptGoal(run.Handle, GoalInterruptRules.MaxLevel);
        _changed = _createdChanged = true;
        return true;
    }

    /// <summary>ARC-DEV-352: true once after a created scenery's loop came, went or moved a frame: its body must be drawn again (the map's own loops are <see cref="TakeChanged"/>'s).</summary>
    public bool TakeCreatedChanged()
    {
        var changed = _createdChanged;
        _createdChanged = false;
        return changed;
    }

    private SceneryLoop? Loop(string entity) => _byEntity.TryGetValue(entity, out var loop) ? loop.Loop : _created.TryGetValue(entity, out var made) ? made.Loop : null;

    private bool StartOne(string entity, bool burning)
    {
        if (_session.QueryGoals().MainRunOf(entity) is not null) return false;
        var result = _session.StartGoal(new GoalStartRequest(entity, GoalStarters.GenericStartsAnyway, AiExecutorRules.SceneryFrame(burning)), _startPorts);
        if (result.Started) return true;
        if (result.Failure == SessionGoalFailure.PassInProgress) StartPending = true; else Refused++;
        return false;
    }

    /// <summary>The loop of a scenery entity, or null.</summary>
    public AreaSceneryLoop? LoopOf(string entity) => _byEntity.GetValueOrDefault(entity);

    /// <summary>The goal a scenery's run is on, or null when it has none.</summary>
    public GoalType? GoalOf(string entity) => _session.QueryGoals().MainRunOf(entity)?.Top.Goal;

    /// <summary>
    /// ARC-RENDER-1126 (0x42DA50): the map's loops whose begin node ran since the last call, in order, each time its run began (a loop that began again after a
    /// restart is listed again: the caller keeps the voice it started, as the original's begin node tests its sound handle). The begin node is where the original
    /// starts the object's looping misc sound; deciding it needs the sound data and the PC, which the host leaves to the caller.
    /// </summary>
    public IReadOnlyList<string> TakeSoundBegins()
    {
        if (_soundBegins.Count == 0) return [];
        var begins = _soundBegins.ToArray();
        _soundBegins.Clear();
        return begins;
    }

    /// <summary>True once after a step moved a frame: the scenery's body must be drawn again.</summary>
    public bool TakeChanged()
    {
        var changed = _changed;
        _changed = false;
        return changed;
    }

    /// <summary>
    /// The starter 0x433580 for every loop: a scenery that has a main run already is left as it is, the others start goal 1 (or 77) through the level-1 plan that starts anyway (GS12: the starter is one of the three whose refused interrupt does not refuse it; it ends nothing, the gate excluded a main run);
    /// the first step comes 5 animation milliseconds later. Returns how many runs started.
    /// </summary>
    [OriginalFunction(0x433580, OriginalFunctionScope.Partial)]
    public int Start()
    {
        var started = 0;
        StartPending = false;
        foreach (var loop in _animation.Loops)
            if (StartOne(loop.Entity, loop.Burning)) started++;
        foreach (var (entity, made) in _created.ToArray())
        {
            if (!StartOne(entity, made.Burning)) continue;
            started++;
            // SUM4: a loop that ran before and is started again (a sweep ended it) has no source any more; the first start keeps it.
            _created[entity] = made with { Source = made.Begun ? null : made.Source, Begun = true };
        }
        return started;
    }

    /// <summary>
    /// ARC-DEV-144 (GT78): the restart 0x43F710 of one of this host's sceneries after the object changed (its art set, its off flag cleared, the object created or put in the list again), decided by
    /// <see cref="SceneryAnimationRules.Restart"/> from the object's present flags (the goal too: 77 for flag 0x10, GT77) and this loop's frames (the frames of a changed art are <see cref="Reframe"/>'s): a scenery that must not animate any more has its run killed (the per-object kill, skipped while this host is stepping a run), one that does animate starts
    /// the loop unless it already has a main run (the starter refuses then). The viewer changes neither art nor flags of a shown scenery at run time yet, so no caller exists; a sector shown again goes through <see cref="Start"/>.
    /// </summary>
    public SceneryRestart Restart(string entity, uint objectFlags, uint sceneryFlags, bool ambientSound = false)
    {
        if (!_byEntity.TryGetValue(entity, out var loop)) return SceneryRestart.Skip;
        var decision = SceneryAnimationRules.Restart(SceneryAnimationRules.SceneryType, objectFlags, sceneryFlags, loop.Frames.Count, ambientSound, _session.QueryGoals().SteppingSlot is not null);
        if (decision == SceneryRestart.Kill) _session.KillGoals(entity);
        else if (decision == SceneryRestart.Start && _session.QueryGoals().MainRunOf(entity) is null && _session.StartGoal(new GoalStartRequest(entity, GoalStarters.GenericStartsAnyway, AiExecutorRules.SceneryFrame((sceneryFlags & AreaSceneryAnimation.BurningFlag) != 0)), _startPorts) is { Started: false } refused)
        {
            if (refused.Failure == SessionGoalFailure.PassInProgress) StartPending = true; else Refused++;
        }
        return decision;
    }

    /// <summary>
    /// ARC-RENDER-1781 (AN1, AN5, AN6, AS1, AS5; exe 0x43EB30, 0x43F710, 0x433580, 0x42DDE0, 0x43F7F0, 0x502BC0, 0x502C40): the scenery <paramref name="row"/> has another art, <paramref name="frames"/> (the new art's frames in frame order, frame 0 shown:
    /// the action sets frame 0 before it writes the id) with the frame rate <paramref name="framesPerSecond"/> of its ART header. The original's step reads the object's <b>current</b> art every time (the frame count from its header, the next frame, the delta), so the
    /// loop goes on over the new frames from frame 0 and the offset the object has; the art writer then runs the restart 0x43F710 (<see cref="SceneryAnimationRules.Restart"/>): an art of one frame (no ambient sound) kills the loop's run, an art of several frames starts the loop
    /// unless the object has a main run already (the starter refuses then), so a running loop keeps its run and the frame delay its begin node stored for the old art, and a scenery with no run gets a new one whose delay is the new art's (1000 / its rate).
    /// An object with its own loop or none, a map's or a changed one's, is handled alike; the object's place is the row's. Returns what the restart did.
    /// </summary>
    public SceneryRestart Reframe(AreaObjectRecord row, IReadOnlyList<AreaVisualRecord> frames, int framesPerSecond, bool ambientSound = false)
    {
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0 || row.WorldX is not { } x || row.WorldY is not { } y) return SceneryRestart.Skip;
        var entity = AreaUseScriptHost.EntityId(row);
        var objectFlags = AreaSceneryAnimation.ObjectFlags(row);
        var sceneryFlags = AreaSceneryAnimation.SceneryFlags(row);
        var decision = SceneryAnimationRules.Restart(SceneryAnimationRules.SceneryType, objectFlags, sceneryFlags, frames.Count, ambientSound, _session.QueryGoals().SteppingSlot is not null);
        var had = _byEntity.TryGetValue(entity, out var old);
        var offset = had ? (old!.Loop.OffsetX, old.Loop.OffsetY) : _resting.GetValueOrDefault(entity);
        if (decision == SceneryRestart.Kill)
        {
            _session.KillGoals(entity);
            if (_byEntity.Remove(entity)) { _animation.Remove(entity); _resting[entity] = offset; }
            _changed = true;
        }
        else if (decision == SceneryRestart.Start && frames.Count >= 2)
        {
            var running = _session.QueryGoals().MainRunOf(entity) is not null;
            // The run that exists keeps its stored delay (the begin node read it from the art the run was started with); a run started now reads the new art.
            var pause = had && running ? old!.Loop.PauseMs : SceneryAnimationRules.PauseMs(framesPerSecond);
            var burning = (sceneryFlags & AreaSceneryAnimation.BurningFlag) != 0;
            var loop = new AreaSceneryLoop(row, frames, new SceneryLoop(x, y, frames.Select(f => (f.ArtOffsetX, f.ArtOffsetY)).ToArray(), 0, pause, offset.Item1, offset.Item2), burning);
            _byEntity[entity] = loop; _animation.Replace(loop); _resting.Remove(entity);
            if (!running) StartOne(entity, burning);
            _changed = true;
        }
        return decision;
    }

    /// <summary>ARC-RENDER-1781: the frame and the object offset the scenery shows by its loop, or by the offset its killed loop left (frame 0); null when the host never knew a loop of it.</summary>
    public (int Frame, int OffsetX, int OffsetY)? Pose(string entity)
    {
        if (_byEntity.TryGetValue(entity, out var loop)) return (loop.Loop.Frame, loop.Loop.OffsetX, loop.Loop.OffsetY);
        return _resting.TryGetValue(entity, out var rest) ? (0, rest.X, rest.Y) : null;
    }

    /// <summary>The loops are given up (a window move, a quick load, a replaced session, the map shown elsewhere): their runs end at level 6 and the sceneries stay on the frame they show.</summary>
    public void LoseAll()
    {
        foreach (var entity in _byEntity.Keys.Concat(_created.Keys))
        {
            try { foreach (var run in _session.QueryGoals().RunsOf(entity).Where(r => IsLoopRun(r)).ToArray()) _session.InterruptGoal(run.Handle, GoalInterruptRules.MaxLevel); }
            catch (ObjectDisposedException) { /* a replaced session: nothing of it is left to end */ }
        }
    }

    /// <inheritdoc />
    public SessionGoalStepAnswer StepGoal(GoalRun run, FrameClocks clocks)
    {
        ArgumentNullException.ThrowIfNull(run);
        // A scenery of another window of the map (the view moved on) has no loop here: its run ends and the object stays on the frame it shows.
        if (!Owns(run.Owner)) return SessionGoalStepAnswer.End;
        Steps++;
        var stepped = GoalStepper.StepScenery(run, this);
        return !_session.QueryGoals().TryGet(run.Handle, out var now) || now.Done ? SessionGoalStepAnswer.End : stepped.Answer;
    }

    private static bool IsLoopRun(GoalRun run) => AiExecutorRules.IsSceneryRunGoal(run.Bottom.Goal);

    // ---- the scenery ports ----

    public int? SceneryBegin(string scenery)
    {
        if (Loop(scenery) is not { } loop) return null;
        // ARC-RENDER-1126: the begin node starts the object's looping sound (0x42DA50, SceneryLoopSoundRules): the viewer takes the begins and decides it.
        if (_byEntity.ContainsKey(scenery) && !_soundBegins.Contains(scenery)) _soundBegins.Add(scenery);
        return (int)Math.Min(loop.PauseMs, int.MaxValue);
    }

    // The distance test (0x441AE0): a player character that is not on the map is far away.
    public bool SceneryNear(string scenery) => Loop(scenery) is { } loop && _player() is { } pc && SceneryAnimationRules.Near(loop.X, loop.Y, pc);

    public bool SceneryAdvance(string scenery)
    {
        if (Loop(scenery) is not { } loop) return false;
        var frame = loop.Step();
        FramesStepped++;
        _changed = true;
        if (_created.ContainsKey(scenery)) _createdChanged = true;
        return frame % 3 != 1;
    }

    // Goal 77's burn (0x42A720): the critters on the scenery's own tile (the scenery is no victim: its type is outside the mask).
    public int? FireRound() => _fire?.Round;

    public int Burn(string scenery, bool normal, bool doubled)
    {
        FireFrames++;
        if (_fire is null || Loop(scenery) is not { } loop) return 0;
        // ARC-DEV-352 (SUM4): a created piece burns with its summoner as the source.
        return _fire.Burn(new CellCoord(checked((int)loop.X), checked((int)loop.Y)), normal, doubled, scenery, CreatedSource(scenery));
    }

    // The table's start ports: the starter 0x433580 passes no gate (a main run excludes it before the start), nothing loads, and the loops are built for objects that are not destroyed or off.
    private sealed class StartPorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }
}
