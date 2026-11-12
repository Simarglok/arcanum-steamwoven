using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-DEV-309 (B21): a script's print line reaches the message window as the entry type its second number names (ledger IS4, ML1–ML2).
/// Synthetic texts only.</summary>
[TestClass]
public sealed class AreaScriptMessagesTests
{
    [TestMethod]
    public void The_second_number_is_the_entry_type_of_the_window()
    {
        Assert.AreEqual(AreaMessageEntry.Text(AreaMessageKind.Hover, "wards"), AreaScriptMessages.Entry(new(6, "wards")));
        Assert.AreEqual(AreaMessageEntry.Text(AreaMessageKind.Message, "a"), AreaScriptMessages.Entry(new(4, "a")));
        Assert.AreEqual(AreaMessageEntry.Text(AreaMessageKind.Plain, "b"), AreaScriptMessages.Entry(new(5, "b")));
        // Never stored for the transient type, stored for the two game-message types.
        Assert.IsFalse(AreaScriptMessages.Entry(new(6, "x"))!.Stored);
        Assert.IsTrue(AreaScriptMessages.Entry(new(4, "x"))!.Stored);
        Assert.IsTrue(AreaScriptMessages.Entry(new(5, "x"))!.Stored);
    }

    [TestMethod]
    public void A_type_the_viewer_does_not_draw_is_not_posted()
    {
        // ARC-SCRIPT-1118 (PY1–PY4): 1 to 3 and 12 are posted; 0 and 7 read an unset number, 8 fields beyond the entry, 9 to 11 and 13 tables a print line has no number for.
        foreach (var type in new[] { 0, 7, 8, 9, 10, 11, 13, -1, 14 })
            Assert.IsNull(AreaScriptMessages.Entry(new(type, "x")), $"type {type}");
    }

    [TestMethod]
    public void Lines_keep_their_print_order_and_drop_the_unposted()
    {
        var entries = AreaScriptMessages.Entries([new(6, "one"), new(0, "skipped"), new(4, "two")]);
        CollectionAssert.AreEqual(new[] { "one", "two" }, entries.Select(e => e.Title).ToArray());
        Assert.AreEqual(0, AreaScriptMessages.Entries([]).Count);
    }

    [TestMethod]
    public void Types_1_2_3_and_12_are_posted_with_the_zero_the_original_builds()
    {
        // ARC-SCRIPT-1118 (PY1–PY4): the print line's entry holds the type, the text and the number 0.
        var poison = AreaScriptMessages.Entry(new(1, "Pillar"))!;
        Assert.AreEqual(AreaMessageKind.PoisonAbsorbed, poison.Kind);
        Assert.AreEqual("Pillar", poison.Title);
        Assert.AreEqual("You have absorbed 0 unit(s) of poison.", poison.Line, "the project's label without the player's texts");
        var texts = AreaLevelTexts.Create(new Dictionary<string, Arcanum.Data.Messages.MesFile>
        {
            [AreaLevelTexts.InterfacePath] = Arcanum.Data.Messages.MesFile.Parse(System.Text.Encoding.Latin1.GetBytes("{13}{Poison taken: %d unit(s).}")).File!,
        });
        Assert.AreEqual("Poison taken: 0 unit(s).", AreaScriptMessages.Entry(new(1, "x"), texts)!.Line, "intgame.mes 13 formatted with 0");
        Assert.AreEqual(AreaMessageEntry.Text(AreaMessageKind.Curse, "c"), AreaScriptMessages.Entry(new(2, "c")));
        Assert.AreEqual(AreaMessageEntry.Text(AreaMessageKind.Blessing, "b"), AreaScriptMessages.Entry(new(3, "b")));
        Assert.AreEqual(AreaMessageEntry.Text(AreaMessageKind.ScriptHover, "h"), AreaScriptMessages.Entry(new(12, "h")));
        Assert.IsTrue(poison.Stored && AreaScriptMessages.Entry(new(2, "c"))!.Stored && AreaScriptMessages.Entry(new(3, "b"))!.Stored);
        Assert.IsFalse(AreaScriptMessages.Entry(new(12, "h"))!.Stored);
    }
}
