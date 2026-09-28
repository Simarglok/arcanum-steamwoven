namespace GamepadLab.Core;

/// <summary>Lab-only adapters; neither mode claims original Arcanum movement semantics.</summary>
public enum MovementMode { DirectIntent, NearbyGoal }
public enum MovementOutcome { Idle, Moving, Arrived, Blocked }

public sealed record MovementPolicy(
    double Speed = 240,
    double ActorRadius = 16,
    double Lookahead = 96,
    double GoalHysteresis = 12,
    double GoalCadenceSeconds = .15,
    double StopDistance = 3);

public sealed record MovementState(Vec2 Position, Vec2 Heading, Vec2? Goal = null, double GoalAgeSeconds = 0, int ReplanCount = 0, MovementOutcome Outcome = MovementOutcome.Idle);
public sealed record MovementStep(MovementState State, bool AttemptedMotion, string Note);

/// <summary>
/// Semantic movement boundary used by the synthetic scene. Collision is supplied by
/// the replaceable presentation adapter; this core neither emits mouse events nor
/// depends on Godot navigation/physics.
/// </summary>
public sealed class MovementEngine
{
    public MovementStep Update(MovementState previous, Vec2 desiredScreenDirection, double delta, MovementMode mode, MovementPolicy policy, Func<Vec2, double, bool> canOccupy)
    {
        var desired = desiredScreenDirection.Length > 1 ? desiredScreenDirection.Normalized() : desiredScreenDirection;
        if (desired.Length == 0)
            return new(previous with { Goal = null, GoalAgeSeconds = 0, Outcome = MovementOutcome.Idle }, false, "neutral cancels pending destination");

        var goal = previous.Goal;
        var age = previous.GoalAgeSeconds + delta;
        var replans = previous.ReplanCount;
        if (mode == MovementMode.NearbyGoal)
        {
            var candidate = previous.Position + desired.Normalized() * policy.Lookahead;
            if (goal is null || Vec2.Distance(goal.Value, candidate) >= policy.GoalHysteresis && age >= policy.GoalCadenceSeconds)
            {
                goal = candidate; age = 0; replans++;
            }
            else if (goal is null) { goal = candidate; age = 0; replans++; }
            desired = (goal.Value - previous.Position).Normalized();
        }

        var distance = policy.Speed * delta;
        var candidatePosition = previous.Position + desired * distance;
        if (!canOccupy(candidatePosition, policy.ActorRadius))
            return new(previous with { Goal = null, GoalAgeSeconds = 0, ReplanCount = replans, Outcome = MovementOutcome.Blocked }, true, "blocked by synthetic obstacle; destination cancelled");

        var remaining = goal is null ? double.PositiveInfinity : Vec2.Distance(candidatePosition, goal.Value);
        var outcome = remaining <= policy.StopDistance ? MovementOutcome.Arrived : MovementOutcome.Moving;
        return new(previous with { Position = candidatePosition, Heading = desired, Goal = outcome == MovementOutcome.Arrived ? null : goal, GoalAgeSeconds = age, ReplanCount = replans, Outcome = outcome }, true, mode == MovementMode.DirectIntent ? "direct desired-direction intent" : "actor-relative nearby semantic goal");
    }
}
