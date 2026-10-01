using GamepadLab.Core;

namespace Arcanum.TerrainViewer.Core;

public readonly record struct DiagnosticSectorPosition(double X, double Y);
public sealed record DiagnosticActorState(DiagnosticSectorPosition Position, int Facing, ActorDiagnosticAction Action);
public enum DiagnosticMotionStatus { Accepted, InvalidInput, InvalidState, Overflow }
public sealed record DiagnosticMotionSegment(ActorDiagnosticAction Action, int Facing, double Seconds);
public sealed record DiagnosticActorStep(DiagnosticActorState State, DiagnosticMotionStatus Status, bool Moved,
    bool BoundaryLimited, IReadOnlyList<DiagnosticMotionSegment> Segments);

/// <summary>Screen-intuitive free motion inside a closed cell-center rectangle; no scenery query.</summary>
public sealed class DiagnosticActorMotion
{
    public DiagnosticActorState Reset() => new(DiagnosticWorldPolicy.InitialPosition, 0, ActorDiagnosticAction.Idle);
    public static Vec2 Project(DiagnosticSectorPosition position) => new(40 * (position.Y - position.X), 20 * (position.Y + position.X));
    public static DiagnosticSectorPosition Unproject(Vec2 delta) => new(delta.Y / 40 - delta.X / 80, delta.Y / 40 + delta.X / 80);

    public DiagnosticActorStep Update(DiagnosticActorState previous, Vec2 screenIntent, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(previous);
        DiagnosticActorStep Reject(DiagnosticMotionStatus status) => new(previous, status, false, false, Array.Empty<DiagnosticMotionSegment>());
        if (!ValidPosition(previous.Position) || previous.Facing is < 0 or > 7 || !Enum.IsDefined(previous.Action))
            return Reject(DiagnosticMotionStatus.InvalidState);
        if (!Finite(screenIntent) || !double.IsFinite(elapsedSeconds) || elapsedSeconds < 0 || screenIntent.Length > 2 + 1e-12)
            return Reject(DiagnosticMotionStatus.InvalidInput);
        if (screenIntent.Length == 0)
            return new(previous with { Action = ActorDiagnosticAction.Idle }, DiagnosticMotionStatus.Accepted, false, false,
                new[] { new DiagnosticMotionSegment(ActorDiagnosticAction.Idle, previous.Facing, elapsedSeconds) });
        if (elapsedSeconds == 0) return new(previous, DiagnosticMotionStatus.Accepted, false, false, Array.Empty<DiagnosticMotionSegment>());
        var screenVelocity = screenIntent * DiagnosticWorldPolicy.ProjectedPixelsPerSecond;
        var velocity = Unproject(screenVelocity);
        // Validate unbounded requested displacement too, before any boundary clamping.
        if (!Finite(screenVelocity) || !double.IsFinite(velocity.X * elapsedSeconds) || !double.IsFinite(velocity.Y * elapsedSeconds)
            || !double.IsFinite(previous.Position.X + velocity.X * elapsedSeconds) || !double.IsFinite(previous.Position.Y + velocity.Y * elapsedSeconds))
            return Reject(DiagnosticMotionStatus.Overflow);
        var x = previous.Position.X; var y = previous.Position.Y;
        var vx = velocity.X; var vy = velocity.Y;
        var remaining = elapsedSeconds; var facing = previous.Facing; var moved = false; var limited = false;
        var segments = new List<DiagnosticMotionSegment>(3);
        for (var segment = 0; segment < 3; segment++)
        {
            if ((x == 0 && vx < 0) || (x == 63 && vx > 0)) { vx = 0; limited = true; }
            if ((y == 0 && vy < 0) || (y == 63 && vy > 0)) { vy = 0; limited = true; }
            if (vx == 0 && vy == 0)
            {
                segments.Add(new(ActorDiagnosticAction.Idle, facing, remaining));
                remaining = 0; break;
            }
            if (remaining == 0) break;
            var tx = HitTime(x, vx); var ty = HitTime(y, vy);
            var seconds = Math.Min(remaining, Math.Min(tx, ty));
            if (seconds > 0)
            {
                facing = MapFacing(Project(new(vx, vy)));
                segments.Add(new(ActorDiagnosticAction.Walk, facing, seconds));
                x += vx * seconds; y += vy * seconds; moved = true;
            }
            if (tx <= seconds) { x = vx > 0 ? 63 : 0; vx = 0; limited = true; }
            if (ty <= seconds) { y = vy > 0 ? 63 : 0; vy = 0; limited = true; }
            remaining -= seconds;
        }
        if (remaining != 0 || !ValidPosition(new(x, y))) return Reject(DiagnosticMotionStatus.Overflow);
        var action = vx == 0 && vy == 0 ? ActorDiagnosticAction.Idle : ActorDiagnosticAction.Walk;
        // An exact end-of-frame stop also resets the action/clock at the hit time.
        if (action == ActorDiagnosticAction.Idle && (segments.Count == 0 || segments[^1].Action != action))
            segments.Add(new(action, facing, 0));
        return new(new(new(x, y), facing, action), DiagnosticMotionStatus.Accepted, moved, limited, segments.AsReadOnly());
    }

    private static double HitTime(double position, double velocity) => velocity > 0 ? (63 - position) / velocity
        : velocity < 0 ? -position / velocity : double.PositiveInfinity;
    private static bool Finite(Vec2 value) => double.IsFinite(value.X) && double.IsFinite(value.Y);
    private static bool ValidPosition(DiagnosticSectorPosition value) => double.IsFinite(value.X) && double.IsFinite(value.Y)
        && value.X is >= 0 and <= 63 && value.Y is >= 0 and <= 63;

    public static int MapFacing(Vec2 projectedVelocity)
    {
        if (!Finite(projectedVelocity) || projectedVelocity.Length == 0) throw new ArgumentOutOfRangeException(nameof(projectedVelocity));
        var octant = Math.Atan2(projectedVelocity.X, -projectedVelocity.Y) * (4 / Math.PI);
        if (octant < 0) octant += 8;
        // Absorb only floating-point noise at mathematically exact half-octants.
        return (int)Math.Floor(octant + .5 + 1e-12) % 8;
    }

    public static ActorDiagnosticPlacementInput ToPlacement(DiagnosticActorState state, int frame)
    {
        if (!ValidPosition(state.Position) || state.Facing is < 0 or > 7 || !Enum.IsDefined(state.Action) || frame < 0)
            throw new ArgumentOutOfRangeException(nameof(state));
        var x = checked((long)Math.Floor(state.Position.X)); var y = checked((long)Math.Floor(state.Position.Y));
        var residual = Project(new(state.Position.X - x, state.Position.Y - y));
        return new(x, y, 0, 0, checked((int)Math.Round(residual.X, MidpointRounding.ToEven)),
            checked((int)Math.Round(residual.Y, MidpointRounding.ToEven)), ActorDiagnosticRenderBranch.NormalUncached, 100, false,
            state.Action, state.Facing, frame, 0, 0, 0, 0, 0, 0);
    }
}
