using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Hotkeys;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-035: the hover descriptions of the hotkey bar (ledger HK19–HK23): the dispatch of 0x550150, the skill and spell hints
/// of 0x5529C0 cases 7 and 8 and the item window's first part; synthetic texts, expected results worked by hand.</summary>
[TestClass]
public sealed class AreaHoverDescriptionsTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;
    private static readonly AreaGameBarTexts Labels = AreaGameBarTexts.Project("test");

    [TestMethod]
    public void The_dispatch_follows_the_entry_type()
    {
        Assert.AreEqual(new AreaHotkeyHover(AreaHotkeyHoverKind.Item, "kit:2"), AreaHoverDescriptions.Dispatch(HotkeyEntry.ForItem("kit:2"), false));
        Assert.AreEqual(new AreaHotkeyHover(AreaHotkeyHoverKind.Skill, null, 3), AreaHoverDescriptions.Dispatch(HotkeyEntry.ForSkill(SkillBarButton.Repair), false));
        Assert.AreEqual(new AreaHotkeyHover(AreaHotkeyHoverKind.Spell, null, 12), AreaHoverDescriptions.Dispatch(HotkeyEntry.ForSpell(12), false));
        Assert.AreEqual(new AreaHotkeyHover(AreaHotkeyHoverKind.Spell, null, 40), AreaHoverDescriptions.Dispatch(HotkeyEntry.ForItemSpell("kit:5", 40), false),
            "an item's spell shows the spell's hint (type 4 → 0x5507E0)");
        Assert.AreEqual(AreaHotkeyHoverKind.None, AreaHoverDescriptions.Dispatch(HotkeyEntry.ForSpell(12), dragged: true).Kind, "flag 1: nothing");
        Assert.AreEqual(AreaHotkeyHoverKind.None, AreaHoverDescriptions.Dispatch(HotkeyEntry.Empty, false).Kind);
    }

    [TestMethod]
    public void A_skill_hint_names_the_buttons_skill()
    {
        var texts = SkillTexts.Create(Mes("{5}{Pick Pocket}\n{6}{Prowling}\n{25}{Lift purses.}\n{26}{Move unseen.}\n{12}{Repair}\n{15}{Disarm Traps}\n{32}{Mend.}\n{35}{Defuse.}\n"));
        static string Of(AreaMessageEntry e) => $"{e.Kind} '{e.Title}' '{e.Detail}' '{e.Value}'";
        Assert.AreEqual("SkillHint 'Prowling' 'Move unseen.' ''", Of(AreaHoverDescriptions.SkillHint(texts, Labels, 0)));
        Assert.AreEqual("SkillHint 'Pick Pocket' 'Lift purses.' ''", Of(AreaHoverDescriptions.SkillHint(texts, Labels, 1)));
        Assert.AreEqual("SkillHint 'Disarm Traps' 'Defuse.' ''", Of(AreaHoverDescriptions.SkillHint(texts, Labels, 2)), "tech skill 3");
        Assert.AreEqual("SkillHint 'Repair' 'Mend.' ''", Of(AreaHoverDescriptions.SkillHint(texts, Labels, 3)), "tech skill 0");
        Assert.AreEqual("SkillHint 'Repair' '' ''", Of(AreaHoverDescriptions.SkillHint(null, Labels, 3)), "without skill.mes: the button's name");
        Assert.AreEqual("Pick Pocket", Labels.SkillButtonName(1));
    }

    [TestMethod]
    public void A_spell_hint_shows_willpower_cost_and_maintenance()
    {
        static AreaSpellRow Row(int? cost, int? maintain, int? period) => new(7, 3, "Spark", "Zap.", 5, 6, cost, maintain, period, AreaSpellState.Known);
        static string Of(AreaMessageEntry e) => $"{e.Icon} '{e.Title}' '{e.Line}' '{e.Value}' '{e.Detail}'";
        Assert.AreEqual("447 'Spark' 'Minimum willpower: 6' 'Fatigue cost: 5' 'Zap.'", Of(AreaHoverDescriptions.SpellHint(7, Row(5, null, null), null, Labels)),
            "spell 7 is college 1's: art 447");
        Assert.AreEqual("447 'Spark' 'Minimum willpower: 6' 'Fatigue cost: 5  (1 / second)' 'Zap.'", Of(AreaHoverDescriptions.SpellHint(7, Row(5, 1, 1), null, Labels)));
        Assert.AreEqual("447 'Spark' 'Minimum willpower: 6' 'Fatigue cost: 5  (2 / 10 seconds)' 'Zap.'", Of(AreaHoverDescriptions.SpellHint(7, Row(5, 2, 10), null, Labels)));
        Assert.AreEqual("447 'Spark' 'Minimum willpower: 6' '' 'Zap.'", Of(AreaHoverDescriptions.SpellHint(7, Row(null, null, null), null, Labels)), "no spell data: no cost");
        Assert.AreEqual("446 'College 1 spell 2' '' '' ''", Of(AreaHoverDescriptions.SpellHint(1, null, null, Labels)), "no hero: the name only");
        Assert.AreEqual(AreaMessageKind.SpellHint, AreaHoverDescriptions.SpellHint(1, null, null, Labels).Kind);
    }

    [TestMethod]
    public void The_item_window_and_pane_names()
    {
        var window = AreaHoverDescriptions.ItemWindow(new(393, "Magic Sword", AreaMessageColour.Magick));
        Assert.AreEqual((AreaMessageKind.Item, "Magic Sword", (int?)393, AreaMessageColour.Magick), (window.Kind, window.Title, window.Icon, window.TitleColour));
        Assert.AreEqual("Repair", AreaHoverDescriptions.PaneName(new(AreaHotkeyHoverKind.Skill, null, 3), Labels, null, _ => "x"));
        Assert.AreEqual("College 2 spell 1", AreaHoverDescriptions.PaneName(new(AreaHotkeyHoverKind.Spell, null, 5), Labels, null, _ => "x"));
        Assert.AreEqual("Sword", AreaHoverDescriptions.PaneName(new(AreaHotkeyHoverKind.Item, "kit:1"), Labels, null, item => item == "kit:1" ? "Sword" : "?"));
    }
}
