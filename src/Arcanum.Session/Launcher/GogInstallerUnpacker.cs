using System.Diagnostics;
using System.Text.RegularExpressions;
using Arcanum.Data.Content;

namespace Arcanum.Session.Launcher;

/// <summary>Where the unpack is: the installer is identified and listed, its checksums verified, its files written, the folder identified.</summary>
public enum GogInstallStage { Checking, Verifying, Unpacking, Identifying }

/// <summary>One step of the unpack. The files and bytes are those the listing promised (<c>--exclude-temp</c> files only); the counts
/// move while <see cref="GogInstallStage.Verifying"/> and <see cref="GogInstallStage.Unpacking"/> run, one file at a time.</summary>
public sealed record GogInstallProgress(GogInstallStage Stage, int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string? File);

/// <summary>How an unpack ended. Only <see cref="Unpacked"/> leaves a game folder to use; after <see cref="ExtractFailed"/>,
/// <see cref="NotAGameFolder"/>, <see cref="FingerprintMismatch"/> and <see cref="Cancelled"/> the target may hold part of the files.</summary>
public enum GogInstallStatus
{
    Unpacked, InvalidPath, InstallerMissing, ToolFailed, NotArcanumInstaller, MissingParts, TargetNotEmpty, NotEnoughSpace,
    VerifyFailed, ExtractFailed, NotAGameFolder, FingerprintMismatch, Cancelled,
}

/// <param name="Detail">What was refused or failed (a name, a size or the tool's first error line; never bytes of the game).</param>
/// <param name="Folder">The checked game folder of an <see cref="GogInstallStatus.Unpacked"/> result; null otherwise.</param>
public sealed record GogInstallResult(GogInstallStatus Status, string? Detail = null, GameFolderCheck? Folder = null)
{
    public bool Succeeded => Status == GogInstallStatus.Unpacked;
}

/// <summary>What one tool run gave: the exit code (-1 when the tool could not be started, then <see cref="Started"/> is false) and its
/// output lines, standard output and error together, at most <see cref="GogInstallerUnpacker.MaxToolLines"/>.</summary>
public sealed record GogToolOutcome(int ExitCode, IReadOnlyList<string> Lines, bool Started = true);

/// <summary>Runs <paramref name="tool"/> with <paramref name="arguments"/> (never through a shell), reports each output line as it comes
/// and stops the tool when <paramref name="cancel"/> is cancelled. Tests pass a synthetic one.</summary>
public delegate Task<GogToolOutcome> GogToolRunner(string tool, IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken cancel);

/// <summary>The unpack's limits and stand-ins. The defaults are the retail 1.0.7.4 executable and this machine's disk.</summary>
public sealed record GogInstallOptions
{
    /// <summary>The SHA-256 the unpacked <c>Arcanum.exe</c> must have (the root, not <c>app/</c>).</summary>
    public string ExpectedExeSha256 { get; init; } = GameFolderLocator.VerifiedExeSha256;
    /// <summary>Free space kept on top of the listed size.</summary>
    public long SpaceSlackBytes { get; init; } = 64L * 1024 * 1024;
    /// <summary>The free bytes on the volume holding an existing folder; null when unknown (the check is then skipped).</summary>
    public Func<string, long?> FreeSpace { get; init; } = GogInstallerUnpacker.DiskFreeSpace;
}

