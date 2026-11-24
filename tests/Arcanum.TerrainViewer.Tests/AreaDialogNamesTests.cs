using System.Text;
using Arcanum.Data.Area;
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
}
