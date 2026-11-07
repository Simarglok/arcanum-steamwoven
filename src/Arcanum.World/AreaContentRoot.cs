using Arcanum.Data.Content;
using Arcanum.Data.Scripts;

namespace Arcanum.World;

public enum AreaContentRootStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed }

/// <summary>The game folder's mount plan for the module archive an area was read from, or why there is none.</summary>
public sealed record AreaContentPlan(AreaContentRootStatus Status, ContentMountPlan? Plan, string Detail);

/// <summary>Script and dialog content for the area's module (ARC-SCRIPT-011), or why it is unavailable.</summary>
public sealed record AreaScriptContentLoad(AreaContentRootStatus Status, ScriptContent? Content, string Detail);

/// <summary>
/// The content stack of the game an area belongs to, found from the module archive the viewer reads
/// (<c>&lt;game&gt;/modules/&lt;Name&gt;.dat</c>, the installed layout): the same universal loader for every map and
/// every module, no per-location archive. Project policy (ARC-UI-005): the viewer takes no separate game-folder argument
/// for it; the mounted stack must hold that same file as its module archive. Details name statuses only, never paths.
/// </summary>
public static class AreaContentRoot
{
    public static AreaContentPlan Discover(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        string full;
        try { full = Path.GetFullPath(modulePath); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        { return new(AreaContentRootStatus.ModuleOutsideGameRoot, null, exception.GetType().Name); }
        var folder = Path.GetDirectoryName(full);
        var root = folder is null ? null : Path.GetDirectoryName(folder);
        if (root is null || !string.Equals(Path.GetFileName(folder), "modules", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(full), ".dat", StringComparison.OrdinalIgnoreCase))
            return new(AreaContentRootStatus.ModuleOutsideGameRoot, null, "module archive is not <game>/modules/<Name>.dat");
        var discovery = ContentMountPlan.Discover(root, Path.GetFileNameWithoutExtension(full));
        // Mount and stack details carry absolute paths, which logs never print.
        if (discovery.Status != ContentMountStatus.Ready) return new(AreaContentRootStatus.MountFailed, null, discovery.Status.ToString());
        var module = discovery.Plan!.SearchOrder.SingleOrDefault(source => source.Role == ContentSourceRole.ModuleArchive);
        // Discovery refuses names that differ only in case, so a case-insensitive comparison names one file.
        if (module is null || !string.Equals(Path.GetFullPath(module.Path), full, StringComparison.OrdinalIgnoreCase))
            return new(AreaContentRootStatus.ModuleOutsideGameRoot, null, "the mounted module archive is not the area's module file");
        return new(AreaContentRootStatus.Ready, discovery.Plan, "mounted");
    }

    /// <summary>Opens the module's script content; the caller owns and disposes it.</summary>
    public static AreaScriptContentLoad OpenScripts(string modulePath)
    {
        var found = Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return new(found.Status, null, found.Detail);
        var opened = ScriptContent.Open(found.Plan!);
        return opened.Status == ContentStackStatus.Ready
            ? new(AreaContentRootStatus.Ready, opened.Content, $"{opened.Content!.Names.Names.Count} script numbers")
            : new(AreaContentRootStatus.StackFailed, null, opened.Status.ToString());
    }
}
