namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-3453 (owner request 2026-11-27): the option that skips the three start-up movies (the two logos and movie 8) before the first
/// menu. It does not skip the loading picture between them (ARC-UI-3501, owner decision 2026-11-28). It is off unless the start passes the lone user argument <see cref="SkipFlag"/> (after Godot's <c>--</c> or <c>++</c>; with or
/// without the other arguments; for tool runs and measurements of the packaged build) or the launcher settings store it on
/// (<see cref="Arcanum.Session.Launcher.LauncherSettings.SkipStartupMovies"/>). Every other movie plays as before.
/// </summary>
public static class ViewerStartupMovies
{
    public const string SkipFlag = "--skip-startup-movies";

    /// <summary>ARC-UI-3501: how long the loading picture stays. The original keeps it exactly while it loads (no minimum, no key ends it);
    /// this build has loaded before the first logo, so the picture gets a fixed time instead, until the loading moves under it (#3507).</summary>
    public const double PictureMilliseconds = 3000;

    /// <summary>The most one frame counts against <see cref="PictureMilliseconds"/>, so a long frame cannot swallow the picture.</summary>
    public const double PictureFrameCapMilliseconds = 250;

    /// <summary>Removes every <see cref="SkipFlag"/> from the user arguments and says whether there was one, so the rest parses as
    /// before (a start without other arguments stays a start from the game folder).</summary>
    public static bool TakeSkip(List<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.RemoveAll(arg => arg == SkipFlag) > 0;
    }
}
