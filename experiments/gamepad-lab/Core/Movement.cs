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
    double StopDistance = 3,
    double MaxCollisionSampleDistance = 8,
    int MaximumCollisionSamples = 512);

public sealed record MovementState(Vec2 Position, Vec2 Heading, Vec2? Goal = null, double GoalAgeSeconds = 0, int ReplanCount = 0, MovementOutcome Outcome = MovementOutcome.Idle, Vec2? BlockedIntent = null);
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
        var effort = desiredScreenDirection.Length;
        if (effort == 0)
            return new(previous with { Goal = null, GoalAgeSeconds = 0, Outcome = MovementOutcome.Idle, BlockedIntent = null }, false, "neutral cancels pending destination and blocked latch");

        // A blocked held stick is not a request to replan every rendered frame.
        // It must be released or materially redirected before the lab tries again.
        var inputDirection = desiredScreenDirection.Normalized();
        if (previous.BlockedIntent is Vec2 blocked && Vec2.Distance(blocked, inputDirection) < .2)
            return new(previous with { Goal = null, GoalAgeSeconds = 0, Outcome = MovementOutcome.Blocked }, false, "blocked intent remains latched until neutral or changed direction");

        // Effort is intentionally retained from the reducer through displacement:
        // settings constrain it to 0.5–2.0, while direction only determines heading
        // and the semantic nearby goal. Do not normalize this value downstream.
        var requestedDistance = policy.Speed * effort * Math.Max(0, delta);
        if (TooManyCollisionSamples(requestedDistance, policy))
            return new(previous with { Goal = null, GoalAgeSeconds = 0, Outcome = MovementOutcome.Blocked, BlockedIntent = inputDirection }, false, "movement exceeds bounded synthetic collision sweep; intent latched");

        if (mode == MovementMode.DirectIntent)
            return MoveDirect(previous, inputDirection, requestedDistance, policy, canOccupy);

        return MoveNearbyGoal(previous, inputDirection, effort, Math.Max(0, delta), policy, canOccupy);
    }

    private static MovementStep MoveDirect(MovementState previous, Vec2 direction, double distance, MovementPolicy policy, Func<Vec2, double, bool> canOccupy)
    {
        if (!TrySweep(previous.Position, direction, distance, policy, canOccupy, out var position))
            return new(previous with { Position = position, Heading = direction, Goal = null, GoalAgeSeconds = 0, Outcome = MovementOutcome.Blocked, BlockedIntent = direction }, true, "blocked by synthetic obstacle sweep; destination cancelled and intent latched");
        return new(previous with { Position = position, Heading = direction, Goal = null, GoalAgeSeconds = 0, Outcome = MovementOutcome.Moving, BlockedIntent = null }, distance > 0, "direct desired-direction intent");
    }

    private static MovementStep MoveNearbyGoal(MovementState previous, Vec2 desiredDirection, double effort, double delta, MovementPolicy policy, Func<Vec2, double, bool> canOccupy)
    {
        var position = previous.Position;
        var heading = previous.Heading;
        var goal = previous.Goal;
        var age = Math.Max(0, previous.GoalAgeSeconds);
        var replans = previous.ReplanCount;
        var remaining = delta;
        var attempted = false;
        var arrived = false;

        // A bounded internal progression gives the stored goal real movement
        // authority without making a render frame's partition change open-space
        // displacement. Each slice is no longer than the command cadence or the
        // distance needed to observe the hysteresis threshold.
        while (remaining > 1e-9)
        {
            var candidate = position + desiredDirection * policy.Lookahead;
            if (goal is null || (age >= policy.GoalCadenceSeconds && Vec2.Distance(goal.Value, candidate) >= policy.GoalHysteresis))
            {
                goal = candidate;
                age = 0;
                replans++;
                arrived = false;
            }

            var toGoal = goal.Value - position;
            var distanceToGoal = toGoal.Length;
            if (distanceToGoal <= policy.StopDistance)
            {
                // The stored target is authoritative. A held direction may create a
                // fresh nearby target only after its cadence/hysteresis gate opens.
                age += remaining;
                remaining = 0;
                arrived = true;
                break;
            }

            var speed = policy.Speed * effort;
            var cadenceSlice = age < policy.GoalCadenceSeconds ? policy.GoalCadenceSeconds - age : policy.GoalCadenceSeconds;
            var hysteresisSlice = policy.GoalHysteresis / Math.Max(speed, 1e-9);
            var seconds = Math.Min(remaining, Math.Min(distanceToGoal / speed, Math.Min(cadenceSlice, hysteresisSlice)));
            // Defensive fallback for a zero/invalid policy value: stop safely rather
            // than spin forever or bypass the stored target.
            if (seconds <= 1e-9)
            {
                arrived = true;
                break;
            }

            var directionToGoal = toGoal.Normalized();
            var distance = speed * seconds;
            attempted = attempted || distance > 0;
            if (!TrySweep(position, directionToGoal, distance, policy, canOccupy, out var nextPosition))
                return new(new MovementState(nextPosition, directionToGoal, null, 0, replans, MovementOutcome.Blocked, desiredDirection), true, "blocked by synthetic obstacle sweep; destination cancelled and intent latched");

            position = nextPosition;
            heading = directionToGoal;
            age += seconds;
            remaining -= seconds;
        }

        return new(new MovementState(position, heading, goal, age, replans, arrived ? MovementOutcome.Arrived : MovementOutcome.Moving, null), attempted, arrived ? "arrived at bounded actor-relative semantic goal" : "moving toward bounded actor-relative semantic goal");
    }

    private static bool TooManyCollisionSamples(double distance, MovementPolicy policy) =>
        policy.MaxCollisionSampleDistance <= 0 || Math.Ceiling(distance / policy.MaxCollisionSampleDistance) > policy.MaximumCollisionSamples;

    private static bool TrySweep(Vec2 start, Vec2 direction, double distance, MovementPolicy policy, Func<Vec2, double, bool> canOccupy, out Vec2 lastSafe)
    {
        var samples = Math.Max(1, (int)Math.Ceiling(distance / policy.MaxCollisionSampleDistance));
        lastSafe = start;
        for (var sample = 1; sample <= samples; sample++)
        {
            var candidate = start + direction * (distance * sample / samples);
            if (!canOccupy(candidate, policy.ActorRadius)) return false;
            lastSafe = candidate;
        }
        return true;
    }
}
