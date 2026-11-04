using System.Security.Cryptography;
using Arcanum.Data.Content;
using Arcanum.Session.Launcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-074: the GOG installer unpack over a stand-in for <c>innoextract</c> and synthetic files (no original content,
/// no real tool); the real tool runs only in the opt-in real-data test.</summary>
[TestClass]
public sealed class GogInstallerUnpackerTests
{
    private string _root = null!;
    private string _installer = null!;

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-gog-unpack-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(_root, "downloads"));
        File.WriteAllBytes(_installer = Path.Combine(_root, "downloads", "setup_arcanum_synthetic.exe"), [1, 2, 3]);
    }

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private static readonly byte[] ExeBytes = Enumerable.Range(0, 13).Select(i => (byte)(i + 1)).ToArray();
    private static readonly string ExeSha = Convert.ToHexString(SHA256.HashData(ExeBytes)).ToLowerInvariant();

    private static GogInstallOptions Options(Func<string, long?>? space = null) => new() { ExpectedExeSha256 = ExeSha, FreeSpace = space ?? (_ => long.MaxValue) };

    /// <summary>A stand-in for the tool: answers the four runs the unpack makes (game id, list, test, extract) from fields, records
    /// every call and writes the synthetic files an extraction would.</summary>
    private sealed class FakeTool
    {
        public string[] GameIdLines { get; init; } = ["Inspecting \"Arcanum\" - setup data version 5.6.2 (unicode)", "GOG.com game ID is 1207658829", "Done."];
        public int GameIdExit { get; init; }
        public int ListExit { get; init; }
        public int TestExit { get; init; }
        public int ExtractExit { get; init; }
        public string[] FailureLines { get; init; } = [];
        public bool CannotStart { get; init; }
        public (string Name, byte[] Bytes)[] Files { get; init; } =
        [
            ("tig.dat", [1, 2, 3]), ("arcanum1.dat", [1, 2, 3, 4, 5]), ("arcanum2.dat", [1, 2, 3, 4, 5, 6, 7]), ("modules/Arcanum.dat", new byte[11]),
            ("Arcanum.exe", ExeBytes), (GameFolderLocator.GogMarkerFile, [1]),
        ];
        public readonly List<string[]> Calls = [];
        public Func<CancellationToken, Task>? OnExtract { get; init; }

        public GogToolRunner Runner => async (tool, arguments, onLine, cancel) =>
        {
            Calls.Add([tool, .. arguments]);
            if (CannotStart) return new(-1, ["the tool cannot be started: no such file"], false);
            List<string> lines = [];
            void Emit(string line) { lines.Add(line); onLine(line); }
            switch (arguments[0])
            {
                case "--gog-game-id":
                    foreach (var line in GameIdLines) Emit(line);
                    return new(GameIdExit, lines);
                case "--list":
                    foreach (var (name, bytes) in Files) Emit($"{bytes.Length} {name}");
                    if (ListExit != 0) foreach (var line in FailureLines) Emit(line);
                    return new(ListExit, lines);
                case "--test":
                    foreach (var (name, _) in Files) Emit($" - \"{name}\" [en-US]");
                    if (TestExit != 0) foreach (var line in FailureLines) Emit(line);
                    return new(TestExit, lines);
                default:
                    var target = arguments[arguments.ToList().IndexOf("--output-dir") + 1];
                    if (OnExtract is not null) await OnExtract(cancel);
                    foreach (var (name, bytes) in Files)
                    {
                        Emit($" - \"{name}\"");
                        var path = Path.Combine(target, name);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, bytes);
                    }
                    if (ExtractExit != 0) foreach (var line in FailureLines) Emit(line);
                    return new(ExtractExit, lines);
            }
        };

        public string[] Actions => [.. Calls.Select(call => call[1])];
    }

    private GogInstallerUnpacker Unpacker(FakeTool tool, GogInstallOptions? options = null) => new("/stub/innoextract", tool.Runner, options ?? Options());

    private string Target(string name = "game") => Path.Combine(_root, name);

    [TestMethod]
    public async Task A_synthetic_installer_unpacks_into_a_new_folder_and_the_folder_is_ready()
    {
        var tool = new FakeTool();
        var reports = new List<GogInstallProgress>();
        var result = await Unpacker(tool).UnpackAsync(_installer, Target(), reports.Add);

        Assert.AreEqual(GogInstallStatus.Unpacked, result.Status, result.Detail);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(Target(), result.Folder!.Path);
        Assert.IsTrue(result.Folder.Ready);
        Assert.AreEqual(ExeSha, result.Folder.Identity!.ExeSha256);
        Assert.IsTrue(result.Folder.Identity.GogMarker);
        CollectionAssert.AreEqual(new[] { "--gog-game-id", "--list", "--test", "--extract" }, tool.Actions, "identify, list, verify, then write");
        Assert.AreEqual(_installer, tool.Calls[^1][^1], "the installer path is one argument, last");
        Assert.AreEqual(Target(), tool.Calls[^1][tool.Calls[^1].ToList().IndexOf("--output-dir") + 1]);
        Assert.IsTrue(tool.Calls.All(call => call[0] == "/stub/innoextract" && call.Skip(1).All(argument => !argument.Contains(' ') || argument == _installer || argument == Target())),
            "no shell string");
        foreach (var stage in new[] { GogInstallStage.Checking, GogInstallStage.Verifying, GogInstallStage.Unpacking, GogInstallStage.Identifying })
            Assert.IsTrue(reports.Any(report => report.Stage == stage), "stage " + stage);
        var unpacking = reports.Where(report => report.Stage == GogInstallStage.Unpacking).ToArray();
        Assert.AreEqual(6, unpacking.Length);
        Assert.AreEqual(6, unpacking[^1].FilesDone);
        Assert.AreEqual(6, unpacking[^1].FilesTotal);
        Assert.AreEqual(unpacking[^1].BytesTotal, unpacking[^1].BytesDone, "every listed byte was written");
        CollectionAssert.AreEqual(unpacking.Select(report => report.FilesDone).Order().ToArray(), unpacking.Select(report => report.FilesDone).ToArray());
        CollectionAssert.AreEqual(new[] { (byte)1, (byte)2, (byte)3 }, File.ReadAllBytes(_installer), "the installer is never changed");
    }

    [TestMethod]
    public async Task An_empty_existing_folder_is_used()
    {
        Directory.CreateDirectory(Target());
        var result = await Unpacker(new FakeTool()).UnpackAsync(_installer, Target() + Path.DirectorySeparatorChar);
        Assert.AreEqual(GogInstallStatus.Unpacked, result.Status, result.Detail);
    }

    [TestMethod]
    public async Task A_folder_with_anything_in_it_is_refused_before_the_tool_runs()
    {
        Directory.CreateDirectory(Target());
        File.WriteAllText(Path.Combine(Target(), "mine.txt"), "x");
        var tool = new FakeTool();
        Assert.AreEqual(GogInstallStatus.TargetNotEmpty, (await Unpacker(tool).UnpackAsync(_installer, Target())).Status);
        // The installer's own folder holds the installer, so it can never be the target.
        Assert.AreEqual(GogInstallStatus.TargetNotEmpty, (await Unpacker(tool).UnpackAsync(_installer, Path.GetDirectoryName(_installer)!)).Status);
        Assert.AreEqual(0, tool.Calls.Count, "nothing was run");
        Assert.AreEqual("x", File.ReadAllText(Path.Combine(Target(), "mine.txt")));
    }

    [TestMethod]
    public async Task A_bad_target_or_installer_is_refused_in_words()
    {
        var tool = new FakeTool();
        File.WriteAllText(Target("afile"), "x");
        Assert.AreEqual(GogInstallStatus.InvalidPath, (await Unpacker(tool).UnpackAsync(_installer, Target("afile"))).Status, "a file");
        Assert.AreEqual(GogInstallStatus.InvalidPath, (await Unpacker(tool).UnpackAsync(_installer, Path.Combine(_root, "no", "such", "game"))).Status, "no parent");
        Assert.AreEqual(GogInstallStatus.InvalidPath, (await Unpacker(tool).UnpackAsync(_installer, "")).Status, "empty");
        Assert.AreEqual(GogInstallStatus.InvalidPath, (await Unpacker(tool).UnpackAsync("a\0b", Target())).Status, "control character");
        Assert.AreEqual(GogInstallStatus.InstallerMissing, (await Unpacker(tool).UnpackAsync(Path.Combine(_root, "missing.exe"), Target())).Status);
        Assert.AreEqual(GogInstallStatus.InstallerMissing, (await Unpacker(tool).UnpackAsync(Path.GetDirectoryName(_installer)!, Target())).Status, "a folder is no installer");
        Assert.AreEqual(0, tool.Calls.Count);
        Assert.IsFalse(Directory.Exists(Target()));
    }

    [TestMethod]
    public async Task An_installer_of_another_game_or_a_non_installer_is_refused_and_nothing_is_written()
    {
        var other = new FakeTool { GameIdLines = ["GOG.com game ID is 42"] };
        var result = await Unpacker(other).UnpackAsync(_installer, Target());
        Assert.AreEqual(GogInstallStatus.NotArcanumInstaller, result.Status);
        Assert.AreEqual("GOG game id 42", result.Detail);

        var none = new FakeTool { GameIdLines = ["Inspecting \"X\" - setup data version 5.6.2", "Done."] };
        Assert.AreEqual("no GOG game id", (await Unpacker(none).UnpackAsync(_installer, Target())).Detail);

        var broken = new FakeTool { GameIdExit = 1, GameIdLines = ["Not a supported Inno Setup installer!", "Error: file is not an installer"] };
        var failed = await Unpacker(broken).UnpackAsync(_installer, Target());
        Assert.AreEqual(GogInstallStatus.NotArcanumInstaller, failed.Status);
        Assert.AreEqual("Error: file is not an installer", failed.Detail, "the first error line");
        Assert.IsFalse(Directory.Exists(Target()), "no folder was made");
    }

    [TestMethod]
    public async Task Too_little_free_space_is_refused_with_the_sizes_before_anything_is_written()
    {
        var tool = new FakeTool();
        string? asked = null;
        var result = await Unpacker(tool, Options(path => { asked = path; return 10; })).UnpackAsync(_installer, Target());
        Assert.AreEqual(GogInstallStatus.NotEnoughSpace, result.Status);
        Assert.AreEqual(_root, asked, "the free space of the folder that exists");
        StringAssert.Contains(result.Detail!, "MB needed");
        CollectionAssert.AreEqual(new[] { "--gog-game-id", "--list" }, tool.Actions, "the check came before the verify and the write");
        Assert.IsFalse(Directory.Exists(Target()));

        // An unknown free space does not block the unpack.
        Assert.AreEqual(GogInstallStatus.Unpacked, (await Unpacker(new FakeTool(), Options(_ => null)).UnpackAsync(_installer, Target())).Status);
    }

    [TestMethod]
    public async Task A_failed_verify_writes_nothing()
    {
        var tool = new FakeTool { TestExit = 1, FailureLines = ["Error: Checksum mismatch in \"arcanum1.dat\""] };
        var result = await Unpacker(tool).UnpackAsync(_installer, Target());
        Assert.AreEqual(GogInstallStatus.VerifyFailed, result.Status);
        StringAssert.Contains(result.Detail!, "Checksum mismatch");
        CollectionAssert.AreEqual(new[] { "--gog-game-id", "--list", "--test" }, tool.Actions);
        Assert.IsFalse(Directory.Exists(Target()));
    }

    [TestMethod]
    public async Task Missing_installer_parts_are_named()
    {
        var tool = new FakeTool { TestExit = 1, FailureLines = ["Error: could not open \"setup-1.bin\": No such file"] };
        var result = await Unpacker(tool).UnpackAsync(_installer, Target());
        Assert.AreEqual(GogInstallStatus.MissingParts, result.Status);
        StringAssert.Contains(result.Detail!, "setup-1.bin");
    }

    [TestMethod]
    public async Task A_failed_extraction_gives_the_first_error_line()
    {
        var tool = new FakeTool { ExtractExit = 1, FailureLines = ["Warning: something", "Error: disk full", "Error: later"] };
        var result = await Unpacker(tool).UnpackAsync(_installer, Target());
        Assert.AreEqual(GogInstallStatus.ExtractFailed, result.Status);
        Assert.AreEqual("Error: disk full", result.Detail);
        Assert.IsNull(result.Folder);
    }

    [TestMethod]
    public async Task A_tool_that_cannot_start_is_a_typed_failure()
    {
        var result = await Unpacker(new FakeTool { CannotStart = true }).UnpackAsync(_installer, Target());
        Assert.AreEqual(GogInstallStatus.ToolFailed, result.Status);
        StringAssert.Contains(result.Detail!, "cannot be started");
    }

    [TestMethod]
    public async Task An_executable_that_is_not_the_verified_one_or_not_in_the_root_is_not_accepted()
    {
        var wrong = new FakeTool { Files = [.. new FakeTool().Files.Where(file => file.Name != "Arcanum.exe"), ("Arcanum.exe", [9, 9, 9])] };
        var result = await Unpacker(wrong).UnpackAsync(_installer, Target("a"));
        Assert.AreEqual(GogInstallStatus.FingerprintMismatch, result.Status);
        StringAssert.Contains(result.Detail!, "not the verified");

        // The default expectation is the retail executable's fingerprint.
        var retail = await new GogInstallerUnpacker("/stub/innoextract", new FakeTool().Runner, new GogInstallOptions { FreeSpace = _ => long.MaxValue })
            .UnpackAsync(_installer, Target("b"));
        Assert.AreEqual(GogInstallStatus.FingerprintMismatch, retail.Status, "a synthetic executable is not the retail one");

        var appFolder = new FakeTool { Files = [.. new FakeTool().Files.Where(file => file.Name != "Arcanum.exe"), ("app/Arcanum.exe", ExeBytes)] };
        var inApp = await Unpacker(appFolder).UnpackAsync(_installer, Target("c"));
        Assert.AreEqual(GogInstallStatus.FingerprintMismatch, inApp.Status);
        Assert.AreEqual("no Arcanum.exe in the folder root", inApp.Detail);
    }

    [TestMethod]
    public async Task A_result_that_is_no_game_folder_is_named()
    {
        var tool = new FakeTool { Files = [("Arcanum.exe", ExeBytes)] };
        var result = await Unpacker(tool).UnpackAsync(_installer, Target());
        Assert.AreEqual(GogInstallStatus.NotAGameFolder, result.Status);
        StringAssert.Contains(result.Detail!, "MissingTig");
    }

    [TestMethod]
    public async Task Cancelling_stops_the_unpack_and_says_so()
    {
        using var source = new CancellationTokenSource();
        var tool = new FakeTool { OnExtract = _ => { source.Cancel(); return Task.CompletedTask; } };
        var result = await Unpacker(tool).UnpackAsync(_installer, Target(), null, source.Token);
        Assert.AreEqual(GogInstallStatus.Cancelled, result.Status, "a cancel is a result, not an exception");
        Assert.IsNull(result.Folder);
    }

    [TestMethod]
    public void The_tool_is_found_on_the_path_then_in_the_package_folders()
    {
        var first = Path.Combine(_root, "first"); var second = Path.Combine(_root, "second");
        string Tool(string folder) => Path.Combine(folder, "innoextract");
        Assert.AreEqual(Tool(second), GogInstallerUnpacker.FindTool(string.Join(Path.PathSeparator, first, second), false, path => path == Tool(second)));
        Assert.AreEqual(Tool(first), GogInstallerUnpacker.FindTool(string.Join(Path.PathSeparator, first, second), false, path => path is var p && (p == Tool(first) || p == Tool(second))), "PATH order");
        Assert.AreEqual(Path.Combine("/opt/homebrew/bin", "innoextract"), GogInstallerUnpacker.FindTool("", false, path => path == Path.Combine("/opt/homebrew/bin", "innoextract")),
            "a started application's short PATH still finds Homebrew's");
        Assert.IsNull(GogInstallerUnpacker.FindTool(string.Join(Path.PathSeparator, first), false, _ => false));
        Assert.IsNull(GogInstallerUnpacker.FindTool(string.Join(Path.PathSeparator, "relative/bin", ".", ""), false, path => path.StartsWith("relative", StringComparison.Ordinal) || path.StartsWith(".", StringComparison.Ordinal)),
            "a relative PATH entry is never used");
        Assert.IsNull(GogInstallerUnpacker.FindTool("", true, path => path.StartsWith("/opt", StringComparison.Ordinal)), "Windows has no package folders");
        Assert.AreEqual(Path.Combine(first, "innoextract.exe"), GogInstallerUnpacker.FindTool(first, true, path => path.EndsWith("innoextract.exe", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void The_first_error_line_is_one_short_printable_line()
    {
        string First(int exit, params string[] lines) => GogInstallerUnpacker.FirstError(new(exit, lines));
        Assert.AreEqual("Error: bad", First(1, "ok", "Error: bad\u0007"), "control characters are dropped");
        Assert.AreEqual("what happened", First(1, " - \"a\"", "what happened", "Done."), "no error word: the last line that is not a file line");
        Assert.AreEqual("exit code 7", First(7), "nothing said");
        Assert.AreEqual(200, First(1, "Error: " + new string('x', 400)).Length);
    }

    [TestMethod]
    public async Task The_process_runner_reads_both_streams_and_stops_a_tool_on_cancel()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("the runner is exercised with /bin/sh"); return; }
        var seen = new List<string>();
        var outcome = await GogInstallerUnpacker.RunProcessAsync("/bin/sh", ["-c", "echo out1; echo err1 1>&2; read x; echo \"[$x]\"; exit 3"], seen.Add, CancellationToken.None);
        Assert.IsTrue(outcome.Started);
        Assert.AreEqual(3, outcome.ExitCode);
        CollectionAssert.AreEquivalent(new[] { "out1", "err1", "[]" }, outcome.Lines.ToArray(), "standard input is closed, so a prompt cannot wait");
        CollectionAssert.AreEquivalent(outcome.Lines.ToArray(), seen);

        var missing = await GogInstallerUnpacker.RunProcessAsync(Path.Combine(_root, "no-such-tool"), [], _ => { }, CancellationToken.None);
        Assert.IsFalse(missing.Started);
        Assert.AreEqual(-1, missing.ExitCode);

        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var killed = await GogInstallerUnpacker.RunProcessAsync("/bin/sh", ["-c", "sleep 60"], _ => { }, source.Token);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(20), "the tool was stopped");
        Assert.AreNotEqual(0, killed.ExitCode);
    }

    /// <summary>Opt-in (<c>ARC_GOG_INSTALLER</c> = the GOG offline installer, <c>innoextract</c> installed): the real tool unpacks the
    /// real installer into a temporary folder, which is then the verified 1.0.7.4 folder. Nothing is written inside the installer's folder.</summary>
    [TestMethod, TestCategory("RealData")]
    public async Task The_real_installer_unpacks_into_the_verified_game_folder()
    {
        var installer = Environment.GetEnvironmentVariable("ARC_GOG_INSTALLER");
        if (string.IsNullOrWhiteSpace(installer)) { Assert.Inconclusive("set ARC_GOG_INSTALLER to the GOG offline installer"); return; }
        var tool = GogInstallerUnpacker.FindTool();
        if (tool is null) { Assert.Inconclusive("innoextract is not installed"); return; }
        var target = Path.Combine(_root, "real");
        var reports = new List<GogInstallProgress>();
        var result = await new GogInstallerUnpacker(tool).UnpackAsync(installer, target, reports.Add);
        Assert.AreEqual(GogInstallStatus.Unpacked, result.Status, result.Detail);
        Assert.IsTrue(result.Folder!.Identity!.VerifiedRelease);
        Assert.IsTrue(result.Folder.Identity.GogMarker);
        Assert.IsTrue(File.Exists(Path.Combine(target, "Arcanum.exe")), "the executable is in the root");
        Assert.AreEqual(GogInstallStage.Identifying, reports[^1].Stage);
    }
}
