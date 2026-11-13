using Arcanum.Data.Area;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-111: the critters that walk off their T0 cell (the companions following the PC, the NPCs the AI walks) are drawn with
// their own walk or run cycle and facing, as the original draws every walker (step animation 0x42EDC0), not as their T0 frame slid
// across the map. The move ARTs are read on demand from the body's archive, once per standing art (AreaBodyMoveArt), and their frames
// join the shown map's frame overlay as the fallen poses do; the move ARTs and the NPCs' gait clocks are dropped with that overlay (a map
// change or a reload), a companion's clock lives with its group. A body whose move ARTs cannot be read keeps its T0 frame and says why once.
// ARC-RENDER-038: an NPC the AI host moved plays its attack ART while its attack animation runs (the AI host's play: the animation
// and weapon of WeaponArtRules, ledger WA1/WA4) and in combat mode stands with its weapon (WA1); those armed ARTs are read the same way
// (AreaBodyMoveArt.LoadArmed), once per body, animation, weapon and shield.
public partial class TerrainViewerApp
{
    private readonly Dictionary<(AreaEntryIdentity Entry, int Palette), AreaBodyMoves> _bodyMoves = [];
    private readonly Dictionary<AreaObjectKey, AreaBodyCycle> _bodyCycles = [];
    // Whether the last AI mover drawing showed a body stepping (its stand pose is drawn once more after the step), and the movers'
    // version it drew (a turn in place is drawn too).
    private bool _aiMoversStepping;
    private long _aiMoversDrawn = -1;
    /// <summary>Smoke observation: the moved bodies drawn with a walk or run frame, and the distinct frames drawn so.</summary>
    private int _bodyWalkDraws;
    private readonly HashSet<AreaFrameKey> _bodyWalkFrames = [];
    // ARC-RENDER-038: the armed ARTs read so far, and (smoke observation) the NPC attack frames and armed stands drawn.
    private readonly Dictionary<(AreaEntryIdentity Entry, int Palette, int Animation, int Weapon, int Shield), AreaBodyArmedLoad> _armedArts = [];
    private int _npcAttackDraws, _npcArmedStandDraws, _npcArmedWalkDraws;
    private readonly HashSet<AreaFrameKey> _npcAttackFrames = [];
    // ARC-RENDER-038: what the last AI mover drawing showed armed (a weapon shown or put away is drawn once).
    private long _aiArmedDrawn;

    /// <summary>A load's frames as drawn: the shown map's overlay (with the move and fallen poses) for the shown map.</summary>
    private IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> AreaFramesOf(AreaLoadResult area) =>
        ReferenceEquals(area, _readyArea) ? AreaFrames() : area.Frames;

    /// <summary>The companions' bodies where they stand, each with the frame of its gait and facing.</summary>
    private IReadOnlyList<AreaDrawCommand> AnimateCompanions(AreaCompanionGroup group, IReadOnlyList<AreaDrawCommand> commands)
    {
        var now = WorldMilliseconds();
        var drawn = new List<AreaDrawCommand>(commands.Count);
        foreach (var command in commands)
        {
            if (group.Companions.FirstOrDefault(c => c.Key == command.Identity) is not { } companion
                || !companion.Template.Visuals.TryGetValue(command.Visual, out var visual)) { drawn.Add(command); continue; }
            var state = DiagnosticGridActor.RenderState(companion.Grid.State);
            var companionGrid = companion.Grid;
            drawn.Add(AnimateBody(command, companion.Speaker.Row, visual, state.Action, state.Facing, now, companion.Cycle, keepT0Until: true,
                gait: (k, r, a, f) => NoteGaitFrame(k, r, a, f, WorldCellCoord(companionGrid))));
        }
        return drawn.AsReadOnly();
    }

    /// <summary>The AI's moved NPCs' bodies (not the fallen ones) with the frame of their step and facing.</summary>
    private IReadOnlyList<AreaDrawCommand> AnimateAiMovers(AreaAiHost host, IReadOnlyList<AreaDrawCommand> commands, long now)
    {
        if (_areaOverlay is null) return commands;
        var drawn = new List<AreaDrawCommand>(commands.Count);
        foreach (var command in commands)
        {
            // ARC-DEV-370: a created critter's visual is not in the map's own (TryBodyVisual).
            if (command.Identity is not { } key || _fallenBodies.ContainsKey(key) || host.Scene.Npcs.FirstOrDefault(n => n.Row.Key == key) is not { } npc
                || host.Movers.Of(npc.Entity) is not { } mover || !TryBodyVisual(command.Visual, out var visual)) { drawn.Add(command); continue; }
            var action = mover.Stepping(now) ? mover.Running ? ActorDiagnosticAction.Run : ActorDiagnosticAction.Walk : ActorDiagnosticAction.Idle;
            if (!_bodyCycles.TryGetValue(key, out var cycle)) _bodyCycles[key] = cycle = new AreaBodyCycle();
            var octant = DiagnosticGridActor.Octant(mover.Facing);
            if (action == ActorDiagnosticAction.Idle && ArmedBody(host, npc, command, visual, octant, now) is { } armed) { cycle.Observe(action, now); drawn.Add(armed); continue; }
            var cell = mover.Cell;
            drawn.Add(AnimateBody(command, npc.Row, visual, action, octant, now, cycle, keepT0Until: false, gait: (k, r, a, f) => NoteGaitFrame(k, r, a, f, cell),
                walkArt: action == ActorDiagnosticAction.Walk ? ArmedWalk(host, npc, visual) : null));
        }
        return drawn.AsReadOnly();
    }

