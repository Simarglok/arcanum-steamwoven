using System.Text;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Messages;

[TestClass]
public sealed class MesFileTests
{
    private static MesFile Parsed(string text)
    {
        var result = MesFile.Parse(Encoding.Latin1.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, result.Status, $"line {result.Line}");
        return result.File!;
    }

    private static MesParseResult Refused(byte[] bytes, MesParseStatus status, MesReaderOptions? options = null)
    {
        var result = MesFile.Parse(bytes, options);
        Assert.AreEqual(status, result.Status);
        Assert.IsNull(result.File);
        return result;
    }

    private static MesParseResult Refused(string text, MesParseStatus status, MesReaderOptions? options = null) =>
        Refused(Encoding.Latin1.GetBytes(text), status, options);

    [TestMethod]
    public void Messages_are_ordered_by_number_and_text_outside_braces_is_ignored()
    {
        var file = Parsed("// header\r\n{20}{Ring}\r\nFEMALE {10}{Coat}  trailing }\r\n\r\n{5}{}\r\n{7}\r\n{split across lines}\r\n");

        CollectionAssert.AreEqual(new[] { 5, 7, 10, 20 }, file.Messages.Select(m => m.Number).ToArray());
        Assert.AreEqual(4, file.Count);
        Assert.AreEqual(20, file.LastNumber);
        Assert.AreEqual("Coat", Encoding.ASCII.GetString(file.Find(10)!.Bytes.Span));
        Assert.AreEqual(0, file.Find(5)!.Bytes.Length, "An empty text is a message, not an absence.");
        Assert.AreEqual("split across lines", Encoding.ASCII.GetString(file.Find(7)!.Bytes.Span), "The two fields may sit on different lines.");
        CollectionAssert.AreEqual(new[] { 5, 6, 3, 2 }, file.Messages.Select(m => m.Line).ToArray());
        Assert.IsNull(file.Find(6));
        Assert.IsNull(file.Find(-1));
    }

    [TestMethod]
    public void Text_bytes_are_preserved_exactly()
    {
        byte[] stored = [0x20, 0xC0, 0xFE, 0x09, 0x7F, (byte)'a', 0x20];
        var payload = "{1}{"u8.ToArray().Concat(stored).Concat("}"u8.ToArray()).ToArray();
        var file = MesFile.Parse(payload).File!;

        CollectionAssert.AreEqual(stored, file.Find(1)!.Bytes.ToArray(), "No trimming, decoding or translation.");
        payload[5] = 0x41;
        Assert.AreEqual(0xC0, file.Find(1)!.Bytes.Span[1], "The file owns a copy of the payload.");
    }

    [TestMethod]
    public void Empty_and_comment_only_payloads_have_no_messages()
    {
        foreach (var text in new[] { "", "// nothing\r\n", "stray } only" })
        {
            var file = Parsed(text);
            Assert.AreEqual(0, file.Count);
            Assert.IsNull(file.LastNumber);
        }
    }

    [TestMethod]
    public void Shapes_the_original_truncates_guesses_or_cannot_hold_are_refused()
    {
        Assert.AreEqual(2, Refused("{1}{a}\n{2}{b", MesParseStatus.UnterminatedField).Line);
        Refused("{1", MesParseStatus.UnterminatedField);
        Assert.AreEqual(2, Refused("{1}{a}\n{2}", MesParseStatus.MissingText).Line);
        Refused("{1}{a{b}", MesParseStatus.BraceInField);
        Refused("{1{}{a}", MesParseStatus.BraceInField);
        Assert.AreEqual(2, Refused("{1}{a}\n{2}{b\r\nc}", MesParseStatus.LineBreakInText).Line);
        Refused("{1}{b\nc}", MesParseStatus.LineBreakInText);
        Refused(new byte[] { (byte)'{', (byte)'1', (byte)'}', (byte)'{', 0, (byte)'}' }, MesParseStatus.NulByte);
        Refused(new byte[] { (byte)'{', (byte)'1', (byte)'}', (byte)'{', 0xFF, (byte)'}' }, MesParseStatus.EndOfFileByte);
        var duplicate = Refused("{1}{a}\n{9}{b}\n{1}{c}", MesParseStatus.DuplicateNumber);
        Assert.AreEqual((3, 1), (duplicate.Line, duplicate.Number));
    }

