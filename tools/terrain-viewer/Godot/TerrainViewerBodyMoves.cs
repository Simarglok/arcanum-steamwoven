using Arcanum.Data.Area;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-111: the critters that walk off their T0 cell (the companions following the PC, the NPCs the AI walks) are drawn with
// their own walk or run cycle and facing, as the original draws every walker (step animation 0x42EDC0), not as their T0 frame slid
// across the map. The move ARTs are read on demand from the body's archive, once per standing art (AreaBodyMoveArt), and their frames
// join the shown map's frame overlay as the fallen poses do. A body whose move ARTs cannot be read keeps its T0 frame and says why once.
public partial class TerrainViewerApp
{
    private readonly Dictionary<(AreaEntryIdentity Entry, int Palette), AreaBodyMoves> _bodyMoves = [];
    private readonly Dictionary<AreaObjectKey, AreaBodyCycle> _bodyCycles = [];
    // Whether the last AI mover drawing showed a body stepping (its stand pose is drawn once more after the step).
    private bool _aiMoversStepping;
    /// <summary>Smoke observation: the moved bodies drawn with a walk or run frame, and the distinct frames drawn so.</summary>
    private int _bodyWalkDraws;
    private readonly HashSet<AreaFrameKey> _bodyWalkFrames = [];

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
            drawn.Add(AnimateBody(command, companion.Speaker.Row, visual, state.Action, state.Facing, now, keepT0Until: true));
        }
        return drawn.AsReadOnly();
    }

    /// <summary>The AI's moved NPCs' bodies (not the fallen ones) with the frame of their step and facing.</summary>
    private IReadOnlyList<AreaDrawCommand> AnimateAiMovers(AreaAiHost host, IReadOnlyList<AreaDrawCommand> commands, long now)
    {
        if (_areaOverlay is not { } overlay) return commands;
        var drawn = new List<AreaDrawCommand>(commands.Count);
        foreach (var command in commands)
        {
            if (command.Identity is not { } key || _fallenBodies.ContainsKey(key) || host.Scene.Npcs.FirstOrDefault(n => n.Row.Key == key) is not { } npc
                || host.Movers.Of(npc.Entity) is not { } mover || !overlay.Visuals.TryGetValue(command.Visual, out var visual)) { drawn.Add(command); continue; }
            var action = mover.Stepping(now) ? mover.Running ? ActorDiagnosticAction.Run : ActorDiagnosticAction.Walk : ActorDiagnosticAction.Idle;
            drawn.Add(AnimateBody(command, npc.Row, visual, action, DiagnosticGridActor.Octant(mover.Facing), now, keepT0Until: false));
        }
        return drawn.AsReadOnly();
    }

    /// <summary>Whether an AI mover is stepping now, or was at the last drawing (so its stand pose is drawn once).</summary>
    private bool AiMoversAnimating(AreaAiHost host, long now)
    {
        var stepping = host.Movers.All.Any(m => m.Stepping(now));
        var animating = stepping || _aiMoversStepping;
        _aiMoversStepping = stepping;
        return animating;
    }

    // One body: its gait clock, then its pose (a companion that has not moved yet keeps its T0 frame and facing, as before it joined).
    private AreaDrawCommand AnimateBody(AreaDrawCommand command, AreaObjectRecord row, AreaVisualRecord visual, ActorDiagnosticAction action,
        int octant, long now, bool keepT0Until)
    {
        if (command.Identity is not { } key || _readyArea is not { } area) return command;
        if (!_bodyCycles.TryGetValue(key, out var cycle)) _bodyCycles[key] = cycle = new AreaBodyCycle();
        var elapsed = cycle.Observe(action, now);
        if (keepT0Until && !cycle.Moved) return command;
        if (!_bodyMoves.TryGetValue((visual.Entry, visual.RequestedPalette), out var moves))
        {
            moves = AreaBodyMoveArt.Load(visual, area.ArtSources.GetValueOrDefault(visual.Entry.Source));
            _bodyMoves[(visual.Entry, visual.RequestedPalette)] = moves;
            GD.Print($"ARC-WORLD-111 body-moves object={key.SourceIndex} status={moves.Status} detail={moves.Detail}");
        }
        try
        {
            var (moved, pose) = AreaBodyMoveArt.Animate(command, row, visual, moves, action, octant, elapsed);
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
