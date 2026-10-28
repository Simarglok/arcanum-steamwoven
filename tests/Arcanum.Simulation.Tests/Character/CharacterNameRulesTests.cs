using System.Collections.Immutable;
using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

// The new-character window's name field (ARC-UI-033, ledger NW6-NW8) as the retail 1.0.7.4 executable runs it. The default name is
// invented (the game's message 500 is not used).
[TestClass]
public sealed class CharacterNameRulesTests
{
    private static readonly byte[] Default = "Your Name"u8.ToArray();

    private static ImmutableArray<byte> Bytes(string text) => [.. text.Select(c => (byte)c)];

    [TestMethod]
    public void The_field_takes_every_byte_from_0x20_and_stops_at_22_bytes()
    {
        Assert.AreEqual((23, 22), (CharacterNameRules.FieldLength, CharacterNameRules.TypedLength));
        for (var b = 0; b < 256; b++) Assert.AreEqual(b >= 0x20, CharacterNameRules.Accepts((byte)b), $"byte {b:X2}");
        ImmutableArray<byte> name = [];
        for (var i = 0; i < CharacterNameRules.TypedLength; i++) name = CharacterNameRules.Type(name, (byte)('a' + i % 26))!.Value;
        Assert.AreEqual(22, name.Length);
        Assert.IsNull(CharacterNameRules.Type(name, (byte)'x'), "a full field refuses the next byte");
        Assert.IsNull(CharacterNameRules.Type([], 0x0D), "a control byte");
        CollectionAssert.AreEqual(new byte[] { 0xE9 }, CharacterNameRules.Type([], 0xE9)!.Value.ToArray(), "an upper-half byte");
        Assert.AreEqual(21, CharacterNameRules.Backspace(name).Length);
        Assert.AreEqual(0, CharacterNameRules.Backspace([]).Length);
    }

    [TestMethod]
    public void The_default_name_is_the_message_cut_to_the_field()
    {
        CollectionAssert.AreEqual(Default, CharacterNameRules.Default(Default).ToArray());
        var longMessage = Enumerable.Repeat((byte)'n', 30).ToArray();
        Assert.AreEqual(23, CharacterNameRules.Default(longMessage).Length, "strncpy of 23 bytes");
        CollectionAssert.AreEqual(Bytes("ab").ToArray(), CharacterNameRules.Default([0x61, 0x62, 0x00, 0x63]).ToArray(), "up to the NUL");
    }

    [TestMethod]
    public void Enter_gives_an_empty_field_the_default_and_a_click_elsewhere_its_old_text()
    {
        var before = Bytes("Old");
        CollectionAssert.AreEqual(Default, CharacterNameRules.EndTyping([], before, enter: true, Default).ToArray());
        CollectionAssert.AreEqual(before.ToArray(), CharacterNameRules.EndTyping([], before, enter: false, Default).ToArray());
        CollectionAssert.AreEqual(Bytes("New").ToArray(), CharacterNameRules.EndTyping(Bytes("New"), before, enter: true, Default).ToArray());
        CollectionAssert.AreEqual(Bytes("New").ToArray(), CharacterNameRules.EndTyping(Bytes("New"), before, enter: false, Default).ToArray());
    }

    [TestMethod]
    public void Accept_refuses_the_default_name_and_an_empty_field()
    {
        Assert.IsTrue(CharacterNameRules.Accept(Bytes("Virgil"), Default).Accepted);
        CollectionAssert.AreEqual(Bytes("Virgil").ToArray(), CharacterNameRules.Accept(Bytes("Virgil"), Default).Name.ToArray());
        var empty = CharacterNameRules.Accept([], Default);
        Assert.IsFalse(empty.Accepted, "an empty field takes the default name, which Accept refuses");
        CollectionAssert.AreEqual(Default, empty.Name.ToArray());
        Assert.IsFalse(CharacterNameRules.Accept([.. Default], Default).Accepted);
        Assert.IsTrue(CharacterNameRules.Accept(Bytes("your name"), Default).Accepted, "strcmp: case counts");
        // A message longer than the field: the field holds its first 23 bytes, which differ from the message, so Accept stores them.
        var longMessage = Enumerable.Repeat((byte)'n', 30).ToArray();
        Assert.IsTrue(CharacterNameRules.Accept(CharacterNameRules.Default(longMessage), longMessage).Accepted);
        Assert.IsFalse(CharacterNameRules.Accept([], longMessage).Accepted);
    }
}
