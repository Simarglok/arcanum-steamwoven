using System.Runtime.Versioning;
using Arcanum.Data.Content;

namespace Arcanum.Session.Launcher;

/// <summary>The machine facts <see cref="SteamDepot"/> reads: the platform, the home folder (macOS), the Windows 32-bit program folder
/// and the reader of Steam's install folder in the Windows registry (null when absent). Tests pass a synthetic one.</summary>
public sealed record SteamPlatform(GameFolderPlatformKind Kind, string? Home, string? ProgramFilesX86, Func<string?> InstallPath)
{
    /// <summary>This machine: the Windows registry is read only on Windows (a failed read is no path).</summary>
    public static SteamPlatform Current()
    {
        if (OperatingSystem.IsWindows())
            return new(GameFolderPlatformKind.Windows, null, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), WindowsInstallPath);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new(OperatingSystem.IsMacOS() ? GameFolderPlatformKind.MacOS : GameFolderPlatformKind.Other,
            string.IsNullOrEmpty(home) ? null : home, null, static () => null);
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsInstallPath()
    {
        // Only the install folder values are read: the user's own key first, then the machine's 32-bit and plain views.
        (Microsoft.Win32.RegistryKey Hive, string Key, string Value)[] places =
        [
            (Microsoft.Win32.Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
        ];
        foreach (var (hive, key, value) in places)
        {
            try
            {
                using var opened = hive.OpenSubKey(key);
                if (opened?.GetValue(value) is string path && path.Length > 0) return path;
            }
            catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        }
        return null;
    }
}

/// <summary>
/// ARC-DEV-3515: the player's own Steam copy of Arcanum (app 500810, a Windows release with one depot, 500811). The player downloads
/// that depot with the Steam client's console (<see cref="ConsoleCommand"/>); Steam writes it to
/// <c>steamapps/content/app_500810/depot_500811</c> under its own folder (on macOS inside the client's <c>Steam.AppBundle</c>). This
/// class only names the command and looks at that fixed list of folders; it reads no Steam settings or account file and never asks
/// for or holds a credential.
/// </summary>
public static class SteamDepot
{
    public const string AppId = "500810";
    public const string DepotId = "500811";
    public const string ConsoleUri = "steam://open/console";
    public const string ConsoleCommand = "download_depot " + AppId + " " + DepotId;
    /// <summary>The bytes of the files of the depot's public manifest (6629016675592712844, Steam's app information read on
    /// 2026-11-28); a download with fewer than <see cref="SteamImportOptions.MinimumBytes"/> is not finished.</summary>
    public const long ManifestBytes = 1_199_679_598;

    /// <summary>The depot's folder under a Steam folder.</summary>
    public static readonly IReadOnlyList<string> ContentParts = ["steamapps", "content", "app_" + AppId, "depot_" + DepotId];

    private static readonly string[] MacBundle = ["Library", "Application Support", "Steam", "Steam.AppBundle", "Steam", "Contents", "MacOS"];
    private static readonly string[] MacPlain = ["Library", "Application Support", "Steam"];

    /// <summary>The fixed folders the downloaded depot may be in, in the order they are tried, each once. Windows: under the Steam
    /// folder of the registry, then under <c>Program Files (x86)\Steam</c>. macOS: under the client's bundle, under the Steam data
    /// folder, then under a Windows Steam in each CrossOver bottle. Elsewhere none.</summary>
    public static IReadOnlyList<string> Candidates(SteamPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        var candidates = new List<string>();
        void Add(string? steamFolder)
        {
            if (!Admissible(steamFolder)) return;
            // A relative folder (a damaged registry value) would resolve against the working folder, so it is not looked at.
            if (!Path.IsPathFullyQualified(steamFolder!)) return;
            var full = Path.GetFullPath(Path.Combine([steamFolder!, .. ContentParts]));
            if (!candidates.Contains(full, StringComparer.OrdinalIgnoreCase)) candidates.Add(full);
        }
        try
        {
            switch (platform.Kind)
            {
                case GameFolderPlatformKind.Windows:
                    Add(platform.InstallPath());
                    if (Admissible(platform.ProgramFilesX86)) Add(Path.Combine(platform.ProgramFilesX86!, "Steam"));
                    break;
                case GameFolderPlatformKind.MacOS:
                    if (!Admissible(platform.Home)) break;
                    Add(Path.Combine([platform.Home!, .. MacBundle]));
                    Add(Path.Combine([platform.Home!, .. MacPlain]));
                    var bottles = Path.Combine(platform.Home!, "Library", "Application Support", "CrossOver", "Bottles");
                    if (!Directory.Exists(bottles)) break;
                    foreach (var bottle in Directory.EnumerateDirectories(bottles).Order(StringComparer.Ordinal).Take(GameFolderLocator.MaxBottles))
                        Add(Path.Combine(bottle, "drive_c", "Program Files (x86)", "Steam"));
                    break;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or NotSupportedException) { }
        return candidates.AsReadOnly();
    }

    /// <summary>The downloaded depot's folder: of the candidates that are game folders the one holding the most bytes (so a smaller,
    /// abandoned download does not hide a finished one; equal ones keep their order), else the first that exists (so the import can say
    /// what it lacks); null when none exists.</summary>
    public static string? Find(SteamPlatform platform)
    {
        var existing = Candidates(platform).Where(Directory.Exists).ToArray();
        var ready = existing.Where(folder => GameFolderLocator.Check(folder).Ready).ToArray();
        return ready.Length > 1 ? ready.OrderByDescending(Bytes).First() : ready.FirstOrDefault() ?? existing.FirstOrDefault();
    }

    private static long Bytes(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 16 })
                .Sum(file => file.Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return 0; }
    }

    private static bool Admissible(string? path)
        => !string.IsNullOrWhiteSpace(path) && path.Length <= GameFolderLocator.MaxPathLength && !path.Any(char.IsControl);
}
