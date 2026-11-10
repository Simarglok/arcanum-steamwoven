using Arcanum.Simulation.Reaction;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-109 (ledger OH7, OH11): a hovered critter's second line and image as 0x553F70 draws them — the level at the right, an
/// NPC's reaction, sneak state or alignment at the left, a dead critter's line alone, and the alignment image of a hero who detects
/// alignment — over invented facts (no original data).</summary>
[TestClass]
public sealed class AreaHoverCritterTests
{
    private static string Of(AreaMessageDraw draw) => string.Join(" | ", draw.Texts.Select(t => $"{t.Rect} {t.Font} '{t.Text}'{(t.Right ? " right" : "")} {t.Colour}"));

    private static AreaHoverCritter Npc(int reaction = 50, int level = 7, int alignment = 120, bool sneaks = false, int excess = 0, bool detects = false, bool dead = false) =>
        new(AreaHoverWindow.NpcType, dead, level, alignment, reaction, sneaks, excess, detects);

    [TestMethod]
    public void ALivingNpcShowsItsReactionAtTheLeftAndItsLevelAtTheRight()
    {
        var entry = AreaHoverWindow.CritterWindow("Virgil", 375, Npc(reaction: 72))!;
        Assert.AreEqual("Reaction: 72 (Courteous)", entry.SecondLine);
        Assert.AreEqual("Level: 7", entry.SecondValue);
        Assert.AreEqual(375, entry.Icon);
        Assert.AreEqual("(290, 504, 291, 19) Title 'Virgil' White | (290, 528, 291, 18) Text 'Reaction: 72 (Courteous)' White | (290, 528, 291, 18) Text 'Level: 7' right White",
            Of(AreaMessageLayout.Draw(entry)));
    }

    [TestMethod]
    public void TheReactionCategoryFollowsThe0x4C0FC0Thresholds()
    {
        string Line(int reaction) => AreaHoverWindow.CritterWindow("N", null, Npc(reaction: reaction))!.SecondLine;
        Assert.AreEqual("Reaction: -5 (Hatred)", Line(-5));
        Assert.AreEqual("Reaction: 20 (Dislike)", Line(20));
        Assert.AreEqual("Reaction: 41 (Neutral)", Line(41), "41..60 is neutral (the thresholds are inclusive upper bounds)");
        Assert.AreEqual("Reaction: 100 (Amiable)", Line(100));
        Assert.AreEqual("Reaction: 101 (Love)", Line(101), "no clamp: a value above 100 is the love category");
        Assert.AreEqual(ReactionCategory.Neutral, ReactionRules.Category(50));
    }

    [TestMethod]
    public void APlayerCharacterOnlyShowsItsLevel()
    {
        var entry = AreaHoverWindow.CritterWindow("Hero", null, new(15, false, 12, 0, 90, false, 0, false))!;
        Assert.AreEqual("", entry.SecondLine, "the left part is an NPC's only (field 0x14F = 16)");
        Assert.AreEqual("Level: 12", entry.SecondValue);
    }

    [TestMethod]
    public void ADeadCritterShowsTheDeadLineAloneInRed()
    {
        var entry = AreaHoverWindow.CritterWindow("Corpse", 375, Npc(dead: true, sneaks: true, detects: true))!;
        Assert.AreEqual("Dead", entry.SecondLine);
        Assert.AreEqual("", entry.SecondValue, "no level for a dead critter");
        Assert.AreEqual("(290, 504, 291, 19) Title 'Corpse' White | (290, 528, 291, 18) Text 'Dead' Red", Of(AreaMessageLayout.Draw(entry)));
        // The image is chosen before the dead test, so a hero who detects alignment still sees the alignment image of a corpse.
        Assert.AreEqual(AreaHoverWindow.AlignmentImage(120), entry.Icon);
    }

