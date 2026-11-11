using System.Diagnostics;
using System.Text.RegularExpressions;
using Arcanum.Data.Hd;

namespace Arcanum.TerrainViewer.Core;

/// <summary>ARC-DEV-350: where the game takes its HD pack from. <see cref="Auto"/> prefers the player's cross-device pack folder (the
/// iCloud copy of ARC-DEV-349) when it exists on this device and the pack in it is complete, so the game always reads the latest copy
/// without a manual apply, and falls back to the installed copy in the game's own folder; <see cref="GameFolder"/> reads only the
/// installed copy; <see cref="Custom"/> reads the folder the player chose.</summary>
public enum HdPackSourceMode { Auto, GameFolder, Custom }

/// <summary>The state of one pack file on this device, read without reading its content (a stat, never a download).</summary>
public enum HdFileState
{
    Local, Missing, WrongSize, OnlineOnly,
    /// <summary>In a cloud folder whose download state could not be read: treated as not safe to read (fail closed).</summary>
    Unverified,
}

/// <summary>Reads the state of a pack file on this device. A probe never reads file content, so it never makes a cloud folder download.</summary>
public interface IHdFileProbe
{
    /// <summary>The state of <paramref name="path"/>; <paramref name="expectedBytes"/> below zero skips the size check.</summary>
    HdFileState Probe(string path, long expectedBytes);
}

/// <summary>The counts of a pack's files by state, and the words for them.</summary>
public sealed record HdPackAvailability(int Files, int Local, int Missing, int WrongSize, int OnlineOnly, int Unverified = 0)
{
    /// <summary>Every file is here, whole and not waiting for a download.</summary>
    public bool Ready => Files > 0 && Local == Files;

    public string Describe()
    {
        if (Ready) return $"{Files} files here";
        var parts = new List<string>();
        if (OnlineOnly > 0) parts.Add($"{OnlineOnly} of {Files} files are online-only in the cloud folder (not downloaded)");
        if (Missing > 0) parts.Add($"{Missing} files are missing");
        if (WrongSize > 0) parts.Add($"{WrongSize} files have another size than the manifest (a sync may still be running)");
        if (Unverified > 0) parts.Add($"{Unverified} files are in a cloud folder whose download state could not be read");
        return parts.Count == 0 ? "the pack has no files" : string.Join("; ", parts);
    }

    /// <summary>Checks every manifest file, the recipe and the unlisted main-menu picture (when the folder has one).</summary>
    public static HdPackAvailability Check(HdPack pack, IHdFileProbe probe)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(probe);
        int files = 0, local = 0, missing = 0, wrong = 0, online = 0, unverified = 0;
        void Count(HdFileState state)
        {
            files++;
            switch (state)
            {
                case HdFileState.Local: local++; break;
                case HdFileState.Missing: missing++; break;
                case HdFileState.WrongSize: wrong++; break;
                case HdFileState.Unverified: unverified++; break;
                default: online++; break;
            }
        }
        foreach (var entry in pack.Entries) Count(probe.Probe(HdPackFiles.Full(pack.Directory, entry.File), entry.Bytes));
        Count(probe.Probe(HdPackFiles.Full(pack.Directory, pack.RecipeFile), -1));
        // The menu picture is outside the manifest: it only counts when the folder has it (or a placeholder of it).
        var menu = probe.Probe(HdPackFiles.Full(pack.Directory, "main-menu-16x9.png"), -1);
        if (menu != HdFileState.Missing) Count(menu);
        return new(files, local, missing, wrong, online, unverified);
    }
}

internal static class HdPackFiles
{
    public static string Full(string root, string relative) => Path.Combine([root, .. relative.Split('/')]);
}

/// <summary>The real probe: a file's length by stat, a missing file with an iCloud placeholder (<c>.name.icloud</c>) next to it as online-only,
/// and on macOS a file whose directory listing says <c>dataless</c> (the file provider keeps it in the cloud only) as online-only. The
/// listing is <c>ls -lO</c> once per directory; where it cannot run, only the first two checks apply.</summary>
public sealed class HdFileProbe : IHdFileProbe
{
    // ls -lO joins flags with commas ("hidden,dataless").
    private static readonly Regex Dataless = new(@"(?<=[\s,])dataless(?=[\s,])", RegexOptions.CultureInvariant);
    private readonly Func<string, string?> _list;
    private readonly Dictionary<string, HashSet<string>?> _online = new(StringComparer.Ordinal);

    public HdFileProbe() : this(OperatingSystem.IsMacOS() ? ListDirectory : _ => null) { }

    /// <summary>A probe over a directory lister (tests): returns the <c>ls -lO</c> text of a directory, or null when it cannot list.</summary>
    public HdFileProbe(Func<string, string?> list) => _list = list ?? throw new ArgumentNullException(nameof(list));

