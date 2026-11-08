using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-MAGIC-015: the viewer's feedback lines of the cast host's log. Names and numbers are invented.
[TestClass]
public sealed class AreaSpellCastingTests
{
    private static string Name(string id) => id == SessionSpellCaster.Hero ? "the hero" : "Rat " + id;

    [TestMethod]
    public void Log_entries_become_feedback_lines()
    {
        Assert.AreEqual("fatigue -5", AreaSpellCasting.Line(new(SpellCastEventKind.FatiguePaid, 1, 5), null, Name));
        Assert.AreEqual("Rat r1 takes 6 damage", AreaSpellCasting.Line(new(SpellCastEventKind.Damaged, 1, 6, "r1"), null, Name));
        Assert.AreEqual("the hero regains 2 hit points", AreaSpellCasting.Line(new(SpellCastEventKind.Healed, 1, 2, SessionSpellCaster.Hero), null, Name));
        Assert.AreEqual("Rat r1 resists", AreaSpellCasting.Line(new(SpellCastEventKind.Resisted, 1, 0, "r1"), null, Name));
        Assert.AreEqual("Summon effect not ready", AreaSpellCasting.Line(new(SpellCastEventKind.ComponentNotReady, 1, (int)SpellComponentType.Summon), null, Name));
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.Freed, 1), null, Name), "a freed run shows nothing");
        // ARC-DEV-302: the hold flags an ObjFlag component wrote; the interrupt request shows nothing.
        Assert.AreEqual("the hero is entangled", AreaSpellCasting.Line(new(SpellCastEventKind.ObjectFlagWritten, 1, 0x1000000, SessionSpellCaster.Hero, 1), null, Name));
        Assert.AreEqual("Rat r1 is no longer paralysed", AreaSpellCasting.Line(new(SpellCastEventKind.ObjectFlagWritten, 1, 0x40, "r1", 2), null, Name));
        Assert.AreEqual("Rat r1 is stoned", AreaSpellCasting.Line(new(SpellCastEventKind.ObjectFlagWritten, 1, 0x10000, "r1", 1), null, Name));
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.HoldStarted, 1, 0x40, "r1", 3), null, Name));
    }

    [TestMethod]
    public void A_message_without_its_text_names_its_number()
    {
        Assert.AreEqual("spell message 604", AreaSpellCasting.Line(new(SpellCastEventKind.Message, -1, 604), null, Name));
        Assert.AreEqual("spell message 643", AreaSpellCasting.Message(null, 643));
    }
}
