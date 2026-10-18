using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

/// <summary>DlgReadMode.Original against the original's DLG field and entry parser rules (0x417C20, 0x417860)
/// (ARC-DATA-017). Synthetic records only.</summary>
[TestClass]
public sealed class DlgOriginalModeTests
{
    private static readonly DlgReaderOptions Original = new() { Mode = DlgReadMode.Original };

    [TestMethod]
    public void Bytes_between_fields_are_skipped_and_counted_and_comments_are_not()
    {
        var source = Bytes("// head\r\n{1}{a} \t{b}x{}{}{}{}\r\n\t{2}{c}{}{5, -17}{}{in-3}{}// tail");
        Throws(() => DlgReader.Read(source));
        var document = DlgReader.Read(source, Original);

        Assert.AreEqual(DlgReadMode.Original, document.Mode);
        Assert.AreEqual(DlgStopReason.EndOfInput, document.StopReason);
        Assert.AreEqual(1, document.SkippedBytes);
        Assert.AreEqual(2, document.Records.Count);
        CollectionAssert.AreEqual(Bytes("b"), document.Records[0].AlternateText.Bytes.ToArray());
        Assert.AreEqual(5, document.Records[1].IqValue);
        Assert.AreEqual(DlgRecordRole.Player, document.Records[1].Role);
        Assert.AreEqual(0, document.Records[1].ResponseValue);
        Assert.AreEqual(DlgResponseKind.Exit, document.Records[1].ResponseKind);
    }

    [TestMethod]
    public void Fields_flow_across_lines_and_an_inner_open_brace_is_text()
    {
        var document = DlgReader.Read(Bytes("{7}{x{y}{}\r\n{}{}\r\n{}{}"), Original);
        Assert.AreEqual(1, document.Records.Count);
        CollectionAssert.AreEqual(Bytes("x{y"), document.Records[0].Text.Bytes.ToArray());
        Assert.AreEqual(7, document.Records[0].Id);
    }

    [TestMethod]
    public void Reading_stops_where_the_original_stops_and_keeps_earlier_records()
    {
        var complete = "{1}{a}{}{}{}{}{}";
        AssertStop(complete + "{2}{b}{}{}{}{}", DlgStopReason.IncompleteRecord, 1);
        AssertStop(complete + "{2}{b}{}{0}{}{}{}", DlgStopReason.ZeroIq, 1);
        AssertStop(complete + "{2}{b}{}{x}{}{}{}", DlgStopReason.ZeroIq, 1);
        AssertStop(complete + "{2}{b}{}{3}{}{#1}{}", DlgStopReason.HashResponse, 1);
        AssertStop(complete + "{2}{b", DlgStopReason.IncompleteRecord, 1);
        AssertStop(complete + "  // only a comment", DlgStopReason.EndOfInput, 1);
        AssertStop("", DlgStopReason.EndOfInput, 0);
        AssertStop(complete + "{2}{b}{}{0}", DlgStopReason.ZeroIq, 1);
        AssertStop(complete + "{2}{b}{}{3}{}{#}", DlgStopReason.HashResponse, 1);
        AssertStop(complete + "/\r\n/ {2}{b}{}{}{}{}{}", DlgStopReason.EndOfInput, 1);
        AssertStop(complete + "{2}{b}{}{}{}{}// open comment", DlgStopReason.IncompleteRecord, 1);
    }

    [TestMethod]
    public void A_lone_slash_and_stray_closing_braces_are_skipped_text()
    {
        var document = DlgReader.Read(Bytes("/x}{1}{}{}{}{}{}{}"), Original);
        Assert.AreEqual(3, document.SkippedBytes);
        Assert.AreEqual(1, document.Records.Count);
    }

    [TestMethod]
    public void Duplicate_ids_are_kept_and_lookup_returns_the_first()
    {
        var document = DlgReader.Read(Bytes("{4}{first}{}{}{}{}{}{4}{second}{}{}{}{}{}"), Original);
        Assert.AreEqual(2, document.Records.Count);
        Assert.AreEqual(1, document.DuplicateIds);
        Assert.IsTrue(document.TryGetRecord(4, out var record));
        CollectionAssert.AreEqual(Bytes("first"), record!.Text.Bytes.ToArray());
    }

