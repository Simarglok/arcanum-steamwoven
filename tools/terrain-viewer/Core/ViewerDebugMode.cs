namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-050: the debug mode of a run, a development aid that is never gameplay. It is off unless the start passes the lone user
/// argument <see cref="Flag"/> (after Godot's <c>--</c> or <c>++</c>; with or without the other arguments) or the launcher settings
/// store it on (<see cref="Arcanum.Session.Launcher.LauncherSettings.DebugMode"/>). It holds for the whole run. While it is on the
/// world map (W) lists every area and teleports to the chosen one at once, from any map; without it the world map has no area list
/// and keeps the original's travel only. The session's rules and state are not changed by the mode itself: a teleport moves the
/// PC (and the following companions) as a landing does, with no game time and no discovered area. A save made while it is on is
/// marked in its location (<see cref="MarkLocation"/>), and the build line of the launcher and the main menu says so.
/// </summary>
public static class ViewerDebugMode
{
    public const string Flag = "--debug-mode";
    /// <summary>The prefix of a save location written in debug mode (the save list shows it).</summary>
    public const string SaveMark = "[DEBUG] ";
    /// <summary>Appended to the build line while the mode is on.</summary>
    public const string BuildSuffix = " · DEBUG MODE";

    /// <summary>Removes every <see cref="Flag"/> from the user arguments and says whether there was one, so the rest parses as
    /// before (a start without other arguments stays a start from the game folder).</summary>
    public static bool Take(List<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.RemoveAll(arg => arg == Flag) > 0;
    }

    /// <summary>A save location in debug mode: <see cref="SaveMark"/> first, so the save list names it.</summary>
    public static string MarkLocation(string location, bool debugMode)
    {
        ArgumentNullException.ThrowIfNull(location);
        return debugMode && !location.StartsWith(SaveMark, StringComparison.Ordinal) ? SaveMark + location : location;
    }
}
