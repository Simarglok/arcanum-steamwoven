namespace Arcanum.Data.Content;

/// <summary>
/// ARC-DEV-069: the folder of prototype files the host built from the player's own <c>Arcanum.exe</c> for an installation that has none
/// (the original writes its prototypes to <c>data/proto</c> at its first start; a GOG installation it never ran, or one unpacked on
/// macOS, has none). <see cref="ContentMountPlan.Discover"/> mounts it as <see cref="ContentSourceRole.PrototypeCache"/>, a base source
/// above the archives and below <c>data/</c>, so every reader that discovers the game folder (the viewer's many loaders) sees the same
/// prototypes without taking another argument. The host sets it once per run, after it checked or built the files for the game folder
/// it opens (the launcher's start), and clears it for an installation that has its own; nothing else sets it. The folder holds
/// <c>proto/NNNNNN - label.pro</c> files and nothing else.
/// </summary>
public static class PrototypeCacheMount
{
    private static string? _folder;

    /// <summary>The folder to mount, or null (the default): no cache.</summary>
    public static string? Folder => Volatile.Read(ref _folder);

    /// <summary>Mounts <paramref name="folder"/> (a full path) in every plan discovered from now on; null mounts none.</summary>
    public static void Use(string? folder)
    {
        if (folder is not null && (!Path.IsPathFullyQualified(folder) || folder.Any(char.IsControl) || folder.Length > GameFolderLocator.MaxPathLength))
            throw new ArgumentException("A full path without control characters is required.", nameof(folder));
        Volatile.Write(ref _folder, folder is null ? null : Path.TrimEndingDirectorySeparator(folder));
    }
}
