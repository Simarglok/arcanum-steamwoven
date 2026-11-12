using System.Text;
using Arcanum.Session.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests;

/// <summary>ARC-UI-1130: the one settings file (<c>arcanum.cfg</c>) in the original's <c>name=value</c> shape (OP8, load 0x438B50,
/// set 0x438DF0, save 0x438C20), on synthetic text.</summary>
[TestClass]
public sealed class ArcanumConfigTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-cfg-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private static string[] Names(ArcanumConfigRead read) => read.Entries.Select(entry => entry.Name).ToArray();

    [TestMethod]
    public void A_line_is_cut_at_its_first_equals_sign_and_both_parts_are_trimmed()
    {
        var read = ArcanumConfig.Parse("  difficulty = 2 \r\nhd pack path=/a=b/c\n\n\tauto attack\t=\t0\t\n");
        CollectionAssert.AreEqual(new[] { "difficulty", "hd pack path", "auto attack" }, Names(read));
        Assert.AreEqual("2", read.Get("difficulty"));
        Assert.AreEqual("/a=b/c", read.Get("hd pack path"), "only the first = splits");
        Assert.AreEqual("0", read.Get("auto attack"));
        Assert.AreEqual(0, read.Dropped);
    }

    [TestMethod]
    public void A_line_without_a_pair_or_with_an_empty_name_or_value_is_ignored()
    {
        var read = ArcanumConfig.Parse("no pair\n=5\nname=\n   =  \nok=1\nbad\u0001name=2\nname with é=3\ntoo=" + new string('x', ArcanumConfig.MaxValueChars + 1) + "\n");
        CollectionAssert.AreEqual(new[] { "ok" }, Names(read));
        Assert.AreEqual(7, read.Dropped);
    }

    [TestMethod]
    public void Names_match_ignoring_case_and_a_repeated_name_keeps_its_place_and_takes_the_last_value()
    {
        var read = ArcanumConfig.Parse("Music Volume=1\nvoice volume=2\nMUSIC VOLUME=9\n");
        CollectionAssert.AreEqual(new[] { "Music Volume", "voice volume" }, Names(read));
        Assert.AreEqual("9", read.Get("music volume"));
        Assert.IsTrue(read.Has("VOICE VOLUME"));
        Assert.IsNull(read.Get("effects volume"));
    }

    [TestMethod]
    public void Text_round_trips_through_format_and_parse()
    {
        var entries = new ArcanumConfigEntry[] { new("difficulty", "1"), new("hd pack path", "/x y/z"), new("launcher", "{\"schema\":1}") };
        var text = ArcanumConfig.Format(entries);
        Assert.AreEqual("difficulty=1\nhd pack path=/x y/z\nlauncher={\"schema\":1}\n", text);
        CollectionAssert.AreEqual(entries, ArcanumConfig.Parse(text).Entries.ToArray());
    }

    [TestMethod]
    public void Names_and_values_the_file_can_hold_are_checked()
    {
        foreach (var good in new[] { "a", "text floaters", "turn-based", new string('n', ArcanumConfig.MaxNameChars) }) Assert.IsTrue(ArcanumConfig.ValidName(good), good);
        foreach (var bad in new[] { "", " x", "x ", "a=b", "tab\tname", "é", new string('n', ArcanumConfig.MaxNameChars + 1) }) Assert.IsFalse(ArcanumConfig.ValidName(bad), bad);
        Assert.IsFalse(ArcanumConfig.ValidName(null));
        foreach (var good in new[] { "1", "a b", "{\"k\":\"v=1\"}", "é" }) Assert.IsTrue(ArcanumConfig.ValidValue(good), good);
        foreach (var bad in new[] { "", " 1", "1 ", "a\nb", "a\u0000b", new string('v', ArcanumConfig.MaxValueChars + 1) }) Assert.IsFalse(ArcanumConfig.ValidValue(bad), bad);
        Assert.IsFalse(ArcanumConfig.ValidValue(null));
    }

    [TestMethod]
    public void A_save_changes_the_named_lines_only_and_keeps_every_other_line_in_place()
    {
        var path = Path.Combine(_root, "deep", "folder", ArcanumConfig.FileName);
        Assert.AreEqual(ArcanumConfigReadStatus.Missing, ArcanumConfig.Read(path).Status);
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, [new("difficulty", "2"), new("Future Setting", "kept")]));
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, [new("music volume", "7")]));
        Assert.AreEqual("difficulty=2\nFuture Setting=kept\nmusic volume=7\n", File.ReadAllText(path), "a new line goes to the end");
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, [new("DIFFICULTY", "0"), new("music volume", null), new("nothing here", null)]));
        Assert.AreEqual("difficulty=0\nFuture Setting=kept\n", File.ReadAllText(path), "a name is matched ignoring case, a null value removes the line");
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, [new("difficulty", "1"), new("difficulty", "2")]));
        Assert.AreEqual("2", ArcanumConfig.Read(path).Get("difficulty"), "a repeated name takes its last value");
        CollectionAssert.AreEqual(new[] { ArcanumConfig.FileName }, Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName).ToArray(), "no temporary file is left");
    }

    [TestMethod]
    public void A_save_drops_the_lines_that_are_not_pairs_as_the_originals_rewrite_does()
    {
        var path = Path.Combine(_root, ArcanumConfig.FileName);
        File.WriteAllText(path, "; a note\r\nalways run=1\r\ngarbage\r\n");
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, [new("always run", "0")]));
        Assert.AreEqual("always run=0\n", File.ReadAllText(path));
    }

    [TestMethod]
    public void A_name_or_value_the_file_cannot_hold_is_a_caller_error()
    {
        var path = Path.Combine(_root, ArcanumConfig.FileName);
        Assert.ThrowsExactly<ArgumentException>(() => ArcanumConfig.Save(path, [new("a=b", "1")]));
        Assert.ThrowsExactly<ArgumentException>(() => ArcanumConfig.Save(path, [new("a", "")]));
        Assert.ThrowsExactly<ArgumentException>(() => ArcanumConfig.Save(path, [new("a", "line\nbreak")]));
        Assert.IsFalse(File.Exists(path), "nothing is written for a caller error");
    }

    [TestMethod]
    public void A_file_that_is_not_a_settings_text_is_replaced_and_an_unreadable_one_is_not()
    {
        var path = Path.Combine(_root, ArcanumConfig.FileName);
        File.WriteAllBytes(path, [(byte)'a', (byte)'=', 0xFF]);
        Assert.AreEqual(ArcanumConfigReadStatus.Invalid, ArcanumConfig.Read(path).Status, "not UTF-8");
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, [new("a", "1")]), "an invalid file is replaced");
        Assert.AreEqual("a=1\n", File.ReadAllText(path));
        File.WriteAllText(path, new string(' ', ArcanumConfig.MaxFileBytes + 1), Encoding.ASCII);
        Assert.AreEqual(ArcanumConfigReadStatus.Invalid, ArcanumConfig.Read(path).Status, "too large");

        Directory.CreateDirectory(Path.Combine(_root, "dir.cfg"));
        Assert.AreEqual(ArcanumConfigSaveStatus.Failed, ArcanumConfig.Save(Path.Combine(_root, "dir.cfg"), [new("a", "1")]), "a folder is not replaced");
        if (OperatingSystem.IsWindows()) return;
        File.WriteAllText(path, "a=1\n");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            if (ArcanumConfig.Read(path).Status != ArcanumConfigReadStatus.Unreadable) return; // not when the tests run as root
            Assert.AreEqual(ArcanumConfigSaveStatus.RefusedUnreadable, ArcanumConfig.Save(path, [new("a", "2")]));
            Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, [new("a", "2")], replaceUnreadable: true), "only a reset replaces it");
        }
        finally { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        Assert.AreEqual("a=2\n", File.ReadAllText(path));
    }

    [TestMethod]
    public void The_file_holds_at_most_the_entry_limit()
    {
        var path = Path.Combine(_root, ArcanumConfig.FileName);
        var many = Enumerable.Range(0, ArcanumConfig.MaxEntries).Select(index => new ArcanumConfigChange("name" + index, "1")).ToArray();
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, many));
        Assert.AreEqual(ArcanumConfigSaveStatus.Failed, ArcanumConfig.Save(path, [new("one more", "1")]));
        Assert.AreEqual(ArcanumConfig.MaxEntries, ArcanumConfig.Read(path).Entries.Count, "a refused write changes nothing");
        Assert.AreEqual(ArcanumConfigSaveStatus.Saved, ArcanumConfig.Save(path, [new("name0", null), new("one more", "1")]));
    }
}