    [TestMethod]
    public void AConcealedHeroSeesTheSneakStateBeforeAlignmentAndReaction()
    {
        string Line(int excess, bool detects = false) => AreaHoverWindow.CritterWindow("N", null, Npc(sneaks: true, excess: excess, detects: detects))!.SecondLine;
        Assert.AreEqual("Sneak: Noticed", Line(0));
        Assert.AreEqual("Sneak: Nearly noticed", Line(1));
        Assert.AreEqual("Sneak: Close", Line(2));
        Assert.AreEqual("Sneak: Far", Line(3));
        Assert.AreEqual("Sneak: Hidden", Line(4));
        Assert.AreEqual("Sneak: Hidden", Line(1000), "an NPC that cannot perceive at all");
        Assert.AreEqual("Sneak: Noticed", Line(0, detects: true), "the sneak line wins over the alignment line");
        Assert.AreEqual("", AreaHoverWindow.CritterWindow("N", null, new(AreaHoverWindow.NpcType, false, 3, 0, 50, true, null, false))!.SecondLine, "no facts of the NPC: no state is guessed");
        Assert.AreEqual(AreaHoverWindow.SneakSeenMessage, AreaHoverWindow.SneakStateMessage(0));
        Assert.AreEqual(AreaHoverWindow.SneakHiddenMessage, AreaHoverWindow.SneakStateMessage(4));
        Assert.AreEqual(0x27, AreaHoverWindow.SneakStateMessage(3));
    }

    [TestMethod]
    public void AHeroWhoDetectsAlignmentSeesTheAlignmentOverTenAndTheAlignmentImage()
    {
        string Line(int alignment) => AreaHoverWindow.CritterWindow("N", 375, Npc(alignment: alignment, detects: true))!.SecondLine;
        Assert.AreEqual("Alignment: 12", Line(125), "the stat over 10, truncated toward zero");
        Assert.AreEqual("Alignment: -12", Line(-125));
        Assert.AreEqual("Alignment: 0", Line(9));
        Assert.AreEqual(AreaHoverWindow.AlignmentImages[0], AreaHoverWindow.CritterWindow("N", 375, Npc(alignment: -1000, detects: true))!.Icon);
        Assert.AreEqual(375, AreaHoverWindow.CritterWindow("N", 375, Npc(detects: false))!.Icon, "without the spell flag the portrait or type image stays");
    }

    [TestMethod]
    public void TheAlignmentBandsAreSixthsOfTheStatRange()
    {
        Assert.AreEqual(0, AreaHoverWindow.AlignmentBand(-1000));
        Assert.AreEqual(0, AreaHoverWindow.AlignmentBand(-668), "333 values a band");
        Assert.AreEqual(1, AreaHoverWindow.AlignmentBand(-667));
        Assert.AreEqual(3, AreaHoverWindow.AlignmentBand(0));
        Assert.AreEqual(4, AreaHoverWindow.AlignmentBand(333));
        Assert.AreEqual(5, AreaHoverWindow.AlignmentBand(665));
        Assert.AreEqual(5, AreaHoverWindow.AlignmentBand(1000), "the top value would be a seventh band; the original caps it");
        Assert.AreEqual(0, AreaHoverWindow.AlignmentBand(-5000), "below the range the first band (the original reads before its table)");
        CollectionAssert.AreEqual(new[] { 634, 390, 389, 388, 387, 386 }, AreaHoverWindow.AlignmentImages.ToArray());
        foreach (var art in AreaHoverWindow.AlignmentImages) Assert.IsTrue(AreaHoverWindow.ImageNumbers.Contains(art), "the alignment arts are loaded with the window's images");
    }

    [TestMethod]
    public void WithoutReactionFactsTheLeftPartIsEmptyAndAnEmptyNameDrawsNothing()
    {
        var entry = AreaHoverWindow.CritterWindow("N", null, new(AreaHoverWindow.NpcType, false, 3, 0, null, false, 0, false))!;
        Assert.AreEqual("", entry.SecondLine);
        Assert.AreEqual("Level: 3", entry.SecondValue);
        Assert.IsNull(AreaHoverWindow.CritterWindow("", null, Npc()));
    }

    [TestMethod]
    public void TheSneakCheckCombinesSightHearingAndSleep()
    {
        Func<int> NoHearing = () => throw new AssertFailedException("hearing is read only when the sight check fails");
        Assert.AreEqual(0, AreaAiHost.HoverSneakExcess(0, NoHearing, asleep: false), "seen: 0 and no hearing check");
        Assert.AreEqual(1, AreaAiHost.HoverSneakExcess(0, NoHearing, asleep: true), "a sleeper that would see counts as 1");
        Assert.AreEqual(2, AreaAiHost.HoverSneakExcess(5, () => 2, asleep: false), "the smaller of hearing and sight");
        Assert.AreEqual(5, AreaAiHost.HoverSneakExcess(5, () => 9, asleep: false));
        Assert.AreEqual(1, AreaAiHost.HoverSneakExcess(1000, () => 0, asleep: true), "heard by a sleeper: still 1");
        Assert.AreEqual(3, AreaAiHost.HoverSneakExcess(1000, () => 3, asleep: true));
    }
}
