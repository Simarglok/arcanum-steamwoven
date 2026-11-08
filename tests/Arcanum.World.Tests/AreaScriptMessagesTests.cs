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
        foreach (var type in new[] { 0, 1, 2, 3, 7, 8, 9, 10, 11, 12, 13, -1, 14 })
            Assert.IsNull(AreaScriptMessages.Entry(new(type, "x")), $"type {type}");
    }

    [TestMethod]
    public void Lines_keep_their_print_order_and_drop_the_unposted()
    {
        var entries = AreaScriptMessages.Entries([new(6, "one"), new(1, "skipped"), new(4, "two")]);
        CollectionAssert.AreEqual(new[] { "one", "two" }, entries.Select(e => e.Title).ToArray());
        Assert.AreEqual(0, AreaScriptMessages.Entries([]).Count);
    }
}