    /// <summary>Whether an AI mover is stepping now, was at the last drawing (so its stand pose is drawn once), or the movers changed
    /// since (a turn in place).</summary>
    private bool AiMoversAnimating(AreaAiHost host, long now)
    {
        // ARC-RENDER-038: an attack animation plays like a step; a weapon shown or put away (combat mode) is drawn once.
        var stepping = host.Movers.All.Any(m => m.Stepping(now) || host.AttackPose(m.Entity, now) is { Play.Art: not null });
        var armed = host.Movers.All.Aggregate(17L, (hash, m) => hash * 31 + host.ShownWeapon(m.Entity).GetHashCode());
        var animating = stepping || _aiMoversStepping || host.Movers.Version != _aiMoversDrawn || armed != _aiArmedDrawn;
        _aiMoversStepping = stepping; _aiMoversDrawn = host.Movers.Version; _aiArmedDrawn = armed;
        return animating;
    }

    /// <summary>
    /// ARC-RENDER-038: an NPC standing where the AI moved it, drawn armed: the frame of its attack ART while its attack animation plays
    /// (the ART the host timed the attack with), else in combat mode its stand with the weapon and shield it shows (frame 0). Null when
    /// neither applies or the ART is not read (the stand then is its move stand).
    /// </summary>
    private AreaDrawCommand? ArmedBody(AreaAiHost host, AreaAiNpc npc, AreaDrawCommand command, AreaVisualRecord visual, int octant, long now)
    {
        if (_readyArea is not { } area) return null;
        AreaBodyArmedLoad load; int frame; bool attack;
        if (host.AttackPose(npc.Entity, now) is { Play.Art: { } art } pose)
        {
            load = ArmedArt(area, visual, npc, art.Animation, art.Weapon, art.Shield);
            frame = pose.Frame; attack = true;
        }
        else if (host.ShownWeapon(npc.Entity) is { Weapon: > 0 } shown)
        {
            load = ArmedArt(area, visual, npc, AreaBodyMoveArt.Stand, shown.Weapon, shown.Shield);
            frame = 0; attack = false;
        }
        else return null;
        if (load.Art is not { } read) return null;
        try
        {
            if (AreaBodyMoveArt.Pose(command, npc.Row, visual, read, octant, frame) is not { } posed) return null;
            ((AreaFrameOverlay)AreaFrames()).Add(posed.Pose.Key, posed.Pose.Frame);
            if (attack) { _npcAttackDraws++; _npcAttackFrames.Add(posed.Pose.Key); } else _npcArmedStandDraws++;
            return posed.Command;
        }
        catch (Exception error) when (error is Arcanum.Data.ArtException or ArgumentException or InvalidOperationException)
        {
            // An ART that decodes no frame for this facing is not drawn again.
            var refused = load with { Status = AreaBodyMoveStatus.Unreadable, Art = null, Detail = error.Message };
            foreach (var known in _armedArts.Where(entry => ReferenceEquals(entry.Value, load)).Select(entry => entry.Key).ToArray()) _armedArts[known] = refused;
            GD.Print($"ARC-RENDER-038 npc-armed-art npc={npc.Entity} status=Unreadable detail={error.GetType().Name} {error.Message}");
            return null;
        }
    }

    /// <summary>
    /// ARC-RENDER-1485: the walk ART of an NPC the AI walks, with the weapon part its body shows now (<see cref="AreaAiHost.ShownWeapon"/>, 0x465020: the wielded weapon in combat mode, none out of
    /// it; the walk keeps the weapon and clears the shield, MA2) in place of the weapon letter of its map art. Null when the ART with that very weapon cannot be read (the walk of its map art is drawn then).
    /// </summary>
    private AreaBodyArt? ArmedWalk(AreaAiHost host, AreaAiNpc npc, AreaVisualRecord visual)
    {
        if (_readyArea is not { } area) return null;
        var weapon = host.ShownWeapon(npc.Entity).Weapon;
        var load = ArmedArt(area, visual, npc, AreaBodyMoveArt.Walk, weapon, 0);
        if (load is not { Art: { } art, Animation: AreaBodyMoveArt.Walk } || load.Weapon != weapon) return null;
        _npcArmedWalkDraws++;
        return art;
    }

