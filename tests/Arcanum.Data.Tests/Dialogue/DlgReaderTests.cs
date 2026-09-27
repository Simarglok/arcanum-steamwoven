using System.Text;
using Arcanum.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class DlgReaderTests
{
    [TestMethod]
    public void Read_preserves_opaque_fields_serialized_order_and_owned_bytes()
    {
        var source = Bytes(" // synthetic\r\n" + Record("20", "a\x80\n", " alt ", "", "LF(unknown)", "", "ACT\x01")
            + "\n// between records\n" + Record("2", "player", "", "-5", "QU(opaque)", "-9", "SC\x02"));
        var document = DlgReader.Read(source);

        Assert.AreEqual(source.Length, document.PayloadLength);
        Assert.AreEqual(2, document.Records.Count);
        Assert.AreEqual(20, document.Records[0].Id);
        Assert.AreEqual(DlgRecordRole.Npc, document.Records[0].Role);
        Assert.IsNull(document.Records[0].IqValue);
        Assert.AreEqual(-9, document.Records[1].ResponseValue);
        Assert.AreEqual(DlgResponseKind.Special, document.Records[1].ResponseKind);
        Assert.AreEqual(7, document.Records[0].Fields.Count);
        CollectionAssert.AreEqual(Bytes("a\x80\n"), document.Records[0].Text.Bytes.ToArray());
        CollectionAssert.AreEqual(Bytes("LF(unknown)"), document.Records[0].Condition.Bytes.ToArray());
        CollectionAssert.AreEqual(Bytes("SC\x02"), document.Records[1].Action.Bytes.ToArray());
        Assert.IsFalse(document.Records is DlgRecord[]);
        Assert.IsFalse(document.Records[0].Text.Bytes is byte[]);

        var sourceMutationOffset = Array.IndexOf(source, (byte)0x80);
        Assert.IsTrue(sourceMutationOffset >= 0);
        source[sourceMutationOffset] = 0;
        Assert.AreEqual((byte)0x80, document.Records[0].Text.Bytes[1]);

        Assert.IsTrue(document.TryGetRecord(2, out var second));
        Assert.AreSame(document.Records[1], second);
        Assert.IsTrue(document.TryGetRecord(20, out var first));
        Assert.AreSame(document.Records[0], first);
        Assert.IsFalse(document.TryGetRecord(999, out _));
    }

    [TestMethod]
    public void Read_parses_the_documented_synthetic_example()
    {
        var document = DlgReader.Read(Bytes(
            "{100}{synthetic-npc}{}{}{}{200}{}\n{200}{synthetic-player}{}{5}{}{0}{}"));

        Assert.AreEqual(2, document.Records.Count);
        Assert.AreEqual(DlgRecordRole.Npc, document.Records[0].Role);
        Assert.AreEqual(200, document.Records[0].ResponseValue);
        Assert.AreEqual(DlgRecordRole.Player, document.Records[1].Role);
        Assert.AreEqual(DlgResponseKind.Exit, document.Records[1].ResponseKind);
        Assert.AreEqual(0, document.Records[1].ResponseValue);
    }

    [TestMethod]
    public void Read_classifies_iq_response_and_int32_boundaries()
    {
        var document = DlgReader.Read(Bytes(
            Record("1", iq: "1", response: "") + Record("2", iq: "-1", response: "0")
            + Record("3", iq: "2", response: "4") + Record("4", iq: "3", response: "-2")
            + Record("0005", iq: "-2147483648", response: "2147483647")
            + Record("-2147483648", iq: "2147483647", response: "-2147483648")));

        Assert.AreEqual(DlgResponseKind.Exit, document.Records[0].ResponseKind);
        Assert.IsNull(document.Records[0].ResponseValue);
        Assert.AreEqual(DlgResponseKind.Exit, document.Records[1].ResponseKind);
        Assert.AreEqual(0, document.Records[1].ResponseValue);
        Assert.AreEqual(DlgResponseKind.Direct, document.Records[2].ResponseKind);
        Assert.AreEqual(4, document.Records[2].ResponseValue);
        Assert.AreEqual(DlgResponseKind.Special, document.Records[3].ResponseKind);
        Assert.AreEqual(-2, document.Records[3].ResponseValue);
        Assert.AreEqual(5, document.Records[4].Id);
        Assert.AreEqual(int.MinValue, document.Records[4].IqValue);
        Assert.AreEqual(int.MaxValue, document.Records[4].ResponseValue);
        Assert.AreEqual(int.MinValue, document.Records[5].Id);
        Assert.AreEqual(int.MaxValue, document.Records[5].IqValue);
        Assert.AreEqual(int.MinValue, document.Records[5].ResponseValue);

        foreach (var value in new[] { "+1", " 1", "-", "2147483648", "-2147483649" })
        {
            var idInput = Bytes(Record(value));
            AssertDiagnostic(Throws<DlgFormatException>(() => DlgReader.Read(idInput)), Array.IndexOf(idInput, (byte)value[0]), 0, DlgFieldKind.Id);
            var iqInput = Bytes(Record("1", iq: value));
            AssertDiagnostic(Throws<DlgFormatException>(() => DlgReader.Read(iqInput)), Array.IndexOf(iqInput, (byte)value[0]), 0, DlgFieldKind.Iq);
            var responseInput = Bytes(Record("1", response: value));
            AssertDiagnostic(Throws<DlgFormatException>(() => DlgReader.Read(responseInput)), Array.IndexOf(responseInput, (byte)value[0]), 0, DlgFieldKind.Response);
        }
        Throws<DlgFormatException>(() => DlgReader.Read(Bytes(Record("1", iq: "0"))));
        Throws<DlgFormatException>(() => DlgReader.Read(Bytes(Record("1", iq: "-0"))));
        Throws<DlgFormatException>(() => DlgReader.Read(Bytes(Record("1") + Record("01"))));
    }

    [TestMethod]
    public void Read_rejects_malformed_structure_with_useful_non_payload_diagnostics()
    {
        var missingField = Bytes("{1}{}{}{}{}{}");
        AssertDiagnostic(Throws<DlgFormatException>(() => DlgReader.Read(missingField)), missingField.Length, 0, DlgFieldKind.Action);

        var extraField = Bytes(Record("1") + "{}");
        AssertDiagnostic(Throws<DlgFormatException>(() => DlgReader.Read(extraField)), extraField.Length, 1, DlgFieldKind.Text);

        var unmatched = Throws<DlgFormatException>(() => DlgReader.Read(Bytes("}")));
        AssertDiagnostic(unmatched, 0, 0, null);

        var unterminated = Bytes("{1}{unterminated");
        AssertDiagnostic(Throws<DlgFormatException>(() => DlgReader.Read(unterminated)), 3, 0, DlgFieldKind.Text);

        var nested = Bytes("{1}{nested{}}{}{}{}{}{}");
        AssertDiagnostic(Throws<DlgUnsupportedFormatException>(() => DlgReader.Read(nested)), Array.IndexOf(nested, (byte)'{', 4), 0, DlgFieldKind.Text);

        var whitespaceBetweenFields = Bytes("{1} {}{}{}{}{}{}");
        AssertDiagnostic(Throws<DlgUnsupportedFormatException>(() => DlgReader.Read(whitespaceBetweenFields)), 3, 0, DlgFieldKind.Text);

        var commentBetweenFields = Bytes("{1}// comment\n{}{}{}{}{}{}");
        AssertDiagnostic(Throws<DlgUnsupportedFormatException>(() => DlgReader.Read(commentBetweenFields)), 3, 0, DlgFieldKind.Text);

        var structuralNul = Bytes("\0" + Record("1"));
        AssertDiagnostic(Throws<DlgFormatException>(() => DlgReader.Read(structuralNul)), 0, 0, null);
        var commentNul = Bytes("//\0\n" + Record("1"));
        AssertDiagnostic(Throws<DlgFormatException>(() => DlgReader.Read(commentNul)), 2, null, null);
        var fieldNul = Bytes(Record("1", text: "a\0b"));
        AssertDiagnostic(Throws<DlgFormatException>(() => DlgReader.Read(fieldNul)), Array.IndexOf(fieldNul, (byte)0), 0, DlgFieldKind.Text);

        var tab = Throws<DlgUnsupportedFormatException>(() => DlgReader.Read(Bytes(Record("1") + "\t")));
        AssertDiagnostic(tab, Record("1").Length, 1, null);
        Assert.IsFalse(tab.Message.Contains("secret-payload", StringComparison.Ordinal));
        Throws<DlgUnsupportedFormatException>(() => DlgReader.Read(Bytes(Record("1") + "\r")));
        Throws<DlgFormatException>(() => DlgReader.Read(Array.Empty<byte>()));
    }

    [TestMethod]
    public void Read_enforces_exact_input_field_record_and_option_boundaries()
    {
        var oneRecord = Bytes(Record("1", text: "abc"));
        Assert.AreEqual(oneRecord.Length, DlgReader.Read(oneRecord, new DlgReaderOptions { MaxInputBytes = oneRecord.Length }).PayloadLength);
        Throws<DlgLimitExceededException>(() => DlgReader.Read(oneRecord, new DlgReaderOptions { MaxInputBytes = oneRecord.Length - 1 }));
        Assert.AreEqual(3, DlgReader.Read(oneRecord, new DlgReaderOptions { MaxFieldBytes = 3 }).Records[0].Text.Length);
        Throws<DlgLimitExceededException>(() => DlgReader.Read(oneRecord, new DlgReaderOptions { MaxFieldBytes = 2 }));

        var twoRecords = Bytes(Record("1") + Record("2"));
        Assert.AreEqual(1, DlgReader.Read(Bytes(Record("1")), new DlgReaderOptions { MaxRecordCount = 1 }).Records.Count);
        Throws<DlgLimitExceededException>(() => DlgReader.Read(twoRecords, new DlgReaderOptions { MaxRecordCount = 1 }));
        foreach (var options in new[]
        {
            new DlgReaderOptions { MaxInputBytes = 0 }, new DlgReaderOptions { MaxRecordCount = 0 }, new DlgReaderOptions { MaxFieldBytes = 0 },
        })
        {
            Throws<ArgumentOutOfRangeException>(() => DlgReader.Read(oneRecord, options));
        }
    }

    [TestMethod]
    public void Read_stream_honors_current_position_short_reads_and_seekable_and_nonseekable_caps()
    {
        using var retained = new TrackingStream(Bytes("prefix" + Record("1")), seekable: true);
        retained.Position = 6;
        Assert.AreEqual(1, DlgReader.Read(retained, leaveOpen: true).Records.Count);
        Assert.IsTrue(retained.CanRead);

        var shortRead = new TrackingStream(Bytes(Record("1")), seekable: false, maximumReadSize: 2);
        Assert.AreEqual(1, DlgReader.Read(shortRead).Records.Count);
        Assert.IsTrue(shortRead.Disposed);

        var data = Bytes(Record("1"));
        var seekableCap = new TrackingStream(data, seekable: true);
        Throws<DlgLimitExceededException>(() => DlgReader.Read(seekableCap, new DlgReaderOptions { MaxInputBytes = data.Length - 1 }));
        Assert.IsTrue(seekableCap.Disposed);

        var nonseekableCap = new TrackingStream(data, seekable: false);
        Throws<DlgLimitExceededException>(() => DlgReader.Read(nonseekableCap, new DlgReaderOptions { MaxInputBytes = data.Length - 1 }));
        Assert.IsTrue(nonseekableCap.Disposed);
    }

    [TestMethod]
    public void Read_stream_disposes_or_retains_on_success_failures_invalid_options_and_io()
    {
        var malformedOwned = new TrackingStream(Bytes("{1}"));
        Throws<DlgFormatException>(() => DlgReader.Read(malformedOwned));
        Assert.IsTrue(malformedOwned.Disposed);
        var malformedRetained = new TrackingStream(Bytes("{1}"));
        Throws<DlgFormatException>(() => DlgReader.Read(malformedRetained, leaveOpen: true));
        Assert.IsFalse(malformedRetained.Disposed);

        var invalidOptionsOwned = new TrackingStream(Bytes(Record("1")));
        Throws<ArgumentOutOfRangeException>(() => DlgReader.Read(invalidOptionsOwned, new DlgReaderOptions { MaxInputBytes = 0 }));
        Assert.IsTrue(invalidOptionsOwned.Disposed);
        var invalidOptionsRetained = new TrackingStream(Bytes(Record("1")));
        Throws<ArgumentOutOfRangeException>(() => DlgReader.Read(invalidOptionsRetained, new DlgReaderOptions { MaxInputBytes = 0 }, leaveOpen: true));
        Assert.IsFalse(invalidOptionsRetained.Disposed);

        var ioOwned = new TrackingStream(Bytes(Record("1")), throwOnRead: true);
        Throws<IOException>(() => DlgReader.Read(ioOwned));
        Assert.IsTrue(ioOwned.Disposed);
        var ioRetained = new TrackingStream(Bytes(Record("1")), throwOnRead: true);
        Throws<IOException>(() => DlgReader.Read(ioRetained, leaveOpen: true));
        Assert.IsFalse(ioRetained.Disposed);

        var unreadableOwned = new UnreadableStream();
        Throws<ArgumentException>(() => DlgReader.Read(unreadableOwned));
        Assert.IsTrue(unreadableOwned.Disposed);
    }

    private static string Record(string id, string text = "", string alternateText = "", string iq = "", string condition = "", string response = "", string action = "") =>
        $"{{{id}}}{{{text}}}{{{alternateText}}}{{{iq}}}{{{condition}}}{{{response}}}{{{action}}}";

    private static byte[] Bytes(string value) => Encoding.Latin1.GetBytes(value);

    private static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new AssertFailedException($"Expected {typeof(TException).Name}.");
    }

    private static void AssertDiagnostic(DlgException exception, int offset, int? recordIndex, DlgFieldKind? fieldKind)
    {
        Assert.AreEqual(offset, exception.Offset);
        Assert.AreEqual(recordIndex, exception.RecordIndex);
        Assert.AreEqual(fieldKind, exception.FieldKind);
    }

    private sealed class TrackingStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly bool _seekable;
        private readonly int _maximumReadSize;
        private readonly bool _throwOnRead;

        public TrackingStream(byte[] bytes, bool seekable = true, int maximumReadSize = int.MaxValue, bool throwOnRead = false)
        {
            _inner = new MemoryStream(bytes, writable: false);
            _seekable = seekable;
            _maximumReadSize = maximumReadSize;
            _throwOnRead = throwOnRead;
        }

        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => _seekable;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_throwOnRead) throw new IOException("Injected stream failure.");
            return _inner.Read(buffer, offset, Math.Min(count, _maximumReadSize));
        }

        public override int ReadByte()
        {
            if (_throwOnRead) throw new IOException("Injected stream failure.");
            return _inner.ReadByte();
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class UnreadableStream : Stream
    {
        public bool Disposed { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}