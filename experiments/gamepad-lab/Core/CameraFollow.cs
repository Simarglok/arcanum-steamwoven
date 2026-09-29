namespace GamepadLab.Core;

public enum CameraMode { Follow, Manual }

/// <summary>
/// Synthetic camera policy, independent of Godot. Input vectors have already passed
/// their per-domain circular deadzones; a zero vector is therefore the policy's
/// neutral sample and filters ordinary stick drift.
/// </summary>
public sealed record CameraFollowState(
    CameraMode Mode = CameraMode.Follow,
    bool RecenterArmed = false,
    bool TrackingActor = true);

public sealed record CameraBounds(Vec2 Minimum, Vec2 Maximum, Vec2 ViewportPixels, double Zoom)
{
    public Vec2 ClampCenter(Vec2 center)
    {
        var zoom = Math.Max(Zoom, .001);
        var halfViewport = new Vec2(ViewportPixels.X / zoom / 2d, ViewportPixels.Y / zoom / 2d);
        double ClampAxis(double value, double minimum, double maximum, double half)
        {
            // A world smaller than the viewport has exactly one legal center: its
            // midpoint. This avoids an inverted clamp range and deterministicly
            // exposes the whole synthetic map rather than choosing an arbitrary edge.
            if (maximum - minimum <= half * 2d) return (minimum + maximum) / 2d;
            return Math.Clamp(value, minimum + half, maximum - half);
        }
        return new(ClampAxis(center.X, Minimum.X, Maximum.X, halfViewport.X), ClampAxis(center.Y, Minimum.Y, Maximum.Y, halfViewport.Y));
    }
}

public sealed record CameraFollowStep(CameraFollowState State, Vec2 Center, string Note);

public sealed class CameraFollowEngine
{
    /// <summary>Initial and explicit reset behavior is Follow centered on the actor.</summary>
    public CameraFollowStep ResetCentered(Vec2 actorPosition, CameraBounds bounds) =>
        new(new CameraFollowState(), bounds.ClampCenter(actorPosition), "reset enters Follow centered on the actor (subject to map bounds)");

    /// <summary>
    /// A lifecycle quarantine clears stale right-stick/recenter edges without moving
    /// the image. Follow resumes only on a fresh actor movement sample, preventing a
    /// focus/modal/disconnect/reconnect transition from snapping a preserved view.
    /// </summary>
    public CameraFollowState SafetyReset(CameraFollowState previous) =>
        previous with { Mode = CameraMode.Follow, RecenterArmed = false, TrackingActor = false };

    public CameraFollowStep Update(CameraFollowState previous, Vec2 actorPosition, Vec2 currentCenter, Vec2 actorInput, Vec2 worldImagePan, CameraBounds bounds)
    {
        var actorActive = actorInput.Length > 0;
        var panActive = worldImagePan.Length > 0;
        // The reducer already divides image translation by zoom. WorldRoot's Canvas
        // transform scales that translation back to the same visual pan speed, so
        // its inverse center adjustment remains in unscaled world coordinates.
        var manualCenter = bounds.ClampCenter(currentCenter - worldImagePan);

        // Pan always wins a simultaneous edge. It never changes actor intent, and
        // neither an old neutral sample nor a held actor stick may immediately undo it.
        if (panActive)
            return new(new CameraFollowState(CameraMode.Manual, false, false), manualCenter, "manual pan has priority");

        if (previous.Mode == CameraMode.Follow)
        {
            if (previous.TrackingActor || actorActive)
                return new(previous with { Mode = CameraMode.Follow, RecenterArmed = false, TrackingActor = true }, bounds.ClampCenter(actorPosition), "following actor");
            return new(previous with { RecenterArmed = false }, bounds.ClampCenter(currentCenter), "safety-reset Follow awaits fresh actor movement");
        }

        // In Manual, release alone changes nothing. Only a full neutral sample arms
        // resumption; a later actor-only neutral-to-active edge returns to Follow.
        if (!actorActive)
            return new(previous with { RecenterArmed = true, TrackingActor = false }, bounds.ClampCenter(currentCenter), "manual neutral sample armed recenter");
        if (previous.RecenterArmed)
            return new(new CameraFollowState(CameraMode.Follow, false, true), bounds.ClampCenter(actorPosition), "fresh actor-only input resumed Follow");
        return new(previous with { TrackingActor = false }, bounds.ClampCenter(currentCenter), "manual persists while actor is held");
    }
}
