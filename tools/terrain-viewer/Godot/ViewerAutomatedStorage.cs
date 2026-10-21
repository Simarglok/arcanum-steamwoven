using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-032: the process-wide automated-run storage. The app marks the process automated when it starts a self-test, a native
/// smoke or the WORLD settings validation, before any setting is read; from then on the stores without an explicit test path use a fresh temporary folder of this
/// process instead of the player's <c>user://</c> files (<see cref="ViewerStoragePaths"/>). The player's files are never read,
/// written or deleted by an automated run; the temporary folder is deleted when the main app exits.
/// </summary>
public static class ViewerAutomatedStorage
{
    private static readonly Lazy<string> IsolatedFolder = new(Create);

    /// <summary>Whether this process runs automated (self-test, native smoke, WORLD settings validation).</summary>
    public static bool Automated { get; private set; }

    /// <summary>Marks the process automated; idempotent.</summary>
    public static void MarkAutomated() => Automated = true;

    /// <summary>The store's path: the test path, else (automated) the isolated folder's file, else the player's <c>user://</c> path.</summary>
    public static string Path(string? testPath, string userPath) =>
        ViewerStoragePaths.Choose(testPath, Automated, userPath, () => System.IO.Path.Combine(Folder(), ViewerStoragePaths.FileName(userPath)));

    /// <summary>The isolated folder of this process (created on first use, deleted by <see cref="Delete"/> when the app exits).</summary>
    public static string Folder() => IsolatedFolder.Value;

    /// <summary>Deletes the isolated folder when this process created one (the main app's exit, after its last save); nothing of
    /// the player's is in it. A failure leaves it to the system's temporary-file cleanup.</summary>
    public static void Delete()
    {
        if (!IsolatedFolder.IsValueCreated) return;
        try { Directory.Delete(IsolatedFolder.Value, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static string Create()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"arcanum-viewer-automated-{System.Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        GD.Print("ARC-DEV-032 automated run: settings and saves are isolated from the player's files");
        return folder;
    }
}