    public HdFileState Probe(string path, long expectedBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            var placeholder = Path.Combine(info.DirectoryName ?? "", "." + info.Name + ".icloud");
            return File.Exists(placeholder) ? HdFileState.OnlineOnly : HdFileState.Missing;
        }
        if (info.LinkTarget is not null) return HdFileState.Missing; // a link inside a pack is refused by the format
        // A file the provider keeps in the cloud reports its real length, so the download state is read first.
        var online = OnlineNames(info.DirectoryName ?? "");
        if (online is null) return IsCloudPath(path) ? HdFileState.Unverified : (expectedBytes >= 0 && info.Length != expectedBytes ? HdFileState.WrongSize : HdFileState.Local);
        if (online.Contains(info.Name)) return HdFileState.OnlineOnly;
        return expectedBytes >= 0 && info.Length != expectedBytes ? HdFileState.WrongSize : HdFileState.Local;
    }

    /// <summary>A path inside iCloud Drive (<c>Mobile Documents</c>) or another file provider's folder (<c>CloudStorage</c>), where a file may be
    /// kept only in the cloud.</summary>
    public static bool IsCloudPath(string path) =>
        path.Contains("/Mobile Documents/", StringComparison.Ordinal) || path.Contains("/Library/CloudStorage/", StringComparison.Ordinal);

    // null: the listing could not run (the caller decides: a cloud folder fails closed, a local one is taken as here).
    private HashSet<string>? OnlineNames(string directory)
    {
        if (_online.TryGetValue(directory, out var known)) return known;
        if (_list(directory) is not { } text) return _online[directory] = null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
            if (line.Length > 0 && line[0] is '-' or 'l' && Dataless.IsMatch(line))
                names.Add(line[(line.LastIndexOf(' ') + 1)..].TrimEnd('\r'));
        return _online[directory] = names;
    }

    // A pack's file names have no spaces (the format's portable names), so the name is the last word of the line. Both streams are read
    // asynchronously (no deadlock on a full pipe) and the process is killed when it does not end in time.
    private static string? ListDirectory(string directory)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/bin/ls", ["-lO", directory])
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            });
            if (process is null) return null;
            var output = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, data) => { if (data.Data is not null) lock (output) output.AppendLine(data.Data); };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            if (!process.WaitForExit(10_000)) { try { process.Kill(true); } catch (InvalidOperationException) { } return null; }
            process.WaitForExit(); // the end of the asynchronous output
            // ls exits 1 when any entry did not stat: the listing is then incomplete, and incomplete means unknown.
            if (process.ExitCode != 0) return null;
            lock (output) return output.ToString();
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { return null; }
    }
}

/// <param name="GameFolder">The game's own pack collection (<c>user://hd-packs</c>): the installed copy.</param>
/// <param name="DefaultFolder">The default cross-device pack collection (the iCloud copy), or null where there is none on this device.</param>
/// <param name="EditorFolder">The checkout's <c>local/hd-packs</c> for a run of the editor binary, else null.</param>
public sealed record HdPackSourceFolders(string GameFolder, string? DefaultFolder, string? EditorFolder)
{
    /// <summary>The iCloud copy's collection under a home folder (ARC-DEV-349, <c>docs/workflow/hd-pack.md</c>), only on macOS and only
    /// when the folder exists on this device: a Windows or another device with no such folder simply has no default.</summary>
    public static string? DefaultIcloudFolder(string home, bool macOs, Func<string, bool> exists)
    {
        if (!macOs || string.IsNullOrEmpty(home)) return null;
        var folder = Path.Combine(home, "Library", "Mobile Documents", "iCloud~md~obsidian", "Documents", "Arcanum Steamwoven", "hd_pack");
        return exists(folder) ? folder : null;
    }
}

/// <summary>The resolver's answer: the pack to open (null: none), where it came from, what was skipped on the way and the pack's file counts.</summary>
public sealed record HdPackSourceChoice(HdDiscoveredPack? Chosen, string Source, IReadOnlyList<string> Skipped, int Found, HdPackAvailability? Availability)
{
    /// <summary>The one line for the log and the HUD: the source, or why none.</summary>
    public string Describe() => Chosen?.Result.Pack is { } pack
        ? $"{Source}, {pack.Id}" + (Skipped.Count == 0 ? "" : $" (fallback: {string.Join(" | ", Skipped)})")
        : $"no pack ({(Skipped.Count == 0 ? "nothing found" : string.Join(" | ", Skipped))})";