    [TestMethod]
    public void Number_fields_are_plain_decimal_digits_within_int32()
    {
        foreach (var number in new[] { "", "-1", "+1", " 1", "1 ", "1a", "0x10", "2147483648", "12345678901" })
            Refused("{" + number + "}{text}", MesParseStatus.UnsupportedNumber);
        Assert.AreEqual(int.MaxValue, Parsed("{2147483647}{max}").LastNumber);
        Assert.AreEqual(7, Parsed("{0007}{leading zeros}").LastNumber);
    }

    [TestMethod]
    public void Limits_are_explicit()
    {
        Assert.AreEqual(1999, Parsed("{1}{" + new string('x', 1999) + "}").Find(1)!.Bytes.Length);
        Refused("{1}{" + new string('x', 2000) + "}", MesParseStatus.TextTooLong);
        Refused("{1}{abcd}", MesParseStatus.TextTooLong, new() { MaxTextBytes = 3 });
        Refused("{1}{a}{2}{b}", MesParseStatus.TooManyMessages, new() { MaxMessages = 1 });
        Refused("{1}{a}", MesParseStatus.TooLarge, new() { MaxBytes = 5 });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MesFile.Parse("{1}{a}"u8, new() { MaxTextBytes = 2000 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MesFile.Parse("{1}{a}"u8, new() { MaxMessages = 0 }));
    }

    [TestMethod]
    public void Decoding_needs_a_declared_profile_and_never_replaces_bytes()
    {
        byte[] cyrillic = [0xCA, 0xEE, 0xEB, 0xFC, 0xF6, 0xEE]; // "Кольцо" in code page 1251
        Assert.AreEqual("Кольцо", MesText.Decode(cyrillic, MesTextProfile.Windows1251).Text);
        Assert.AreEqual("Êîëüöî", MesText.Decode(cyrillic, MesTextProfile.Windows1252).Text, "The same bytes under another profile.");
        Assert.AreEqual(new MesDecodeResult(MesDecodeStatus.UndefinedByte, null, 0), MesText.Decode(cyrillic, MesTextProfile.Ascii));
        Assert.AreEqual("Ring", MesText.Decode("Ring"u8, MesTextProfile.Ascii).Text);
        Assert.AreEqual("€ Œ ™ ÿ", MesText.Decode(new byte[] { 0x80, 0x20, 0x8C, 0x20, 0x99, 0x20, 0xFF }, MesTextProfile.Windows1252).Text);
        Assert.AreEqual("Ё ё № я Ђ", MesText.Decode(new byte[] { 0xA8, 0x20, 0xB8, 0x20, 0xB9, 0x20, 0xFF, 0x20, 0x80 }, MesTextProfile.Windows1251).Text);

        foreach (var undefined in new byte[] { 0x81, 0x8D, 0x8F, 0x90, 0x9D })
            Assert.AreEqual(1, MesText.Decode(new byte[] { 0x41, undefined }, MesTextProfile.Windows1252).Offset);
        Assert.AreEqual(MesDecodeStatus.UndefinedByte, MesText.Decode(new byte[] { 0x98 }, MesTextProfile.Windows1251).Status);

        var defined1252 = Enumerable.Range(0x80, 128).Count(b => MesText.Decode(new[] { (byte)b }, MesTextProfile.Windows1252).Status == MesDecodeStatus.Decoded);
        var defined1251 = Enumerable.Range(0x80, 128).Select(b => MesText.Decode(new[] { (byte)b }, MesTextProfile.Windows1251).Text).Where(t => t is not null).ToArray();
        Assert.AreEqual(123, defined1252);
        Assert.AreEqual(127, defined1251.Length);
        Assert.AreEqual(127, defined1251.Distinct().Count(), "Every defined code point is distinct.");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MesText.Decode("a"u8, (MesTextProfile)99));
    }
}
