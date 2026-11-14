using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1924 (ledger IW46): the weapon stat lines (0x556220) and the armour stat lines (0x5566B0) as the message window lays them out. All values are invented.</summary>
[TestClass]
public sealed class AreaStatLinesTests
{
    private static readonly AreaGameBarTexts Texts = AreaGameBarTexts.Project("self-test");

    private static SessionDollRatings Ratings(int armorClass = 12, int normal = 25, int fire = 0, int electrical = 5, int poison = 0, int magic = 10) => new(17, 33, false)
    {
        Detail = new(62, [(10, 20), (0, 0), (2, 4), (1, 1), (3, 9)], armorClass, normal, fire, electrical, poison, magic),
    };

    [TestMethod]
    public void The_weapon_lines_are_the_rating_the_skill_percent_and_the_five_ranges_in_the_executables_order()
    {
        var entry = AreaStatLines.Weapon(Ratings(), Texts)!;
        Assert.AreEqual(AreaMessageKind.StatLines, entry.Kind);
        Assert.AreEqual(675, entry.Icon);
        Assert.AreEqual("Damage: 17", entry.Title, "0x464630's number after the title (%s: %d)");
        var cells = entry.StatCells!;
        // Row 1: the skill's effectiveness (%d%%) and the normal damage (type 0); row 2: fatigue (type 4) and electrical (2); row 3: fire (3) and poison (1, both ends 0 prints "%s: 0").
        Assert.AreEqual("Skill: 62%", cells.Left1);
        Assert.AreEqual("Normal: 10-20", cells.Right1);
        Assert.AreEqual(Texts.Text(AreaStatLines.WeaponFatigueMessage) + ": 3-9", cells.Left2);
        Assert.AreEqual("Electrical: 2-4", cells.Right2);
        Assert.AreEqual("Fire: 1-1", cells.Left3);
        Assert.AreEqual("Poison: 0", cells.Right3);
    }

    [TestMethod]
    public void The_armour_lines_are_the_rating_the_class_and_the_five_resistances_and_a_zero_has_no_percent_sign()
    {
        var entry = AreaStatLines.Armour(Ratings(), Texts)!;
        Assert.AreEqual(AreaMessageKind.StatLines, entry.Kind);
        Assert.AreEqual(674, entry.Icon);
        Assert.AreEqual("Armor: 33", entry.Title);
        var cells = entry.StatCells!;
        Assert.AreEqual("Armor class: 12", cells.Left1);
        Assert.AreEqual("Normal: 25%", cells.Right1);
        Assert.AreEqual("Magic: 10%", cells.Left2);
        Assert.AreEqual("Electrical: 5%", cells.Right2);
        Assert.AreEqual("Fire: 0", cells.Left3, "a resistance of 0 prints '%s: 0'");
        Assert.AreEqual("Poison: 0", cells.Right3);
        Assert.AreEqual("Armor class: 0", AreaStatLines.Armour(Ratings(armorClass: 0), Texts)!.StatCells!.Left1);
    }

    [TestMethod]
    public void Without_the_detail_there_are_no_lines()
    {
        Assert.IsNull(AreaStatLines.Weapon(null, Texts));
        Assert.IsNull(AreaStatLines.Armour(new SessionDollRatings(1, 2, false), Texts));
    }

    [TestMethod]
    public void The_message_window_draws_the_art_the_title_and_three_rows_of_two_cells_in_white()
    {
        var entry = AreaStatLines.Weapon(Ratings(), Texts)!;
        var draw = AreaMessageLayout.Draw(entry);
        Assert.AreEqual(675, draw.Icon);
        Assert.IsFalse(draw.Widens);
        Assert.AreEqual(7, draw.Texts.Count);
        Assert.AreEqual(((290, 504, 291, 19), AreaMessageFont.Title, "Damage: 17"), (draw.Texts[0].Rect, draw.Texts[0].Font, draw.Texts[0].Text));
        // The left cell on the row's rectangle (flag 1), the right cell on its right half (flag 0x11: the width halved, the left edge moved by it).
        Assert.AreEqual((290, 528, 291, 18), draw.Texts[1].Rect);
        Assert.AreEqual((435, 528, 145, 18), draw.Texts[2].Rect);
        Assert.AreEqual((290, 546, 291, 18), draw.Texts[3].Rect);
        Assert.AreEqual((435, 546, 145, 18), draw.Texts[4].Rect);
        Assert.AreEqual((290, 564, 291, 18), draw.Texts[5].Rect);
        Assert.AreEqual((435, 564, 145, 18), draw.Texts[6].Rect);
        Assert.IsTrue(draw.Texts.Skip(1).All(text => text.Font == AreaMessageFont.Text && !text.Right && text.Colour == AreaMessageColour.White));
        Assert.IsTrue(AreaMessageLayout.ArtNumbers.Contains(674) && AreaMessageLayout.ArtNumbers.Contains(675), "the window reads both arts");
    }

    [TestMethod]
    public void The_stat_lines_are_shown_never_stored()
    {
        var log = new AreaMessageLog();
        var entry = AreaStatLines.Armour(Ratings(), Texts)!;
        Assert.IsFalse(entry.Stored);
        Assert.IsTrue(log.ShowItem(entry));
        Assert.AreSame(entry, log.Shown);
        Assert.ThrowsExactly<ArgumentException>(() => log.Post(entry, 0));
        log.EnterPane();
        Assert.IsFalse(log.ShowItem(entry), "a pane mode takes only the title (0x550770)");
    }
}
