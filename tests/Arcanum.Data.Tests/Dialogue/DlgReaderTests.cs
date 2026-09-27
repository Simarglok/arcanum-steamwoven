using System.Text;
using Arcanum.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests;

[TestClass]
public sealed class DlgReaderTests
{
    [TestMethod]
    public void Read_preserves_all_fields_roles_order_and_owned_bytes()
    {
        var source = Bytes("// synthetic\r\n{001}{a\x80\n}{ alt }{}{LF(unknown)}{}{ACT\x01}\n{2}{player}{}{-5}{??}{-9}{}");
        var original = source.ToArray();
        var document = DlgReader.Read(source);

        Assert.AreEqual(source.Length, document.PayloadLength);
        Assert.AreEqual(2, document.Records.Count);
        Assert.AreEqual(1, document.Records[0].Id);
        Assert.AreEqual(DlgRecordRole.Npc, document.Records[0].Role);
        Assert.IsNull(document.Records[0].IqValue);
        Assert.IsNull(document.Records[0].ResponseKind);
        Assert.AreEqual(2, document.Records[1].Id);
        Assert.AreEqual(DlgRecordRole.Player, document.Records[1].Role);
        Assert.AreEqual(-5, document.Records[1].IqValue);
        Assert.AreEqual(-9, document.Records[1].ResponseValue);
        Assert.AreEqual(DlgResponseKind.Special, document.Records[1].ResponseKind);
        Assert.AreEqual(7, document.Records[0].Fields.Count);
        CollectionAssert.AreEqual(new byte[] { (byte)'0', (byte)'0', (byte)'1' }, document.Records[0].IdField.Bytes.ToArray());
        CollectionAssert.AreEqual(new byte[] { (byte)'a', 0x80, (byte)'\n' }, document.Records[0].Text.Bytes.ToArray());
        Assert.IsFalse(document.Records is DlgRecord[]);
        Assert.IsFalse(document.Records[0].Text.Bytes is byte[]);
        source[21] = 0;
        CollectionAssert.AreEqual(original, Bytes("// synthetic\r\n{001}{a\x80\n}{ alt }{}{LF(unknown)}{}{ACT\x01}\n{2}{player}{}{-5}{??}{-9}{}"));
        Assert.AreEqual((byte)'a', document.Records[0].Text.Bytes[0]);
        Assert.IsTrue(document.TryGetRecord(1, out var found));
        Assert.AreSame(document.Records[0], found);
        Assert.IsFalse(document.TryGetRecord(999, out _));
    }

    [TestMethod]
    public void Read_classifies_player_responses_and_int32_boundaries()
    {
        var document = DlgReader.Read(Bytes("{2147483647}{}{}{}{}{}{}{-2147483648}{}{}{1}{}{2147483647}{}"));
        Assert.AreEqual(int.MaxValue, document.Records[0].Id);
        Assert.AreEqual(int.MinValue, document.Records[1].Id);
        Assert.AreEqual(DlgResponseKind.Direct, document.Records[1].ResponseKind);
        Assert.AreEqual(int.MaxValue, document.Records[1].ResponseValue);

        foreach (var value in new[] { "+1", " 1", "-", "2147483648", "-2147483649" })
        {
            Throws<DlgFormatException>(() => DlgReader.Read(Bytes($"{{{value}}}{{}}{{}}{{}}{{}}{{}}{{}}")));
        }
        Throws<DlgFormatException>(() => DlgReader.Read(Bytes("{1}{}{}{0}{}{}{}")));
        Throws<DlgFormatException>(() => DlgReader.Read(Bytes("{1}{}{}{-0}{}{}{}")));
        Throws<DlgFormatException>(() => DlgReader.Read(Bytes("{1}{}{}{}{}{}{} {01}{}{}{}{}{}{}")));
    }

