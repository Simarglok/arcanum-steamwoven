using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaDialogNamesTests
{
    private static readonly DialogSpeakerNames Fallback = new("[PC]", "[NPC]");

    [TestMethod]
    public void The_pc_name_is_the_hero_player_name_up_to_its_first_zero_and_empty_when_unset()
    {
        // ARC-SCRIPT-2787 (0x441B60, field 0x10E): the hero's own name; an unset name is the empty string; no hero keeps the fallback.
        using var none = ConversationSessionFixture.Create();
        Assert.IsNull(AreaDialogNames.PcName(none));
        Assert.AreEqual(Fallback, AreaDialogNames.For(none, Fallback, null));

        using var hero = ConversationSessionFixture.CreateWithHero();
        Assert.AreEqual(string.Empty, AreaDialogNames.PcName(hero), "a hero with no name set");
        Assert.AreEqual(GameSessionDisposition.Changed, hero.SetHeroPlayerName(hero.Version, Encoding.Latin1.GetBytes("Zedé")).Disposition);
        Assert.AreEqual("Zedé", AreaDialogNames.PcName(hero), "Windows-1252 bytes, decoded as the game's texts are");
        Assert.AreEqual(new DialogSpeakerNames("Zedé", "[NPC]"), AreaDialogNames.For(hero, Fallback, null));
        Assert.AreEqual(new DialogSpeakerNames("Zedé", "Wren"), AreaDialogNames.For(hero, Fallback, "Wren"));
    }

    [TestMethod]
    public void The_npc_name_is_the_sources_text_empty_without_text_and_the_fallback_without_a_source()
    {
        var row = new AreaObjectRecord(new AreaObjectKey(new string('a', 64), 1, AreaSourceKind.Mobile, 1, 0, 0L, new string('b', 64), 100), 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);
        Assert.IsNull(AreaDialogNames.NpcName(null, row), "no source: the fallback applies");
        Assert.AreEqual("Wren", AreaDialogNames.NpcName(r => r == row ? "Wren" : null, row));
        Assert.AreEqual(string.Empty, AreaDialogNames.NpcName(_ => null, row), "a name without text prints empty");
        Assert.AreEqual(string.Empty, AreaDialogNames.NpcName(_ => "Wren", null), "an unknown speaker prints empty when a source is wired");
    }

    private static AreaObjectRecord Row(int type, params (int Slot, long Value)[] fields) =>
        new(new AreaObjectKey(new string('a', 64), 1, AreaSourceKind.Mobile, 1, 0, 0L, new string('b', 64), 100), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10, 20,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [.. fields.Select(f => new AreaFieldValue(f.Slot, AreaPresence.Value, AreaValueOrigin.Instance, f.Value, [], null))], true, true, true);

    [TestMethod]
    public void The_object_name_source_names_an_npc_by_its_observer_and_an_item_by_the_sessions_identified_mark()
    {
        // ARC-SCRIPT-2811 (0x441B60 over 0x45F970): an NPC its own observer is recognized (its description), with no observer not (its unknown description); an unidentified item shows its unknown description, identified or marked identified its own.
        var catalog = ObjectDescriptionCatalog.Create(MesFile.Parse(Encoding.ASCII.GetBytes("{100}{Coat}{101}{Odd Coat}{200}{Stranger}{201}{Bandit}")).File!);
        var source = AreaDialogNames.ObjectNameSource(() => catalog);
        var npc = Row(16, (ObjectDisplayName.DescriptionSlot, 201), (ObjectDisplayName.CritterUnknownDescriptionSlot, 200));
        Assert.AreEqual("Bandit", source(npc, CritterRecognition.Recognized, false));
        Assert.AreEqual("Stranger", source(npc, CritterRecognition.NotRecognized, false));
        var coat = Row(7, (ObjectDisplayName.DescriptionSlot, 100), (ObjectDisplayName.ItemUnknownDescriptionSlot, 101), (ObjectDisplayName.ItemComplexitySlot, 3), (ObjectDisplayName.ItemFlagsSlot, 0));
        Assert.AreEqual("Odd Coat", source(coat, CritterRecognition.Recognized, false));
        Assert.AreEqual("Coat", source(coat, CritterRecognition.Recognized, true), "the session marked it identified");
        Assert.AreEqual("Coat", source(Row(7, (ObjectDisplayName.DescriptionSlot, 100), (ObjectDisplayName.ItemUnknownDescriptionSlot, 101), (ObjectDisplayName.ItemComplexitySlot, 3), (ObjectDisplayName.ItemFlagsSlot, 1)), CritterRecognition.Recognized, false));
        Assert.IsNull(source(Row(7, (ObjectDisplayName.DescriptionSlot, 999), (ObjectDisplayName.ItemComplexitySlot, 0)), CritterRecognition.Recognized, false), "a name without text");
        Assert.IsNull(AreaDialogNames.ObjectNameSource(() => null)(npc, CritterRecognition.Recognized, false), "tables not loaded");
        Assert.AreEqual("Bandit", AreaDialogNames.NpcNameSource(() => catalog)(npc), "the speaker's source is the recognized reading");
        using var session = ConversationSessionFixture.Create();
        Assert.AreEqual(new DialogSpeakerNames("[PC]", "Wren"), AreaDialogNames.Of(session, Fallback, null, "Wren"));
        Assert.AreEqual(new DialogSpeakerNames("Odd", "[NPC]"), AreaDialogNames.Of(session, Fallback, "Odd", null), "an NPC triggerer's name is not the hero's");
    }
}
