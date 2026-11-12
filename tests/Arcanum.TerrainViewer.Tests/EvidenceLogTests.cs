using System.Text.RegularExpressions;
using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-GPAD-1376: the evidence log's format, recorder and file; AC1 (off, local), AC2 (closed tokens only), AC3 (the summariser reads
/// the same lines, checked against tools/input/evidence_summary.py's synthetic session).</summary>
[TestClass]
public sealed class EvidenceLogTests
{
    private sealed class MemorySink : IEvidenceSink
    {
        public List<string> Lines { get; } = [];
        public bool Closed { get; private set; }
        public bool Fail { get; set; }
        public int FailAfter { get; set; } = int.MaxValue;
        public bool Append(string text)
        {
            if (Fail || Lines.Count >= FailAfter) return false;
            Lines.AddRange(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            return true;
        }
        public void Close() => Closed = true;
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json"))) dir = dir.Parent;
        Assert.IsNotNull(dir, "repository root not found");
        return Path.Combine(dir.FullName, relative);
    }

    private static string[] PythonList(string name)
    {
        var text = File.ReadAllText(RepoFile("tools/input/evidence_summary.py"));
        var match = Regex.Match(text, "^" + name + @" = \[(.*?)\]$", RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.IsTrue(match.Success, name);
        return Regex.Matches(match.Groups[1].Value, "\"([a-z0-9-]+)\"").Select(m => m.Groups[1].Value).ToArray();
    }

    [TestMethod]
    public void The_python_summariser_knows_exactly_the_tokens_of_the_enums()
    {
        CollectionAssert.AreEqual(Enum.GetValues<EvidenceKind>().Select(EvidenceLogCodec.Token).ToArray(), PythonList("KINDS"));
        CollectionAssert.AreEqual(Enum.GetValues<EvidenceCommand>().Select(EvidenceLogCodec.Token).ToArray(), PythonList("COMMANDS"));
        CollectionAssert.AreEqual(Enum.GetValues<EvidenceScheme>().Select(EvidenceLogCodec.Token).ToArray(), PythonList("SCHEMES"));
        CollectionAssert.AreEqual(Enum.GetValues<ActiveDevice>().Select(EvidenceLogCodec.Token).ToArray(), PythonList("DEVICES"));
        CollectionAssert.AreEqual(Enum.GetValues<EvidenceOutcome>().Select(EvidenceLogCodec.Token).ToArray(), PythonList("OUTCOMES"));
    }

    [TestMethod]
    public void The_recorder_writes_the_synthetic_session_the_summariser_reads()
    {
        var sink = new MemorySink();
        var log = new EvidenceRecorder(() => sink);
        const EvidenceScheme s = EvidenceScheme.Direct; const ActiveDevice d = ActiveDevice.Pad;
        Assert.IsTrue(log.SetEnabled(true, 100.0, s, d));
        void Task(double at, EvidenceKind kind, int n) => log.Record(100 + at, kind, EvidenceCommand.None, s, d, EvidenceOutcome.Ok, n);
        void Cmd(double at, EvidenceCommand c, EvidenceOutcome o, int n) => log.Command(100 + at, c, s, d, o, n);
        Task(1, EvidenceKind.TaskStart, 1);
        Cmd(1.5, EvidenceCommand.Walk, EvidenceOutcome.Ok, 1);
        Cmd(2.5, EvidenceCommand.Examine, EvidenceOutcome.WrongTarget, 1);
        Cmd(3, EvidenceCommand.Examine, EvidenceOutcome.Ok, 1);
        Task(3.5, EvidenceKind.PauseOn, 1); Task(4, EvidenceKind.PauseOff, 1); Task(5, EvidenceKind.TaskEnd, 1);
        Task(6, EvidenceKind.TaskStart, 2);
        Cmd(6.5, EvidenceCommand.OpenWindow, EvidenceOutcome.Refused, 2);
        Cmd(7, EvidenceCommand.Target, EvidenceOutcome.Cancelled, 2);
        Task(9.5, EvidenceKind.TaskEnd, 2); Task(10, EvidenceKind.TaskStart, 3);
        Cmd(10.5, EvidenceCommand.QuickSave, EvidenceOutcome.Ok, 3);
        Assert.IsTrue(log.SetEnabled(false, 112.0, s, d));
        Assert.IsTrue(sink.Closed);

        var text = File.ReadAllText(RepoFile("tools/input/evidence_summary.py"));
        var sample = Regex.Match(text, "SAMPLE = \"\"\"(.*?)\"\"\"", RegexOptions.Singleline).Groups[1].Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(sink.Lines.Count).ToArray();
        var written = sink.Lines.ToArray();
        Assert.AreEqual(string.Join("\n", sample), string.Join("\n", written));
    }

    [TestMethod]
    public void Off_by_default_nothing_is_opened_or_written()
    {
        var opened = 0;
        var log = new EvidenceRecorder(() => { opened++; return new MemorySink(); });
        Assert.IsFalse(log.Recording);
        log.Command(1, EvidenceCommand.Walk, EvidenceScheme.Original, ActiveDevice.KeyboardMouse);
        log.Record(2, EvidenceKind.TaskStart, EvidenceCommand.None, EvidenceScheme.Original, ActiveDevice.KeyboardMouse, task: 1);
        Assert.IsTrue(log.SetEnabled(false, 3, EvidenceScheme.Original, ActiveDevice.KeyboardMouse), "off while off is a no-op");
        Assert.AreEqual(0, opened);
        Assert.AreEqual(0, log.Lines);
    }

    [TestMethod]
    public void A_device_with_no_place_to_write_stays_off_and_a_failing_sink_stops_the_session()
    {
        var none = new EvidenceRecorder(() => null);
        Assert.IsFalse(none.SetEnabled(true, 0, EvidenceScheme.Original, ActiveDevice.KeyboardMouse));
        Assert.IsFalse(none.Recording);
        var thrown = new EvidenceRecorder(() => throw new IOException("disk"));
        Assert.IsFalse(thrown.SetEnabled(true, 0, EvidenceScheme.Original, ActiveDevice.KeyboardMouse));

        var sink = new MemorySink { FailAfter = 3 }; // header, session start, one command
        var log = new EvidenceRecorder(() => sink);
        Assert.IsTrue(log.SetEnabled(true, 0, EvidenceScheme.Original, ActiveDevice.KeyboardMouse));
        log.Command(1, EvidenceCommand.Walk, EvidenceScheme.Original, ActiveDevice.KeyboardMouse);
        log.Command(2, EvidenceCommand.Walk, EvidenceScheme.Original, ActiveDevice.KeyboardMouse);
        Assert.IsFalse(log.Recording);
        Assert.IsTrue(log.Stopped);
        Assert.IsTrue(sink.Closed);
    }

    [TestMethod]
    public void A_session_is_bounded()
    {
        var sink = new MemorySink();
        var log = new EvidenceRecorder(() => sink);
        log.SetEnabled(true, 0, EvidenceScheme.Original, ActiveDevice.KeyboardMouse);
        for (var i = 0; i < EvidenceRecorder.MaximumLines + 10; i++) log.Command(i / 1000.0, EvidenceCommand.Walk, EvidenceScheme.Original, ActiveDevice.KeyboardMouse);
        Assert.IsFalse(log.Recording);
        Assert.IsTrue(log.Stopped);
        Assert.AreEqual(EvidenceRecorder.MaximumLines + 1, sink.Lines.Count, "header plus the cap");
        Assert.AreEqual(EvidenceLogCodec.Header, sink.Lines[0]);
    }

    [TestMethod]
    public void A_line_holds_only_closed_tokens_and_numbers_never_a_path_or_a_name()
    {
        var allowed = new Regex("^\\{\"t\":\\d+,\"k\":\"[a-z-]+\",\"c\":\"[a-z-]+\",\"s\":\"[a-z-]+\",\"d\":\"[a-z-]+\",\"o\":\"[a-z-]+\",\"n\":\\d+\\}$");
        var seen = 0;
        foreach (var kind in Enum.GetValues<EvidenceKind>()) foreach (var command in Enum.GetValues<EvidenceCommand>())
        foreach (var scheme in Enum.GetValues<EvidenceScheme>()) foreach (var device in Enum.GetValues<ActiveDevice>()) foreach (var outcome in Enum.GetValues<EvidenceOutcome>())
        {
            var line = EvidenceLogCodec.Format(new EvidenceEvent(123456, kind, command, scheme, device, outcome, 8));
            Assert.IsNotNull(line);
            Assert.IsTrue(allowed.IsMatch(line), line);
            Assert.IsTrue(line.Length <= EvidenceLogCodec.MaxLineBytes);
            Assert.AreEqual(new EvidenceEvent(123456, kind, command, scheme, device, outcome, 8), EvidenceLogCodec.Parse(line));
            seen++;
        }
        Assert.AreEqual(7 * Enum.GetValues<EvidenceCommand>().Length * 4 * 3 * 4, seen);
        Assert.IsNull(EvidenceLogCodec.Format(new EvidenceEvent(0, (EvidenceKind)99, EvidenceCommand.None, EvidenceScheme.Original, ActiveDevice.Pad, EvidenceOutcome.Ok)));
        Assert.IsNull(EvidenceLogCodec.Format(new EvidenceEvent(-1, EvidenceKind.Command, EvidenceCommand.None, EvidenceScheme.Original, ActiveDevice.Pad, EvidenceOutcome.Ok)));
        Assert.IsNull(EvidenceLogCodec.Format(new EvidenceEvent(0, EvidenceKind.Command, EvidenceCommand.None, EvidenceScheme.Original, ActiveDevice.Pad, EvidenceOutcome.Ok, 100)));
    }

    [TestMethod]
    public void The_reader_refuses_anything_outside_the_format()
    {
        const string good = "{\"t\":1,\"k\":\"command\",\"c\":\"walk\",\"s\":\"direct\",\"d\":\"pad\",\"o\":\"ok\",\"n\":0}";
        Assert.IsNotNull(EvidenceLogCodec.Parse(good));
        foreach (var bad in new[]
        {
            null, "", "not json", "[]", EvidenceLogCodec.Header,
            good.Replace("\"n\":0}", "\"n\":0,\"p\":\"/Users/x\"}"), good.Replace("\"walk\"", "\"/Users/x/save\""), good.Replace("\"direct\"", "\"Direct\""),
            good.Replace("\"t\":1", "\"t\":\"1\""), good.Replace("\"t\":1", "\"t\":-1"), good.Replace("\"n\":0", "\"n\":100"), good.Replace("\"t\":1,", ""),
            good.Replace("\"pad\"", "\"gamepad\""), good + new string(' ', 300),
        }) Assert.IsNull(EvidenceLogCodec.Parse(bad), bad);
    }

    [TestMethod]
    public void The_file_sink_writes_lines_without_clobbering_a_file_and_fails_to_null_on_a_bad_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "arcanum-evidence-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var at = new DateTime(2026, 11, 12, 18, 30, 5, DateTimeKind.Utc);
            var first = EvidenceFileSink.Open(folder, at);
            var second = EvidenceFileSink.Open(folder, at);
            Assert.IsNotNull(first); Assert.IsNotNull(second);
            Assert.AreEqual("evidence-20261112-183005.jsonl", first.FileName);
            Assert.AreEqual("evidence-20261112-183005-1.jsonl", second.FileName);
            Assert.IsTrue(first.Append(EvidenceLogCodec.Header + "\n"));
            Assert.IsTrue(first.Append("{\"t\":0}\n"));
            first.Close(); second.Close();
            CollectionAssert.AreEqual(new[] { EvidenceLogCodec.Header, "{\"t\":0}" }, File.ReadAllLines(Path.Combine(folder, first.FileName)));
            Assert.IsFalse(first.Append("x"), "a closed sink reports the failure");
            var occupied = Path.Combine(folder, "file");
            File.WriteAllText(occupied, "x");
            Assert.IsNull(EvidenceFileSink.Open(Path.Combine(occupied, "sub"), at), "a folder that can not be made gives null");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
