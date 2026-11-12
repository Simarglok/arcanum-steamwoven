using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Magic;
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
        // ARC-DEV-304: spell 56's flag is no hold.
        Assert.AreEqual("Rat r1 can be spoken with", AreaSpellCasting.Line(new(SpellCastEventKind.ObjectFlagWritten, 1, 0x2000000, "r1", 1), null, Name));
        Assert.AreEqual("Rat r1 can no longer be spoken with", AreaSpellCasting.Line(new(SpellCastEventKind.ObjectFlagWritten, 1, 0x2000000, "r1", 0), null, Name));
        // ARC-DEV-358 (MA-1): every other flag, and the object flags a side effect wrote, tell no line (the original tells none); a hold's bit in the object flags is no hold.
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.ObjectFlagWritten, 1, 0x1, "r1", 1), null, Name), "invisible");
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.ObjectFlagWritten, 1, 0x200, "r1", 5), null, Name), "the invisible object flag");
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.ObjectFlagWritten, 1, 0x40, "r1", 5), null, Name), "the translucent object flag is no paralysis");
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.ObjectFlagWritten, 1, 0x10, "r1", 3), null, Name), "a critter flag that is no hold");
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.ObjectFlagWritten, 1, 0x40, "r1", 3 | SessionSpellCaster.SideEffectDetail), null, Name), "the paralysed bit don't-draw writes is no hold line");
    }

    [TestMethod]
    public void The_identify_answer_is_a_text_line_by_the_target_type_and_the_mark_a_line_of_its_own()
    {
        // ARC-MAGIC-032 (MA-7, D5(a)): the windows of the original are E8's; the line says what the window would show.
        Assert.AreEqual("identify: Rat r1 (its character sheet)", AreaSpellCasting.Line(new(SpellCastEventKind.IdentifyCritter, 1, SpellTargetObject.Npc, "r1"), null, Name));
        Assert.AreEqual("identify: Rat r1 (the hero's own sheet is the C key)", AreaSpellCasting.Line(new(SpellCastEventKind.IdentifyCritter, 1, SpellTargetObject.Pc, "r1"), null, Name));
        Assert.AreEqual("identify: the contents of Rat r2 (its contents window)", AreaSpellCasting.Line(new(SpellCastEventKind.IdentifyContainer, 1, 2, "r2"), null, Name));
        Assert.AreEqual("identify: Rat kit:2 (its item description)", AreaSpellCasting.Line(new(SpellCastEventKind.IdentifyItem, 1, 5, "kit:2"), null, Name));
        Assert.AreEqual("Rat kit:2 is identified", AreaSpellCasting.Line(new(SpellCastEventKind.ItemIdentified, 1, 1, "kit:2"), null, Name));
        Assert.AreEqual("the identification was not saved", AreaSpellCasting.Line(new(SpellCastEventKind.ItemIdentifyNotSaved, 1, 3, "kit:2"), null, Name));
    }

    [TestMethod]
    public void The_recharge_of_an_item_tells_its_new_charge_count_and_trait64_tells_nothing()
    {
        // ARC-MAGIC-1146: the Recharge component's answer is a line of the viewer's own; Trait64 writes state only.
        Assert.AreEqual("Rat kit:2 is recharged to 8 charges", AreaSpellCasting.Line(new(SpellCastEventKind.ItemRecharged, 1, 8, "kit:2", 5), null, Name));
        Assert.AreEqual("the recharge was not saved", AreaSpellCasting.Line(new(SpellCastEventKind.ItemRechargeNotSaved, 1, 3, "kit:2"), null, Name));
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.Trait64Written, 1, 242, "r1", 0), null, Name));
        // ARC-MAGIC-1295: TraitIdx writes state only too.
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.TraitIndexWritten, 1, 220, "r1", 1), null, Name));
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.TraitIndexRefused, 1, 0, "r1", 1), null, Name));
    }

    [TestMethod]
    public void The_lock_flags_a_spell_wrote_on_a_portal_or_container_tell_a_line_of_the_viewers_own()
    {
        // ARC-MAGIC-037 (MA-6): the locked bit (0x1) and the magically held bit (0x4); the detail bit 0x1 is on. Any other lock bit, a guard alert and a refused gate tell nothing themselves.
        Assert.AreEqual("Rat d1 is unlocked", AreaSpellCasting.Line(new(SpellCastEventKind.OpenableFlagWritten, 1, 0x1, "d1", 0), null, Name));
        Assert.AreEqual("Rat d1 is locked", AreaSpellCasting.Line(new(SpellCastEventKind.OpenableFlagWritten, 1, 0x1, "d1", 1), null, Name));
        Assert.AreEqual("Rat d1 is held by magick", AreaSpellCasting.Line(new(SpellCastEventKind.OpenableFlagWritten, 1, 0x4, "d1", 1), null, Name));
        Assert.AreEqual("Rat d1 is no longer held by magick", AreaSpellCasting.Line(new(SpellCastEventKind.OpenableFlagWritten, 1, 0x4, "d1", 0), null, Name));
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.OpenableFlagWritten, 1, 0x2, "d1", 1), null, Name));
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.LockGuardAlert, 1, 2, "d1"), null, Name));
        Assert.IsNull(AreaSpellCasting.Line(new(SpellCastEventKind.UnlockRefused, 1, 40, "d1"), null, Name));
        Assert.AreEqual("the lock change was not saved", AreaSpellCasting.Line(new(SpellCastEventKind.OpenableFlagNotSaved, 1, 3, "d1"), null, Name));
    }

    [TestMethod]
    public void A_message_without_its_text_names_its_number()
    {
        Assert.AreEqual("spell message 604", AreaSpellCasting.Line(new(SpellCastEventKind.Message, -1, 604), null, Name));
        Assert.AreEqual("spell message 643", AreaSpellCasting.Message(null, 643));
    }
}
