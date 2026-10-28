using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// ARC-COMBAT-017: the thrown range (0x4622A0, ledger RH4). Strengths and weights are invented.
[TestClass]
public sealed class ThrowRulesTests
{
    [TestMethod]
    public void TheRangeIsFiftyTimesStrengthOverATenthOfTheWeightOverFive()
    {
        Assert.AreEqual(80, ThrowRules.Range(8, SkillTraining.None, 15), "400 / 1 / 5");
        Assert.AreEqual(80, ThrowRules.Range(8, SkillTraining.Apprentice, 15));
        Assert.AreEqual(120, ThrowRules.Range(8, SkillTraining.Expert, 15), "expert adds half: 600 / 1 / 5");
        Assert.AreEqual(120, ThrowRules.Range(8, SkillTraining.Master, 15));
        Assert.AreEqual(80, ThrowRules.Range(8, SkillTraining.None, 0), "a tenth of the weight is at least 1");
        Assert.AreEqual(5, ThrowRules.Range(8, SkillTraining.None, 150), "400 / 15 = 26, / 5 = 5: divisions toward zero");
        Assert.AreEqual(9, ThrowRules.Range(9, SkillTraining.Expert, 150), "450 + 225 = 675, / 15 = 45, / 5 = 9");
    }

    [TestMethod]
    public void TheRangeIsAtLeastOne()
    {
        Assert.AreEqual(1, ThrowRules.Range(8, SkillTraining.None, ThrowRules.MaxWeight));
        Assert.AreEqual(1, ThrowRules.Range(-3, SkillTraining.None, 15), "a negative arm counts as 1");
        Assert.AreEqual(1, ThrowRules.Range(0, SkillTraining.Expert, 15));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Range(8, SkillTraining.None, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Range(8, SkillTraining.None, ThrowRules.MaxWeight + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Range(8, (SkillTraining)9, 15));
    }

    // ARC-COMBAT-018 (0x4B39B0, ledger RH2, PS rows): (20 − level) / 5 + 1 tiles toward zero, at most the distance.
    [TestMethod]
    public void AMissedThrowScattersFewerTilesTheBetterTheThrower()
    {
        Assert.AreEqual(5, ThrowRules.ScatterTiles(0, 10));
        Assert.AreEqual(4, ThrowRules.ScatterTiles(4, 10), "16 / 5 = 3");
        Assert.AreEqual(4, ThrowRules.ScatterTiles(5, 10));
        Assert.AreEqual(1, ThrowRules.ScatterTiles(16, 10));
        Assert.AreEqual(1, ThrowRules.ScatterTiles(20, 10));
        Assert.AreEqual(2, ThrowRules.ScatterTiles(0, 2), "capped at the distance");
        Assert.AreEqual(0, ThrowRules.ScatterTiles(0, 0));
        Assert.AreEqual(1, ThrowRules.ScatterTiles(24, 10), "-4 / 5 = 0 toward zero");
        Assert.AreEqual(0, ThrowRules.ScatterTiles(25, 10));
        Assert.AreEqual(-1, ThrowRules.ScatterTiles(30, 10));
    }

    // ARC-COMBAT-018 (0x4B90D0): the walk takes its steps in one rotation and stops on the last tile a step off the map left it on.
    [TestMethod]
    public void TheScatterWalksItsTilesInOneRotationAndStopsAtTheMapsEdge()
    {
        static CellCoord? Open(CellCoord cell, int rotation) => Arcanum.Simulation.Navigation.OriginalPathRequest.Neighbor(cell, rotation);
        static CellCoord? Edge(CellCoord cell, int rotation) => Open(cell, rotation) is { X: >= 9 } next ? next : null;
        var from = new CellCoord(10, 10);
        Assert.AreEqual(new CellCoord(13, 10), ThrowRules.Scatter(from, 5, 3, Open));
        Assert.AreEqual(new CellCoord(7, 7), ThrowRules.Scatter(from, 0, 3, Open));
        Assert.AreEqual(new CellCoord(10, 13), ThrowRules.Scatter(from, 3, 3, Open));
        Assert.AreEqual(new CellCoord(9, 9), ThrowRules.Scatter(from, 0, 3, Edge), "the step off the map stops the walk");
        Assert.AreEqual(from, ThrowRules.Scatter(from, 4, 0, Open));
        Assert.AreEqual(from, ThrowRules.Scatter(from, 4, -1, Open), "no tiles: no walk");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ThrowRules.Scatter(from, 8, 1, Open));
        Assert.ThrowsExactly<ArgumentNullException>(() => ThrowRules.Scatter(from, 1, 1, null!));
    }
}
