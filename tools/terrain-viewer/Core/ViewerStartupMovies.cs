namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-3453 (owner request 2026-11-27): the option that skips the three start-up movies (the two logos and movie 8) before the first
/// menu. It is off unless the start passes the lone user argument <see cref="SkipFlag"/> (after Godot's <c>--</c> or <c>++</c>; with or
/// without the other arguments; for tool runs and measurements of the packaged build) or the launcher settings store it on
/// (<see cref="Arcanum.Session.Launcher.LauncherSettings.SkipStartupMovies"/>). Every other movie plays as before.
/// </summary>
public static class ViewerStartupMovies
{
    public const string SkipFlag = "--skip-startup-movies";

    /// <summary>Removes every <see cref="SkipFlag"/> from the user arguments and says whether there was one, so the rest parses as
    /// before (a start without other arguments stays a start from the game folder).</summary>
    public static bool TakeSkip(List<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.RemoveAll(arg => arg == SkipFlag) > 0;
    }
}