    [TestMethod]
    public void Read_rejects_bounded_grammar_and_limits_with_structural_diagnostics()
    {
        var cases = new (byte[] Bytes, Type ExceptionType)[]
        {
            (Array.Empty<byte>(), typeof(DlgFormatException)),
            (Bytes("{1}{}{}{}{}{}"), typeof(DlgFormatException)),
            (Bytes("{1}{}{}{}{}{}{}{}"), typeof(DlgFormatException)),
            (Bytes("{1}{nested{}}{}{}{}{}{}"), typeof(DlgUnsupportedFormatException)),
            (Bytes("{1}{unterminated"), typeof(DlgFormatException)),
            (Bytes("{1}{}{}{}{}{}{}\t"), typeof(DlgUnsupportedFormatException)),
            (Bytes("{1}{}{}{}{}{}{}\r"), typeof(DlgUnsupportedFormatException)),
            (Bytes("{1}{}{}{}{}{}{}//\0"), typeof(DlgFormatException)),
        };
        foreach (var testCase in cases)
        {
            try
            {
                DlgReader.Read(testCase.Bytes);
                Assert.Fail("Expected an exception.");
            }
            catch (DlgException exception)
            {
                Assert.AreEqual(testCase.ExceptionType, exception.GetType());
                Assert.IsTrue(exception.Offset >= 0);
                Assert.IsFalse(exception.Message.Contains("nested", StringComparison.OrdinalIgnoreCase) && exception.Message.Contains("{"));
            }
        }

        var one = Bytes("{1}{abc}{}{}{}{}{}");
        Assert.AreEqual(one.Length, DlgReader.Read(one, new DlgReaderOptions { MaxInputBytes = one.Length }).PayloadLength);
        Throws<DlgLimitExceededException>(() => DlgReader.Read(one, new DlgReaderOptions { MaxInputBytes = one.Length - 1 }));
        Throws<DlgLimitExceededException>(() => DlgReader.Read(one, new DlgReaderOptions { MaxFieldBytes = 2 }));
        Throws<DlgLimitExceededException>(() => DlgReader.Read(Bytes("{1}{}{}{}{}{}{}{2}{}{}{}{}{}{}"), new DlgReaderOptions { MaxRecordCount = 1 }));
        foreach (var options in new[]
        {
            new DlgReaderOptions { MaxInputBytes = 0 }, new DlgReaderOptions { MaxRecordCount = 0 }, new DlgReaderOptions { MaxFieldBytes = 0 },
        })
        {
            Throws<ArgumentOutOfRangeException>(() => DlgReader.Read(one, options));
        }
    }

    [TestMethod]
    public void Read_stream_honors_position_short_reads_disposal_and_limits()
    {
        var data = Bytes("prefix{1}{}{}{}{}{}{}");
        using var retained = new MemoryStream(data);
        retained.Position = 6;
        var document = DlgReader.Read(retained, leaveOpen: true);
        Assert.AreEqual(1, document.Records.Count);
        Assert.IsTrue(retained.CanRead);

        var shortRead = new ShortReadStream(Bytes("{1}{}{}{}{}{}{}"));
        var shortDocument = DlgReader.Read(shortRead);
        Assert.AreEqual(1, shortDocument.Records.Count);
        Assert.IsTrue(shortRead.Disposed);

        var failure = new ShortReadStream(Bytes("{1}"));
        Throws<DlgFormatException>(() => DlgReader.Read(failure));
        Assert.IsTrue(failure.Disposed);
        Throws<ArgumentException>(() => DlgReader.Read(new UnreadableStream()));
    }

    private static byte[] Bytes(string value) => Encoding.Latin1.GetBytes(value);

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        Assert.Fail($"Expected {typeof(TException).Name}.");
    }

    private sealed class ShortReadStream : MemoryStream
    {
        public ShortReadStream(byte[] bytes) : base(bytes, writable: false) { }
        public bool Disposed { get; private set; }
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 2));
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class UnreadableStream : Stream
    {
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
    }
}