    [TestMethod]
    public void Limits_and_nul_bytes_still_refuse()
    {
        Throws(() => DlgReader.Read(Bytes("{1}{a\0}{}{}{}{}{}"), Original));
        Throws(() => DlgReader.Read(Bytes("\0{1}"), Original));
        Throws(() => DlgReader.Read(Bytes("{1}{abcdef}{}{}{}{}{}"), new DlgReaderOptions { Mode = DlgReadMode.Original, MaxFieldBytes = 3 }));
        Throws(() => DlgReader.Read(Bytes("{1}{}{}{}{}{}{}{2}{}{}{}{}{}{}"), new DlgReaderOptions { Mode = DlgReadMode.Original, MaxRecordCount = 1 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DlgReader.Read(Bytes("{1}"), new DlgReaderOptions { Mode = (DlgReadMode)9 }));
    }

    [TestMethod]
    public void Atoi_reads_leading_digits_and_saturates()
    {
        Assert.AreEqual(-17, DlgReader.Atoi(Bytes(" \t-17x")));
        Assert.AreEqual(12, DlgReader.Atoi(Bytes("+12")));
        Assert.AreEqual(0, DlgReader.Atoi(Bytes("abc")));
        Assert.AreEqual(int.MaxValue, DlgReader.Atoi(Bytes("99999999999")));
        Assert.AreEqual(int.MinValue, DlgReader.Atoi(Bytes("-99999999999")));
        Assert.AreEqual(int.MinValue, DlgReader.Atoi(Bytes("-2147483648")));
    }

    /// <summary>Opt-in (DAT_ROOT): every installed DLG reads in original mode; the strictly
    /// readable ones read identically, the four single-record files missing a field read empty.</summary>
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_dlg_reads_in_original_mode()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in DLG check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        int files = 0, identical = 0, strictRefused = 0, incomplete = 0, skipping = 0, duplicates = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var bytes = stack.Read(stack.Resolve(key).Winner!).Bytes!;
            files++;
            var document = DlgReader.Read(bytes, Original);
            if (document.StopReason == DlgStopReason.IncompleteRecord) { incomplete++; Assert.AreEqual(0, document.Records.Count, key); }
            else Assert.AreEqual(DlgStopReason.EndOfInput, document.StopReason, key);
            if (document.SkippedBytes > 0) skipping++;
            duplicates += document.DuplicateIds;
            DlgDocument strict;
            try { strict = DlgReader.Read(bytes); } catch (DlgException) { strictRefused++; continue; }
            Assert.AreEqual(strict.Records.Count, document.Records.Count, key);
            for (var i = 0; i < strict.Records.Count; i++)
            {
                Assert.AreEqual(strict.Records[i].Id, document.Records[i].Id, key);
                Assert.AreEqual(strict.Records[i].IqValue, document.Records[i].IqValue, key);
                Assert.AreEqual(strict.Records[i].ResponseValue, document.Records[i].ResponseValue, key);
                for (var f = 0; f < 7; f++) CollectionAssert.AreEqual(strict.Records[i].Fields[f].Bytes.ToArray(), document.Records[i].Fields[f].Bytes.ToArray(), key);
            }
            identical++;
        }
        CollectionAssert.AreEqual(new[] { 709, 671, 38, 4, 1, 0 }, new[] { files, identical, strictRefused, incomplete, skipping, duplicates });
    }

    private static void AssertStop(string text, DlgStopReason reason, int records)
    {
        var document = DlgReader.Read(Bytes(text), Original);
        Assert.AreEqual(reason, document.StopReason, text);
        Assert.AreEqual(records, document.Records.Count, text);
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (DlgException) { return; }
        Assert.Fail("Expected a DLG exception.");
    }

    private static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);
}
