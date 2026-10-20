namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-032: where a viewer setting or save lives. An explicit test path (an environment variable such as
/// <c>WORLD_DIAGNOSTIC_ZOOM_TEST_PATH</c>) wins; an automated run (the self-test, a native smoke) without one uses its own
/// isolated folder, fresh for the process; only an ordinary run reads and writes the player's file. So an automated run never
/// reads the player's saved settings (a saved zoom of 1.7 once broke the WORLD self-test) and never writes them.
/// </summary>
public static class ViewerStoragePaths
{
    /// <summary>The path a store uses: <paramref name="testPath"/> when set, else <paramref name="isolated"/>'s path of the same file
    /// for an automated run, else <paramref name="userPath"/>.</summary>
    public static string Choose(string? testPath, bool automated, string userPath, Func<string> isolated)
    {
        ArgumentNullException.ThrowIfNull(userPath); ArgumentNullException.ThrowIfNull(isolated);
        if (!string.IsNullOrWhiteSpace(testPath)) return testPath;
        return automated ? isolated() : userPath;
    }

    /// <summary>The file name of a <c>user://</c> path (its last segment), for the isolated folder.</summary>
    public static string FileName(string userPath)
    {
        ArgumentNullException.ThrowIfNull(userPath);
        var name = userPath[(userPath.LastIndexOf('/') + 1)..];
        return name.Length == 0 ? throw new ArgumentException("A file path is required.", nameof(userPath)) : name;
    }
}
