using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Arcanum.Data.Content;

/// <summary>The operating system a game folder is looked for on (ARC-DEV-046). GOG ships the game for Windows only; on macOS the
/// player copies or extracts the Windows files, or runs them in a Wine wrapper.</summary>
public enum GameFolderPlatformKind { MacOS, Windows, Other }

/// <summary>The machine facts <see cref="GameFolderLocator"/> reads: the platform, the home and system applications folders (macOS),
/// the Windows system drive and 32-bit program folder, and the reader of the GOG install path in the Windows registry (a key under
/// <c>HKEY_LOCAL_MACHINE</c> to its <c>path</c> value, null when absent). Tests pass a synthetic one.</summary>
public sealed record GameFolderPlatform(GameFolderPlatformKind Kind, string? Home, string? Applications, string? SystemDrive,
    string? ProgramFilesX86, Func<string, string?> RegistryPath)
{
    /// <summary>This machine: the Windows registry is read only on Windows (a failed read is no path).</summary>
    public static GameFolderPlatform Current()
    {
        if (OperatingSystem.IsWindows())
            return new(GameFolderPlatformKind.Windows, null, null, Path.GetPathRoot(Environment.SystemDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), WindowsRegistryPath);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? new(GameFolderPlatformKind.MacOS, string.IsNullOrEmpty(home) ? null : home, "/Applications", null, null, static _ => null)
            : new(GameFolderPlatformKind.Other, null, null, null, null, static _ => null);
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsRegistryPath(string key)
    {
        try
        {
            using var opened = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
            return opened?.GetValue("path") as string;
        }
        catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>Where a candidate folder came from: the GOG registry key, a default install folder, a CrossOver bottle, inside a Wine
/// wrapper or bottle the player chose, or the player's own choice.</summary>
public enum GameFolderOrigin { Registry, DefaultFolder, CrossOverBottle, Wrapper, Chosen }

public sealed record GameFolderCandidate(string Path, GameFolderOrigin Origin);

/// <summary>Why a folder is or is not a game folder the game can open: <see cref="Ready"/>, a content-stack refusal of
/// <see cref="ContentMountPlan.Discover"/> (missing folder, <c>tig.dat</c> or module, an ambiguous name, an unsupported layout,
/// unreadable), or an input the game's map mode needs that the stack does not require (the module archive
/// <c>modules/Arcanum.dat</c>, <c>arcanum1.dat</c>, <c>arcanum2.dat</c>, and since ARC-DEV-066 the prototype files
/// <c>data/proto/*.pro</c>: <see cref="MissingPrototypes"/>).</summary>
public enum GameFolderStatus
{
    Ready, MissingFolder, MissingTig, MissingModule, AmbiguousName, UnsupportedLayout, Unreadable,
    MissingModuleArchive, MissingActorArchive, MissingTerrainArchive,
    /// <summary>ARC-DEV-066: no base source holds a <c>proto/NNNNNN - label.pro</c> file. The installed game ships no prototype
    /// file: <c>Arcanum.exe</c> builds every prototype in code and writes it to <c>data\proto</c> at its first start
    /// (0x4681B0 → 0x468660 → 0x468890), so an installation the original never ran has none.</summary>
    MissingPrototypes,
}

/// <summary>An archive's file name in the game folder (as found) and its size in bytes.</summary>
public sealed record GameArchiveSize(string Name, long Bytes);

/// <summary>What identifies an installation, so a changed one is noticed at start: <c>Version.txt</c>'s text (null when absent or
/// not one short printable line), <c>Arcanum.exe</c>'s size and SHA-256 (null when absent or too large), whether the GOG marker
/// <c>goggame-1207658829.info</c> is there, and the size of every mounted archive in mount order.</summary>
public sealed record GameFolderIdentity(string? Version, long? ExeBytes, string? ExeSha256, bool GogMarker, IReadOnlyList<GameArchiveSize> Archives)
{
    /// <summary>The retail 1.0.7.4 executable the project verifies against (the original executable check).</summary>
    public bool VerifiedRelease => string.Equals(ExeSha256, GameFolderLocator.VerifiedExeSha256, StringComparison.Ordinal);

    /// <summary>Equal fields and the same archives, names compared ASCII case-insensitively, in the same order.</summary>
    public bool SameAs(GameFolderIdentity? other)
        => other is not null && Version == other.Version && ExeBytes == other.ExeBytes && ExeSha256 == other.ExeSha256
           && GogMarker == other.GogMarker && Archives.Count == other.Archives.Count
           && Archives.Zip(other.Archives).All(pair => ContentMountPlan.AsciiLower(pair.First.Name) == ContentMountPlan.AsciiLower(pair.Second.Name)
                                                     && pair.First.Bytes == pair.Second.Bytes);
}

/// <summary>A checked folder: its status, the full path checked, and when <see cref="GameFolderStatus.Ready"/> the content stack's
/// mount plan, the map inputs and the identity. <see cref="Detail"/> names what is missing or refused (a name or path, never bytes).</summary>
public sealed record GameFolderCheck(GameFolderStatus Status, string Path, GameFolderOrigin Origin, string? Detail = null,
    ContentMountPlan? Plan = null, GameFolderInputs? Inputs = null, GameFolderIdentity? Identity = null)
{
    public bool Ready => Status == GameFolderStatus.Ready;
}

/// <summary>The inputs the game's map mode reads from a ready game folder: the folder itself, the module archive, the actor art
/// archive (<c>arcanum1.dat</c>) and the terrain archive (<c>arcanum2.dat</c>), each a full path as found.</summary>
public sealed record GameFolderInputs(string GameRoot, string ModuleArchive, string ActorArchive, string TerrainArchive);

/// <summary>Every candidate checked by <see cref="GameFolderLocator.Detect"/>, in order, and the first ready one (null when none).</summary>
public sealed record GameFolderDetection(IReadOnlyList<GameFolderCheck> Checked)
{
    public GameFolderCheck? Found => Checked.FirstOrDefault(check => check.Ready);
}

/// <summary>ARC-DEV-046: finds and checks the player's original game folder (research ARC-RES-074, "Finding the game folder").
/// It looks only at a short fixed list of candidates and at the folder the player chooses, never scans a disk, and only reads:
/// the installation stays read-only. Each candidate is checked with <see cref="ContentMountPlan.Discover"/> plus the map inputs,
/// then identified. The candidate paths (GOG registry key, default folders, wrapper layouts) are common layouts and public
/// reports, not yet checked on a machine (level F0); the packaging task checks the ones it supports.</summary>
public static class GameFolderLocator
{
    public const string GogProductId = "1207658829";
    public const string GogMarkerFile = "goggame-" + GogProductId + ".info";
    /// <summary>SHA-256 of the retail 1.0.7.4 <c>Arcanum.exe</c> (GOG), the only executable the project verifies against.</summary>
    public const string VerifiedExeSha256 = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52";
    /// <summary>The registry keys (under <c>HKEY_LOCAL_MACHINE</c>) holding the GOG install path, the 32-bit view first.</summary>
    public static readonly IReadOnlyList<string> GogRegistryKeys =
        [$@"SOFTWARE\WOW6432Node\GOG.com\Games\{GogProductId}", $@"SOFTWARE\GOG.com\Games\{GogProductId}"];
    /// <summary>At most this many CrossOver bottles are looked into.</summary>
    public const int MaxBottles = 64;
    /// <summary>A longer path is refused before any file access.</summary>
    public const int MaxPathLength = 4096;
    /// <summary>A larger <c>Arcanum.exe</c> is not hashed (the retail one is about 3 MB).</summary>
    public const long MaxExeBytes = 64L * 1024 * 1024;
    /// <summary>A longer <c>Version.txt</c> is not read as a version.</summary>
    public const int MaxVersionBytes = 256;
    /// <summary>A version longer than this is no version.</summary>
    public const int MaxVersionLength = 64;
    /// <summary>A folder mounting more archives than this (tig, <c>arcanum*.dat</c>, the module and its patches) is an unsupported layout.</summary>
    public const int MaxArchives = 64;

    private static readonly string[][] InstallFolders = [["GOG Games", "Arcanum"], ["Program Files (x86)", "GOG Galaxy", "Games", "Arcanum"]];
    private static readonly string[][] WrapperDrives = [["Contents", "SharedSupport", "prefix", "drive_c"], ["Contents", "Resources", "drive_c"]];

    /// <summary>The fixed candidates of <paramref name="platform"/>, in the order they are tried, each path once. Windows: the GOG
    /// registry path, then <c>C:\GOG Games\Arcanum</c> and the GOG Galaxy folder. macOS: <c>/Applications/Arcanum</c>,
    /// <c>~/Applications/Arcanum</c>, then the GOG folders inside each CrossOver bottle. Whisky bottles live in another app's
    /// container (macOS would ask the player for access), so they are only looked at when chosen.</summary>
    public static IReadOnlyList<GameFolderCandidate> Candidates(GameFolderPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        var candidates = new List<GameFolderCandidate>();
        void Add(string? path, GameFolderOrigin origin)
        {
            if (!Admissible(path)) return;
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path!));
            if (candidates.All(candidate => !string.Equals(candidate.Path, full, StringComparison.OrdinalIgnoreCase))) candidates.Add(new(full, origin));
        }
        try
        {
            switch (platform.Kind)
            {
                case GameFolderPlatformKind.Windows:
                    foreach (var key in GogRegistryKeys) Add(platform.RegistryPath(key), GameFolderOrigin.Registry);
                    if (Admissible(platform.SystemDrive)) Add(Path.Combine(platform.SystemDrive!, "GOG Games", "Arcanum"), GameFolderOrigin.DefaultFolder);
                    if (Admissible(platform.ProgramFilesX86)) Add(Path.Combine(platform.ProgramFilesX86!, "GOG Galaxy", "Games", "Arcanum"), GameFolderOrigin.DefaultFolder);
                    break;
                case GameFolderPlatformKind.MacOS:
                    if (Admissible(platform.Applications)) Add(Path.Combine(platform.Applications!, "Arcanum"), GameFolderOrigin.DefaultFolder);
                    if (!Admissible(platform.Home)) break;
                    Add(Path.Combine(platform.Home!, "Applications", "Arcanum"), GameFolderOrigin.DefaultFolder);
                    var bottles = Path.Combine(platform.Home!, "Library", "Application Support", "CrossOver", "Bottles");
                    if (!Directory.Exists(bottles)) break;
                    foreach (var bottle in Directory.EnumerateDirectories(bottles).Order(StringComparer.Ordinal).Take(MaxBottles))
                        foreach (var folder in InstallFolders) Add(Path.Combine([bottle, "drive_c", .. folder]), GameFolderOrigin.CrossOverBottle);
                    break;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or NotSupportedException) { }
        return candidates.AsReadOnly();
    }

    /// <summary>Checks every fixed candidate of <paramref name="platform"/>, in order.</summary>
    public static GameFolderDetection Detect(GameFolderPlatform platform)
        => new(Candidates(platform).Select(candidate => Check(candidate.Path, candidate.Origin)).ToArray());

    /// <summary>The folder the player chose: itself when it is a game folder; otherwise, for a Wine wrapper (<c>.app</c>), a bottle
    /// or its <c>drive_c</c>, the first ready GOG folder inside it (origin <see cref="GameFolderOrigin.Wrapper"/>); otherwise the
    /// chosen folder's own refusal.</summary>
    public static GameFolderCheck Choose(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var chosen = Check(path, GameFolderOrigin.Chosen);
        if (chosen.Ready || chosen.Status != GameFolderStatus.MissingTig && chosen.Status != GameFolderStatus.MissingModule) return chosen;
        var drives = new List<string>();
        if (chosen.Path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) drives.AddRange(WrapperDrives.Select(parts => Path.Combine([chosen.Path, .. parts])));
        drives.Add(Path.Combine(chosen.Path, "drive_c"));
        if (string.Equals(Path.GetFileName(chosen.Path), "drive_c", StringComparison.OrdinalIgnoreCase)) drives.Add(chosen.Path);
        foreach (var drive in drives)
            foreach (var folder in InstallFolders)
            {
                var inner = Check(Path.Combine([drive, .. folder]), GameFolderOrigin.Wrapper);
                if (inner.Ready) return inner;
            }
        return chosen;
    }

    /// <summary>Checks one folder: <see cref="ContentMountPlan.Discover"/> of the <c>Arcanum</c> module, then the map inputs, then
    /// the prototype files, then the identity. Never throws for a path; a path that is empty, too long or has a control character is a missing folder.</summary>
    public static GameFolderCheck Check(string path, GameFolderOrigin origin = GameFolderOrigin.Chosen)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Admissible(path)) return new(GameFolderStatus.MissingFolder, path, origin, "path");
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        { return new(GameFolderStatus.MissingFolder, path, origin, "path"); }
        var discovery = ContentMountPlan.Discover(full);
        if (discovery.Status != ContentMountStatus.Ready)
            return new(discovery.Status switch
            {
                ContentMountStatus.MissingGameRoot => GameFolderStatus.MissingFolder,
                ContentMountStatus.MissingTig => GameFolderStatus.MissingTig,
                ContentMountStatus.MissingModule => GameFolderStatus.MissingModule,
                ContentMountStatus.AmbiguousName => GameFolderStatus.AmbiguousName,
                ContentMountStatus.Unreadable => GameFolderStatus.Unreadable,
                _ => GameFolderStatus.UnsupportedLayout,
            }, full, origin, discovery.Detail);
        var plan = discovery.Plan!;
        var module = plan.SearchOrder.FirstOrDefault(source => source.Role == ContentSourceRole.ModuleArchive);
        if (module is null) return new(GameFolderStatus.MissingModuleArchive, full, origin, $"modules/{ContentMountPlan.DefaultModule}.dat", plan);
        var actor = BaseArchive(plan, "arcanum1.dat");
        if (actor is null) return new(GameFolderStatus.MissingActorArchive, full, origin, "arcanum1.dat", plan);
        var terrain = BaseArchive(plan, "arcanum2.dat");
        if (terrain is null) return new(GameFolderStatus.MissingTerrainArchive, full, origin, "arcanum2.dat", plan);
        var archives = plan.SearchOrder.Where(source => source.Kind == ContentSourceKind.Archive).ToArray();
        if (archives.Length > MaxArchives) return new(GameFolderStatus.UnsupportedLayout, full, origin, "archives", plan);
        // The identity stores each archive's name; a name with a control character cannot be stored (or shown).
        if (archives.Any(source => Path.GetFileName(source.Path).Any(char.IsControl)))
            return new(GameFolderStatus.UnsupportedLayout, full, origin, "archive name", plan);
        try
        {
            if (!HasPrototypes(plan)) return new(GameFolderStatus.MissingPrototypes, full, origin, PrototypeFolder, plan);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return new(GameFolderStatus.Unreadable, full, origin, "proto", plan);
        }
        try
        {
            return new(GameFolderStatus.Ready, plan.GameRoot, origin, null, plan, new(plan.GameRoot, module.Path, actor.Path, terrain.Path), Identify(plan));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return new(GameFolderStatus.Unreadable, full, origin, "identity", plan);
        }
    }

    /// <summary>The folder the original writes its prototypes to at its first start (<see cref="GameFolderStatus.MissingPrototypes"/>).</summary>
    public const string PrototypeFolder = "data/proto";

    /// <summary>ARC-DEV-066: whether a base source holds a prototype file <see cref="ContentPrototypes"/> reads. The loose base
    /// folders (the game folder and <c>data/</c>) are looked at first, where the original writes them; only without one is the
    /// stack opened, for prototypes inside a base archive (the installed archives hold none). A stack that does not open holds none.</summary>
    private static bool HasPrototypes(ContentMountPlan plan)
    {
        foreach (var source in plan.SearchOrder.Where(source => source.Kind == ContentSourceKind.Directory && !ContentPrototypes.IsModule(source.Role)))
        {
            var folder = Directory.EnumerateDirectories(source.Path).FirstOrDefault(entry => ContentMountPlan.AsciiLower(Path.GetFileName(entry)) == "proto");
            if (folder is not null && Directory.EnumerateFiles(folder)
                    .Any(file => ContentPrototypes.TryNumber(ContentMountPlan.AsciiLower(Path.GetFileName(file)), out _))) return true;
        }
        var opened = ContentStack.Open(plan);
        if (opened.Status != ContentStackStatus.Ready) return false;
        using var stack = opened.Stack!;
        return ContentPrototypes.Open(stack).Numbers.Count > 0;
    }

    private static ContentSource? BaseArchive(ContentMountPlan plan, string name)
        => plan.SearchOrder.FirstOrDefault(source => source.Role == ContentSourceRole.BaseArchive
                                                     && ContentMountPlan.AsciiLower(Path.GetFileName(source.Path)) == name);

    private static GameFolderIdentity Identify(ContentMountPlan plan)
    {
        var entries = Directory.EnumerateFiles(plan.GameRoot).ToArray();
        string? Find(string name)
        {
            var matches = entries.Where(entry => ContentMountPlan.AsciiLower(Path.GetFileName(entry)) == name).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        string? version = null;
        if (Find("version.txt") is { } versionFile && new FileInfo(versionFile).Length <= MaxVersionBytes)
        {
            // Bytes, not decoded text: a byte outside printable ASCII makes it no version (a decoder would turn it into '?').
            var bytes = File.ReadAllBytes(versionFile);
            var text = Encoding.ASCII.GetString(bytes).Trim();
            if (bytes.All(b => b is >= 0x20 and <= 0x7e or (byte)'\r' or (byte)'\n' or (byte)'\t') && text.Length is > 0 and <= MaxVersionLength
                && text.All(c => c is >= ' ' and <= '~')) version = text;
        }
        long? exeBytes = null; string? exeSha256 = null;
        if (Find("arcanum.exe") is { } exe)
        {
            var length = new FileInfo(exe).Length;
            if (length <= MaxExeBytes)
            {
                using var stream = File.OpenRead(exe);
                exeBytes = length; exeSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }
        }
        var archives = plan.SearchOrder.Reverse()
            .Where(source => source.Kind == ContentSourceKind.Archive)
            .Select(source => new GameArchiveSize(Path.GetRelativePath(plan.GameRoot, source.Path).Replace('\\', '/'), new FileInfo(source.Path).Length))
            .ToArray();
        return new(version, exeBytes, exeSha256, Find(ContentMountPlan.AsciiLower(GogMarkerFile)) is not null, archives);
    }

    private static bool Admissible(string? path)
        => !string.IsNullOrWhiteSpace(path) && path.Length <= MaxPathLength && !path.Any(char.IsControl);
}