    /// <summary>What the launcher screen says after a change: null when the source has nothing to explain.</summary>
    public string Notice() => Chosen?.Result.Pack is { } pack
        ? $"HD pack after a restart: {Source}, {pack.Id}" + (Skipped.Count == 0 ? "" : "; " + string.Join("; ", Skipped))
        : "HD pack after a restart: none; " + (Skipped.Count == 0 ? "no pack found" : string.Join("; ", Skipped));
}

/// <summary>ARC-DEV-350: chooses the active HD pack from the player's setting. The chain is tried in order and the first pack that opens, is
/// not rejected and is complete on this device wins: Auto is the default folder, the game folder, the checkout folder; GameFolder is the
/// game folder alone; Custom is the chosen folder, then the game folder. A pack that opens but is not complete (online-only files in the
/// cloud folder, missing files, a size that is not the manifest's) is skipped with its reason; when nothing complete is left, the first
/// incomplete one whose files are all on this device is used (frames that are not there draw Legacy), never one that would make the
/// game wait for a download. Reads manifests and stats files; never hashes or reads a frame.</summary>
public static class HdPackSourceResolver
{
    public const string DefaultLabel = "iCloud copy";
    public const string GameLabel = "game folder";
    public const string CustomLabel = "custom folder";
    public const string EditorLabel = "checkout local/hd-packs";

    private sealed record Candidate(string Label, IReadOnlyList<HdPackLocation> Locations);

    public static HdPackSourceChoice Resolve(HdPackSourceMode mode, string? customPath, HdPackSourceFolders folders, IHdFileProbe probe, HdPackOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(probe);
        var skipped = new List<string>();
        var chain = new List<Candidate>();
        if (mode == HdPackSourceMode.Custom)
        {
            if (string.IsNullOrWhiteSpace(customPath)) skipped.Add("no custom folder chosen");
            else chain.Add(new(CustomLabel, [CustomLocation(customPath)]));
        }
        else if (mode == HdPackSourceMode.Auto && folders.DefaultFolder is { Length: > 0 } icloud)
            chain.Add(new(DefaultLabel, [new(HdPackLocationKind.Collection, icloud)]));
        chain.Add(new(GameLabel, [new(HdPackLocationKind.Collection, folders.GameFolder)]));
        if (mode != HdPackSourceMode.GameFolder && folders.EditorFolder is { Length: > 0 } editor)
            chain.Add(new(EditorLabel, [new(HdPackLocationKind.Collection, editor)]));

        var found = 0;
        HdPackSourceChoice? partial = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in chain)
        {
            // The same folder reached by a link (local/hd-packs is a link to the iCloud folder) counts once.
            if (!seen.Add(RealPath(candidate.Locations[0].Path))) continue;
            // The manifest and the recipe are read when a pack opens, so their own state is checked first: an online-only one would make the
            // game wait for a download here (the startup, and every change of the line).
            var (ready, notes) = Expand(candidate, probe, options);
            skipped.AddRange(notes);
            var listed = HdPack.Discover(ready, options).ToArray();
            found += listed.Length;
            if (listed.Length == 0) { if (notes.Count == 0) skipped.Add($"{candidate.Label}: no pack found"); continue; }
            // The first pack that opens and is not rejected, then the others in order (a whole pack beside an incomplete one is not missed).
            var opened = listed.Where(pack => pack.Result.Status == HdPackOpenStatus.Opened && pack.Result.Pack is not null).ToArray();
            var order = opened.Where(pack => pack.Result.Pack!.Review != HdReviewStatus.Rejected).Concat(opened.Where(pack => pack.Result.Pack!.Review == HdReviewStatus.Rejected)).ToArray();
            if (order.Length == 0) { skipped.Add($"{candidate.Label}: {listed.Length} found, none opened ({listed[0].Result.Status})"); continue; }
            foreach (var active in order)
            {
                var pack = active.Result.Pack!;
                var availability = HdPackAvailability.Check(pack, probe);
                if (availability.Ready) return new(active, candidate.Label, skipped, found, availability);
                skipped.Add($"{candidate.Label} skipped: {availability.Describe()}");
                if (partial is null && availability.OnlineOnly == 0 && availability.Unverified == 0) partial = new(active, candidate.Label + " (incomplete)", [], found, availability);
            }
        }
        return partial is { } use ? use with { Skipped = skipped, Found = found } : new(null, "none", skipped, found, null);
    }

    // The pack folders of a candidate whose manifest and recipe are here (not online-only, not unverified in a cloud folder), with the
    // reasons for the others. Pack folders are listed as Discover does (no hidden folder, ordinal order, at most MaxPacksPerCollection).
    private static (IReadOnlyList<HdPackLocation> Ready, IReadOnlyList<string> Notes) Expand(Candidate candidate, IHdFileProbe probe, HdPackOptions? options)
    {
        var ready = new List<HdPackLocation>(); var notes = new List<string>();
        var location = candidate.Locations[0];
        IEnumerable<string> packs;
        if (location.Kind == HdPackLocationKind.Pack) packs = Directory.Exists(location.Path) ? [Path.GetFullPath(location.Path)] : [];
        else
        {
            var root = Path.GetFullPath(location.Path);
            if (!Directory.Exists(root)) return (ready, notes);
            try
            {
                packs = Directory.GetDirectories(root).Where(c => !Path.GetFileName(c).StartsWith('.')).Order(StringComparer.Ordinal)
                    .Where(c => File.Exists(Path.Combine(c, HdPack.ManifestFile)) || new FileInfo(Path.Combine(c, HdPack.ManifestFile)).LinkTarget is not null
                        || File.Exists(Path.Combine(c, "." + HdPack.ManifestFile + ".icloud"))).Take((options ?? new()).MaxPacksPerCollection).ToArray();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return (ready, notes); }
        }
        foreach (var directory in packs)
        {
            var bad = new List<string>();
            var files = new List<string> { Path.Combine(directory, HdPack.ManifestFile) };
            try { files.AddRange(Directory.GetFiles(directory, "*.json").Where(f => !string.Equals(Path.GetFileName(f), HdPack.ManifestFile, StringComparison.Ordinal))); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            foreach (var file in files)
            {
                var state = probe.Probe(file, -1);
                if (state is HdFileState.OnlineOnly or HdFileState.Unverified) bad.Add(Path.GetFileName(file) + (state == HdFileState.OnlineOnly ? " is online-only" : " download state unknown"));
            }
            if (bad.Count == 0) ready.Add(new(HdPackLocationKind.Pack, directory));
            else notes.Add($"{candidate.Label} skipped: {Path.GetFileName(directory)}: {string.Join(", ", bad)} (not downloaded)");
        }
        return (ready, notes);
    }

    // A chosen folder that holds a manifest is the pack itself; any other folder is a collection of packs.
    private static HdPackLocation CustomLocation(string path) =>
        new(File.Exists(Path.Combine(path, HdPack.ManifestFile)) ? HdPackLocationKind.Pack : HdPackLocationKind.Collection, path);

    private static string RealPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return new DirectoryInfo(full).ResolveLinkTarget(true)?.FullName ?? full;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return path; }
    }

    public static string ModeName(HdPackSourceMode mode) => mode switch
    {
        HdPackSourceMode.GameFolder => "game", HdPackSourceMode.Custom => "custom", _ => "auto",
    };

    public static bool TryParseMode(string? name, out HdPackSourceMode mode)
    {
        mode = name switch { "auto" => HdPackSourceMode.Auto, "game" => HdPackSourceMode.GameFolder, "custom" => HdPackSourceMode.Custom, _ => (HdPackSourceMode)(-1) };
        return Enum.IsDefined(mode);
    }
}