/// <summary>
/// ARC-DEV-074: turns the GOG offline installer (<c>setup_arcanum_….exe</c>, Inno Setup 5.6.2, one file) into a game folder without
/// Windows, by calling the free <c>innoextract</c> tool (zlib licence; research ARC-RES-087, option A). It checks the installer is
/// GOG's Arcanum (game id <see cref="GameFolderLocator.GogProductId"/>), lists the files it will write and checks the free space,
/// verifies the installer's checksums (<c>--test</c>), unpacks into a folder that is new or empty (the installer's folder is never
/// written), then checks the result with <see cref="GameFolderLocator.Check"/> and the fingerprint of <c>Arcanum.exe</c> in the root.
/// It builds no prototypes (that is E3/E4) and never changes the installer. The tool is found on <c>PATH</c> and the usual package
/// folders (<see cref="FindTool"/>); a failure is a typed <see cref="GogInstallResult"/>, not an exception. Godot-independent.
/// </summary>
public sealed class GogInstallerUnpacker
{
    /// <summary>The tool's output is kept to this many lines (each line to <see cref="MaxLineLength"/> characters).</summary>
    public const int MaxToolLines = 2000;
    public const int MaxLineLength = 512;
    public const string ToolName = "innoextract";
    /// <summary>The folders besides <c>PATH</c> the tool is looked for in (a started application has a short <c>PATH</c>): Homebrew
    /// (Apple silicon, Intel), MacPorts, the system.</summary>
    public static readonly IReadOnlyList<string> PackageFolders = ["/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin", "/usr/bin"];