    /// <summary>ARC-RENDER-038: the body's armed ART for an animation, weapon part and shield (read once; the load names the file the
    /// repair chose, which is kept under that key too).</summary>
    private AreaBodyArmedLoad ArmedArt(AreaLoadResult area, AreaVisualRecord visual, AreaAiNpc npc, int animation, int weapon, int shield)
    {
        var key = (visual.Entry, visual.RequestedPalette, animation, weapon, shield);
        if (_armedArts.TryGetValue(key, out var load)) return load;
        load = AreaBodyMoveArt.LoadArmed(visual, area.ArtSources.GetValueOrDefault(visual.Entry.Source), animation, weapon, shield);
        _armedArts[key] = load;
        if (load.Art is not null) _armedArts.TryAdd((visual.Entry, visual.RequestedPalette, load.Animation, load.Weapon, load.Shield), load);
        GD.Print($"ARC-RENDER-038 npc-armed-art npc={npc.Entity} animation={animation} weapon={weapon} shield={shield} status={load.Status}"
            + $" read={load.Animation}/{load.Weapon}/{load.Shield} detail={load.Detail}");
        return load;
    }

    /// <summary>ARC-RENDER-038: the AI host's attack ART of an NPC's body (<see cref="AreaAiScene.AttackArt"/>), or null when the shown
    /// map's overlay does not hold its body or the ART does not read.</summary>
    private AreaAiAttackArt? NpcAttackArt(AreaAiNpc npc, AreaAiAttackArtRequest wanted)
    {
        if (_readyArea is not { } area) return null;
        AreaFrames();
        if (NpcT0Body(npc.Row.Key) is not { } command || !TryBodyVisual(command.Visual, out var visual)) return null;
        var load = ArmedArt(area, visual, npc, wanted.Animation, wanted.Weapon, wanted.Shield);
        return load.Art is { } art ? new(load.Animation, load.Weapon, load.Shield, art.FramesPerGroup, art.ActionFrame, art.FramesPerSecond) : null;
    }

    /// <summary>Drops the move ARTs and the NPCs' gait clocks with the frame overlay they were drawn into.</summary>
    private void ResetBodyMoves() { _bodyMoves.Clear(); _bodyCycles.Clear(); _bodyWalkFrames.Clear(); _armedArts.Clear(); _npcAttackFrames.Clear(); }

    // One body: its gait clock, then its pose (a companion that has not moved yet keeps its T0 frame and facing, as before it joined).
    private AreaDrawCommand AnimateBody(AreaDrawCommand command, AreaObjectRecord row, AreaVisualRecord visual, ActorDiagnosticAction action,
        int octant, long now, AreaBodyCycle cycle, bool keepT0Until, Action<AreaObjectKey, AreaObjectRecord, ActorDiagnosticAction, int>? gait = null, AreaBodyArt? walkArt = null)
    {
        if (command.Identity is not { } key || _readyArea is not { } area) return command;
        var elapsed = cycle.Observe(action, now);
        if (keepT0Until && !cycle.Moved) return command;
        if (!_bodyMoves.TryGetValue((visual.Entry, visual.RequestedPalette), out var moves))
        {
            moves = AreaBodyMoveArt.Load(visual, area.ArtSources.GetValueOrDefault(visual.Entry.Source));
            _bodyMoves[(visual.Entry, visual.RequestedPalette)] = moves;
            GD.Print($"ARC-WORLD-111 body-moves object={key.SourceIndex} status={moves.Status} detail={moves.Detail}");
        }
        // ARC-RENDER-1485: the walk of the weapon the NPC shows (the move ARTs kept are the body's own).
        var drawnMoves = walkArt is not null && action == ActorDiagnosticAction.Walk && moves.Status == AreaBodyMoveStatus.Ready ? moves with { Walk = walkArt } : moves;
        // ARC-RENDER-1126 (0x4305D0): the walk or run frame the cycle is on, for the footsteps (frames 3 and 8).
        if (gait is not null && moves.Status == AreaBodyMoveStatus.Ready && drawnMoves.For(action) is { } gaitArt)
            gait(key, row, action, AreaBodyMoveArt.CycleFrame(action == ActorDiagnosticAction.Idle ? 0 : elapsed, gaitArt.FramesPerSecond, gaitArt.FramesPerGroup));
        try
        {
            var (moved, pose) = AreaBodyMoveArt.Animate(command, row, visual, drawnMoves, action, octant, elapsed, cycle.Moved);
            if (pose is null) return command;
            ((AreaFrameOverlay)AreaFrames()).Add(pose.Key, pose.Frame);
            if (action != ActorDiagnosticAction.Idle) { _bodyWalkDraws++; _bodyWalkFrames.Add(pose.Key); }
            return moved;
        }
        catch (Exception error) when (error is Arcanum.Data.ArtException or ArgumentException or InvalidOperationException)
        {
            // An ART that decodes no frame for this facing: the body keeps its T0 frame from now on.
            _bodyMoves[(visual.Entry, visual.RequestedPalette)] = moves with { Status = AreaBodyMoveStatus.Unreadable, Detail = error.Message };
            GD.Print($"ARC-WORLD-111 body-moves object={key.SourceIndex} status=Unreadable detail={error.GetType().Name} {error.Message}");
            return command;
        }
    }
}