/// <summary>ARC-DEV-350: whether the HD pack situation at startup is an error the player must be told about, and what the run does about it.
/// Only a player who asked for HD (the HD graphics profile or the HD interface setting) is told; the fallback is Legacy graphics for this run
/// (the saved settings are not touched, so a repaired pack brings HD back at the next start).</summary>
/// <param name="Message">The text for the log and the on-screen box; null when there is nothing to report.</param>
/// <param name="DropPack">True when the pack is not used and the run draws Legacy.</param>
public sealed record HdPackProblem(string? Message, bool DropPack)
{
    public static readonly HdPackProblem None = new(null, false);

    /// <param name="wantsHd">The HD graphics profile or the HD interface setting is on.</param>
    /// <param name="fromEnvironment">The pack came from <c>ARC_HD_PACK</c> rather than from the source setting.</param>
    /// <param name="status">The open status of the chosen pack, or of the environment pack (<c>None</c>: nothing was found).</param>
    public static HdPackProblem Check(bool wantsHd, bool fromEnvironment, HdPack? pack, string openStatus, HdPackSourceChoice? choice)
    {
        if (!wantsHd) return None;
        if (pack is null)
        {
            var why = fromEnvironment ? $"the pack named by ARC_HD_PACK did not open ({openStatus})"
                : choice is null ? "no pack was searched" : choice.Describe();
            return new($"The HD pack cannot be used: {why}. The game continues with the original (Legacy) graphics.", true);
        }
        if (pack.Review == HdReviewStatus.Rejected)
            return new($"The HD pack {pack.Id} is marked as rejected and is not used. The game continues with the original (Legacy) graphics.", true);
        if (choice?.Availability is { Ready: false } incomplete && choice.Source.EndsWith("(incomplete)", StringComparison.Ordinal))
            return new($"The HD pack {pack.Id} is incomplete ({incomplete.Describe()}). Frames that are not there are drawn with the original (Legacy) graphics.", false);
        return None;
    }
}