    private static readonly string[] CommonFlags = ["--gog", "--exclude-temp"];
    private static readonly Regex GameId = new(@"game ID is (?<id>\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex SizeLine = new(@"^(?<bytes>\d+) (?<name>.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex FileLine = new("^\\s*-\\s+\"(?<name>[^\"]+)\"", RegexOptions.CultureInvariant);

    private readonly string _tool;
    private readonly GogToolRunner _run;
    private readonly GogInstallOptions _options;

    public GogInstallerUnpacker(string tool, GogToolRunner? runner = null, GogInstallOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        _tool = tool;
        _run = runner ?? RunProcessAsync;
        _options = options ?? new GogInstallOptions();
    }

    /// <summary>The full path of <c>innoextract</c> (<c>innoextract.exe</c> on Windows): the first match of the absolute folders of
    /// <paramref name="pathVariable"/> (this process's <c>PATH</c> when null), then <see cref="PackageFolders"/> (not on Windows); null
    /// when there is none. A relative <c>PATH</c> entry is skipped (it would resolve against the working folder).
    /// <paramref name="isExecutable"/> stands in for the file check in tests.</summary>
    public static string? FindTool(string? pathVariable = null, bool? windows = null, Func<string, bool>? isExecutable = null)
    {
        var onWindows = windows ?? OperatingSystem.IsWindows();
        var check = isExecutable ?? IsExecutableFile;
        var name = onWindows ? ToolName + ".exe" : ToolName;
        var folders = (pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim().Trim('"')).Concat(onWindows ? [] : PackageFolders);
        foreach (var folder in folders)
        {
            try
            {
                if (folder.Length == 0 || folder.Any(char.IsControl) || !Path.IsPathFullyQualified(folder)) continue;
                var candidate = Path.Combine(folder, name);
                if (check(candidate)) return candidate;
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path)) return false;
        return OperatingSystem.IsWindows() || (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    /// <summary>The free bytes on the volume of <paramref name="existingFolder"/>; null when they cannot be read.</summary>
    public static long? DiskFreeSpace(string existingFolder)
    {
        try { return new DriveInfo(existingFolder).AvailableFreeSpace; }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    /// <summary>Unpacks <paramref name="installerPath"/> into <paramref name="targetFolder"/> (a new folder whose parent exists, or an
    /// empty one). Progress is reported from the thread that produced it. Cancelling stops the tool and gives
    /// <see cref="GogInstallStatus.Cancelled"/>.</summary>
    public async Task<GogInstallResult> UnpackAsync(string installerPath, string targetFolder, Action<GogInstallProgress>? progress = null,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(installerPath);
        ArgumentNullException.ThrowIfNull(targetFolder);
        if (!Admissible(installerPath) || !Admissible(targetFolder)) return new(GogInstallStatus.InvalidPath, "path");
        string installer, target;
        try
        {
            installer = Path.GetFullPath(installerPath);
            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetFolder));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return new(GogInstallStatus.InvalidPath, "path");
        }
        if (!File.Exists(installer)) return new(GogInstallStatus.InstallerMissing, installer);
        if (CheckTarget(target) is { } refused) return refused;

        progress?.Invoke(new(GogInstallStage.Checking, 0, 0, 0, 0, null));
        // 1. Is it GOG's Arcanum installer?
        var identity = await Run(["--gog-game-id", "--progress=0", "--color=0", installer], null, cancel);
        if (cancel.IsCancellationRequested) return new(GogInstallStatus.Cancelled);
        if (!identity.Started) return new(GogInstallStatus.ToolFailed, Describe(identity));
        if (identity.ExitCode != 0) return new(GogInstallStatus.NotArcanumInstaller, FirstError(identity));
        var id = identity.Lines.Select(line => GameId.Match(line)).FirstOrDefault(match => match.Success)?.Groups["id"].Value;
        if (id != GameFolderLocator.GogProductId) return new(GogInstallStatus.NotArcanumInstaller, id is null ? "no GOG game id" : "GOG game id " + id);

        // 2. What will be written, and is there room?
        var listing = await Run(["--list", "--list-sizes", .. CommonFlags, "--silent", "--progress=0", "--color=0", installer], null, cancel);
        if (cancel.IsCancellationRequested) return new(GogInstallStatus.Cancelled);
        if (listing.ExitCode != 0) return Failed(GogInstallStatus.ExtractFailed, listing);
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in listing.Lines)
            if (SizeLine.Match(line) is { Success: true } match && long.TryParse(match.Groups["bytes"].Value, out var bytes)) sizes[match.Groups["name"].Value.Trim()] = bytes;
        if (sizes.Count == 0) return new(GogInstallStatus.ExtractFailed, "the installer lists no files");
        var total = sizes.Values.Sum();
        var existing = target;
        while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent) existing = parent;
        if (_options.FreeSpace(existing) is { } free && free < total + _options.SpaceSlackBytes)
            return new(GogInstallStatus.NotEnoughSpace, $"{Megabytes(total + _options.SpaceSlackBytes)} MB needed, {Megabytes(free)} MB free");

        // 3. Verify the installer's checksums before anything is written.
        var verify = await Run(["--test", .. CommonFlags, "--quiet", "--progress=0", "--color=0", installer], Reporter(GogInstallStage.Verifying), cancel);
        if (cancel.IsCancellationRequested) return new(GogInstallStatus.Cancelled);
        if (verify.ExitCode != 0) return Failed(GogInstallStatus.VerifyFailed, verify);

        // 4. Write the files.
        try { Directory.CreateDirectory(target); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new(GogInstallStatus.InvalidPath, "the target folder cannot be created");
        }
        var extract = await Run(["--extract", .. CommonFlags, "--quiet", "--progress=0", "--color=0", "--output-dir", target, installer], Reporter(GogInstallStage.Unpacking), cancel);
        if (cancel.IsCancellationRequested) return new(GogInstallStatus.Cancelled);
        if (extract.ExitCode != 0) return Failed(GogInstallStatus.ExtractFailed, extract);

        // 5. Is the result a game folder with the verified executable (in the root)?
        progress?.Invoke(new(GogInstallStage.Identifying, sizes.Count, sizes.Count, total, total, null));
        var check = GameFolderLocator.Check(target);
        if (!check.Ready) return new(GogInstallStatus.NotAGameFolder, check.Status + (check.Detail is { Length: > 0 } detail ? " " + detail : ""));
        if (check.Identity!.ExeSha256 is null) return new(GogInstallStatus.FingerprintMismatch, "no Arcanum.exe in the folder root");
        if (!string.Equals(check.Identity.ExeSha256, _options.ExpectedExeSha256, StringComparison.Ordinal))
            return new(GogInstallStatus.FingerprintMismatch, "Arcanum.exe is not the verified 1.0.7.4 executable");
        return new(GogInstallStatus.Unpacked, null, check);

        Task<GogToolOutcome> Run(string[] arguments, Action<string>? onLine, CancellationToken token) => _run(_tool, arguments, onLine ?? (static _ => { }), token);

        // The listing's file names map to sizes; a file line moves the counts when its name is known (a name is counted once).
        Action<string> Reporter(GogInstallStage stage)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var done = 0L;
            var gate = new object();
            return line =>
            {
                if (progress is null || FileLine.Match(line) is not { Success: true } match) return;
                var name = match.Groups["name"].Value;
                GogInstallProgress report;
                lock (gate)
                {
                    if (seen.Add(name) && sizes.TryGetValue(name, out var bytes)) done += bytes;
                    report = new(stage, seen.Count, sizes.Count, done, total, name);
                }
                progress(report);
            };
        }
    }

