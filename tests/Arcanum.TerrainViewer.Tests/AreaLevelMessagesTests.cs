using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-046: the level-up message as the original composes it (ledger OL27; 0x4A7030 and the purchase log 0x4A7B90). All texts are
// invented synthetic MES lines.
[TestClass]
public sealed class AreaLevelMessagesTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    private static AreaLevelTexts Texts() => AreaLevelTexts.Create(new Dictionary<string, MesFile>
    {
        [AreaLevelTexts.InterfacePath] = Mes("{11}{Lvl %d!}{12}{Pts %d (100%%)}{21}{TITLE}"),
        [AreaLevelTexts.LevelPath] = Mes("{1}{DONE}{2}{Bought: }{3}{none}{4}{norule}{5}{HP}{6}{FT}"),
        [AreaLevelTexts.StatPath] = Mes("{0}{ST}{6}{WP}"),
        [AreaLevelTexts.SkillPath] = Mes("{2}{Melee}{12}{Repair}"),
        [AreaLevelTexts.TechPath] = Mes("{3}{Smithy}{10}{Journeyman}"),
        [AreaLevelTexts.SpellPath] = Mes("{22}{Flame}"),
    });

    [TestMethod]
    public void The_points_message_formats_the_level_and_the_points()
    {
        var entry = AreaLevelMessages.Compose(new(4, 57, null), Texts());
        Assert.AreEqual(new AreaMessageEntry("TITLE", "Lvl 4!", "Pts 57 (100%)"), entry);
        // Without the files the project's labels stand in.
        Assert.AreEqual(new AreaMessageEntry("Level up", "Level 4", "Unspent points: 57"), AreaLevelMessages.Compose(new(4, 57, null), AreaLevelTexts.None));
    }

    [TestMethod]
    public void A_scheme_names_its_purchases_as_the_original_logs_them()
    {
        AdvancementPurchase P(AdvancementTrack track, int index, int value) => new(track, index, value);
        var scheme = new SessionLevelUpScheme(true,
        [
            P(AdvancementTrack.Stat, 6, 10), P(AdvancementTrack.Stat, 6, 11), P(AdvancementTrack.BasicSkill, 2, 1), P(AdvancementTrack.TechSkill, 0, 2),
            P(AdvancementTrack.College, 4, 3), P(AdvancementTrack.TechDiscipline, 3, 2), P(AdvancementTrack.HitPoints, 0, 44), P(AdvancementTrack.Fatigue, 0, 0),
        ], Completed: true);
        // A repeated name keeps its first place with the last value; a degree shows its name; a value of 0 shows no number; the last
        // separator becomes ". "; the completion follows a space.
        Assert.AreEqual("Bought: WP 11, Melee 1, Repair 2, Flame 3, Smithy Journeyman, HP 44, FT.  DONE", AreaLevelMessages.SchemeText(scheme, Texts()));
        Assert.AreEqual("Lvl 2!", AreaLevelMessages.Compose(new(2, 0, scheme), Texts()).Line);
    }

    [TestMethod]
    public void Nothing_bought_and_no_rule_have_their_own_messages()
    {
        Assert.AreEqual("Bought: none", AreaLevelMessages.SchemeText(new(true, [], false), Texts()));
        Assert.AreEqual("Bought: none DONE", AreaLevelMessages.SchemeText(new(true, [], true), Texts()));
        Assert.AreEqual("Bought: norule", AreaLevelMessages.SchemeText(new(false, [], false), Texts()));
    }

    [TestMethod]
    public void The_log_keeps_at_most_ten_names()
    {
        var purchases = Enumerable.Range(0, 12).Select(i => new AdvancementPurchase(AdvancementTrack.BasicSkill, i, 1)).ToList();
        // An update of a logged name still counts after the log is full.
        purchases.Add(new(AdvancementTrack.BasicSkill, 0, 5));
        var text = AreaLevelMessages.SchemeText(new(true, purchases, false), AreaLevelTexts.None);
        Assert.AreEqual("Auto-level: skill 0 5, " + string.Join(", ", Enumerable.Range(1, 9).Select(i => $"skill {i} 1")) + ". ", text);
    }

    [TestMethod]
    public void The_format_takes_one_number()
    {
        Assert.AreEqual("a 7 b %d c %s", AreaLevelMessages.Format("a %d b %d c %s", 7));
        Assert.AreEqual("7%", AreaLevelMessages.Format("%i%%", 7));
        Assert.AreEqual("plain", AreaLevelMessages.Format("plain", 7));
        Assert.AreEqual("end %", AreaLevelMessages.Format("end %", 7));
    }
}
