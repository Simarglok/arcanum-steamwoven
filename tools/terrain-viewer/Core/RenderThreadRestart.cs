using Arcanum.Session.Launcher;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-RENDER-3420: the launcher's "separate rendering thread" setting. The engine picks its thread model only when it starts, so a
/// start that wants the separate thread restarts the game once. The first process keeps the default model, so the launcher screen (and the
/// setting) stays reachable even if the other model fails.</summary>
public static class RenderThreadRestart
{
    /// <summary>The user argument that marks the restarted process: it never restarts again and goes past a "show at every start" screen.</summary>
    public const string Flag = "--render-thread-restart";

    /// <summary>Removes <see cref="Flag"/> from <paramref name="args"/>; true when it was there.</summary>
    public static bool Take(List<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.RemoveAll(argument => argument == Flag) > 0;
    }

    /// <summary>Whether this start restarts into the separate rendering thread. An automated run never does.</summary>
    public static bool Needed(bool wanted, bool restarted, bool automated) => wanted && !restarted && !automated;

    /// <summary>The restarted process's command line: the engine option, this start's remaining engine arguments, then the user arguments
    /// (without <c>--launcher</c>, whose screen the first process showed) and <see cref="Flag"/>. <paramref name="projectPath"/> is the project folder of a start from sources (the engine consumes its own
    /// <c>--path</c>), null for an exported game.</summary>
    public static string[] Arguments(IEnumerable<string> engine, IEnumerable<string> user, string? projectPath)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(user);
        var arguments = new List<string> { "--render-thread", "separate" };
        if (projectPath is not null) arguments.AddRange(["--path", projectPath]);
        arguments.AddRange(engine.Where(argument => argument is not ("--" or "++")));
        arguments.Add("--");
        arguments.AddRange(user.Where(argument => argument is not (Flag or "--launcher")));
        arguments.Add(Flag);
        return arguments.ToArray();
    }

    /// <summary>The restarted process continues the start that Play (or a start without the screen) began: "show at every start" alone does
    /// not open the screen again. Every other reason still does.</summary>
    public static LauncherShowReason Reason(LauncherShowReason decided, bool restarted)
        => restarted && decided == LauncherShowReason.ShowAtStart ? LauncherShowReason.None : decided;
}