    /// <summary>A target that is a file, is not empty, or is new without an existing parent is refused (null when it can be used).</summary>
    private static GogInstallResult? CheckTarget(string target)
    {
        try
        {
            if (File.Exists(target)) return new(GogInstallStatus.InvalidPath, "the target is a file");
            if (Directory.Exists(target))
                return Directory.EnumerateFileSystemEntries(target).Any() ? new(GogInstallStatus.TargetNotEmpty, target) : null;
            var parent = Path.GetDirectoryName(target);
            return parent is not null && Directory.Exists(parent) ? null : new(GogInstallStatus.InvalidPath, "the parent of the target folder does not exist");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return new(GogInstallStatus.InvalidPath, "the target folder cannot be read");
        }
    }

    private static GogInstallResult Failed(GogInstallStatus status, GogToolOutcome outcome)
    {
        if (!outcome.Started) return new(GogInstallStatus.ToolFailed, Describe(outcome));
        // A multi-part installer (-1.bin next to the .exe) whose parts are not beside it: the tool names the missing slice.
        if (outcome.Lines.Any(line => line.Contains(".bin", StringComparison.OrdinalIgnoreCase)))
            return new(GogInstallStatus.MissingParts, FirstError(outcome));
        return new(status, FirstError(outcome));
    }

    private static string Describe(GogToolOutcome outcome) => outcome.Started ? FirstError(outcome) : (outcome.Lines.Count > 0 ? Sanitize(outcome.Lines[0]) : "the tool cannot be started");

    /// <summary>The first line naming an error, else the last line that is not a file line, else the exit code; one short printable line.</summary>
    internal static string FirstError(GogToolOutcome outcome)
    {
        var lines = outcome.Lines.Select(Sanitize).Where(line => line.Length > 0).ToArray();
        return lines.FirstOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase))
               ?? lines.LastOrDefault(line => !FileLine.IsMatch(line) && line != "Done.")
               ?? $"exit code {outcome.ExitCode}";
    }

    private static string Sanitize(string line)
    {
        var clean = new string(line.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length <= 200 ? clean : clean[..200];
    }

    private static long Megabytes(long bytes) => (bytes + 1024 * 1024 - 1) / (1024 * 1024);

    private static bool Admissible(string path) => !string.IsNullOrWhiteSpace(path) && path.Length <= GameFolderLocator.MaxPathLength && !path.Any(char.IsControl);

    /// <summary>The default runner: starts the tool without a shell (arguments as a list), no console window, no input, and reads both
    /// output streams line by line. Cancelling kills the tool and its children.</summary>
    public static async Task<GogToolOutcome> RunProcessAsync(string tool, IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(onLine);
        var info = new ProcessStartInfo(tool)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var lines = new List<string>();
        void Add(string? data)
        {
            if (data is null) return;
            var line = data.Length <= MaxLineLength ? data : data[..MaxLineLength];
            lock (lines) { if (lines.Count < MaxToolLines) lines.Add(line); }
            onLine(line);
        }
        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => Add(e.Data);
        process.ErrorDataReceived += (_, e) => Add(e.Data);
        try
        {
            if (!process.Start()) return new(-1, ["the tool cannot be started"], false);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new(-1, ["the tool cannot be started: " + error.Message], false);
        }
        try { process.StandardInput.Close(); } catch (IOException) { }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var registration = cancel.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        process.WaitForExit(); // the output events are all delivered once the streams end
        lock (lines) return new(process.ExitCode, lines.ToArray());
    }
}
