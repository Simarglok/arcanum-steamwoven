using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-032: the process-wide automated-run storage. The app marks the process automated when it starts a self-test or a native
/// smoke, before any setting is read; from then on the stores without an explicit test path use a fresh temporary folder of this
/// process instead of the player's <c>user://</c> files (<see cref="ViewerStoragePaths"/>). The player's files are never read,
/// written or deleted by an automated run; the temporary folder is left to the system's temporary-file cleanup.
/// </summary>
public static class ViewerAutomatedStorage
{
    private static string? _folder;

    /// <summary>Whether this process runs automated (self-test or native smoke).</summary>
    public static bool Automated { get; private set; }

    /// <summary>Marks the process automated; idempotent.</summary>
    public static void MarkAutomated() => Automated = true;

    /// <summary>The store's path: the test path, else (automated) the isolated folder's file, else the player's <c>user://</c> path.</summary>
    public static string Path(string? testPath, string userPath) =>
        ViewerStoragePaths.Choose(testPath, Automated, userPath, () => System.IO.Path.Combine(Folder(), ViewerStoragePaths.FileName(userPath)));

    /// <summary>The isolated folder of this process (created on first use).</summary>
    public static string Folder()
    {
        if (_folder is null)
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"arcanum-viewer-automated-{System.Environment.ProcessId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            _folder = folder;
            GD.Print("ARC-DEV-032 automated run: settings and saves are isolated from the player's files");
        }
        return _folder;
    }
}
