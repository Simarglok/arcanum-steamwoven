using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Animation;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-142 (MIG13 S9a, docs/architecture/goal-table.md §3.5, the S9 read GT75–GT78): the host of the shown map's animated scenery as owners of goal runs. A scenery whose loop the start rule admits
/// (<see cref="AreaSceneryAnimation"/>) owns a run of goal 1 <c>ag_animate_loop</c>, or of goal 77 for a burning scenery (scenery flag 0x10), started when the map is shown (the original starts it when the object
/// enters the object list, 0x433580). The run is the loop: its begin stores the frame delay, then it looks at the distance (every third look measures it again) and steps one frame per delay while the local player
/// character is within 30 tiles, or sleeps 800 ms when it is away. The runs step on the animation clock inside the frame step (<see cref="IGoalStepHost"/>), so the loops stop with the Esc pause and take a round's
/// time jump; a step that moves a frame marks the host (<see cref="TakeChanged"/>) and the viewer redraws the scenery's body. This host answers the scenery ports (<see cref="IAiSceneryWorld"/>) from the loops.
/// <b>Not ready:</b> goal 77's fire damage to the items and critters on the tile (the port is a counted no-op, <see cref="FireFrames"/>), the loop's ambient sound, the restart of a loop after a table-exhaustion sweep or
/// an art change (the original's restart sites 0x43F710, GT78), and a loop whose object is stoned later (the spell flag is read at the start only).
/// </summary>
public sealed class AreaSceneryHost : IAiSceneryWorld, IGoalStepHost
{
    private readonly GameSessionOwner _session;
    private readonly AreaSceneryAnimation _animation;
    private readonly Func<CellCoord?> _player;
    private readonly Dictionary<string, AreaSceneryLoop> _byEntity;
    private readonly StartPorts _startPorts;
    private bool _changed;

    /// <param name="player">The local player character's tile (the distance test of the loop), or null when it has none.</param>
    public AreaSceneryHost(GameSessionOwner session, AreaSceneryAnimation animation, Func<CellCoord?> player)
    {
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

    /// <summary>Goal 77's fire-damage steps reached (the damage itself is not ready).</summary>
    public long FireFrames { get; private set; }

    /// <summary>The runs the table refused to start (a full table).</summary>
    public int Refused { get; private set; }

    /// <summary>The loops whose run is in the table now.</summary>
    public int Running => _byEntity.Keys.Count(entity => _session.QueryGoals().MainRunOf(entity) is not null);

    /// <summary>Whether <paramref name="entity"/> is an animated scenery of this host.</summary>
    public bool Owns(string entity) => _byEntity.ContainsKey(entity);

    /// <summary>The loop of a scenery entity, or null.</summary>
    public AreaSceneryLoop? LoopOf(string entity) => _byEntity.GetValueOrDefault(entity);

    /// <summary>The goal a scenery's run is on, or null when it has none.</summary>
    public GoalType? GoalOf(string entity) => _session.QueryGoals().MainRunOf(entity)?.Top.Goal;

    /// <summary>True once after a step moved a frame: the scenery's body must be drawn again.</summary>
    public bool TakeChanged()
    {
        var changed = _changed;
        _changed = false;
        return changed;
    }

    /// <summary>
    /// The starter 0x433580 for every loop: a scenery that has a main run already is left as it is, the others start goal 1 (or 77) through the level-1 plan (it ends nothing, the gate excluded a main run);
    /// the first step comes 5 animation milliseconds later. Returns how many runs started.
    /// </summary>
    public int Start()
    {
        var started = 0;
        foreach (var loop in _animation.Loops)
        {
            if (_session.QueryGoals().MainRunOf(loop.Entity) is not null) continue;
            var result = _session.StartGoal(new GoalStartRequest(loop.Entity, GoalStarters.Generic, AiExecutorRules.SceneryFrame(loop.Burning)), _startPorts);
            if (result.Started) started++; else Refused++;
        }
        return started;
    }

    /// <summary>The loops are given up (a window move, a quick load, a replaced session, the map shown elsewhere): their runs end at level 6 and the sceneries stay on the frame they show.</summary>
    public void LoseAll()
    {
        foreach (var entity in _byEntity.Keys)
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
        if (!_byEntity.ContainsKey(run.Owner)) return SessionGoalStepAnswer.End;
        Steps++;
        var stepped = GoalStepper.StepScenery(run, this);
        return !_session.QueryGoals().TryGet(run.Handle, out var now) || now.Done ? SessionGoalStepAnswer.End : stepped.Answer;
    }

    private static bool IsLoopRun(GoalRun run) => AiExecutorRules.IsSceneryRunGoal(run.Bottom.Goal);

    // ---- the scenery ports ----

    public int? SceneryBegin(string scenery) => _byEntity.TryGetValue(scenery, out var loop) ? (int)Math.Min(loop.Loop.PauseMs, int.MaxValue) : null;

    // The distance test (0x441AE0): a player character that is not on the map is far away.
    public bool SceneryNear(string scenery) => _byEntity.TryGetValue(scenery, out var loop) && _player() is { } pc && SceneryAnimationRules.Near(loop.Loop.X, loop.Loop.Y, pc);

    public bool SceneryAdvance(string scenery)
    {
        if (!_byEntity.TryGetValue(scenery, out var loop)) return false;
        var frame = loop.Loop.Step();
        FramesStepped++;
        _changed = true;
        return frame % 3 != 1;
    }

    public bool SceneryFireDamage(string scenery)
    {
        FireFrames++;
        return true;
    }

    // The table's start ports: the starter 0x433580 passes no gate (a main run excludes it before the start), nothing loads, and the loops are built for objects that are not destroyed or off.
    private sealed class StartPorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }
}
