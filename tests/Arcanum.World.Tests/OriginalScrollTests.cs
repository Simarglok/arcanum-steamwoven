using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-UI-099 and ARC-UI-100 (MIG16 tail, SA44): the direction codes, the step table and the rate gate of the scroll function
/// 0x40E0A0 and the exact-border-pixel edge test of 0x401F50, against the executable reading. Pure logic, no original data.</summary>
[TestClass]
public sealed class OriginalScrollTests
{
    [TestMethod]
    public void The_original_800_by_600_border_pixels_give_the_executables_codes()
    {
        // 0x401F50: column 0 then row 0 / 599 / other = 7 / 5 / 6; column 799 then 1 / 3 / 2; any other column then row 0 / 599 = 0 / 4.
        Assert.AreEqual(7, OriginalScroll.EdgeCode(0, 0, 800, 600));
        Assert.AreEqual(5, OriginalScroll.EdgeCode(0, 599, 800, 600));
        Assert.AreEqual(6, OriginalScroll.EdgeCode(0, 300, 800, 600));
        Assert.AreEqual(1, OriginalScroll.EdgeCode(799, 0, 800, 600));
        Assert.AreEqual(3, OriginalScroll.EdgeCode(799, 599, 800, 600));
        Assert.AreEqual(2, OriginalScroll.EdgeCode(799, 300, 800, 600));
        Assert.AreEqual(0, OriginalScroll.EdgeCode(400, 0, 800, 600));
        Assert.AreEqual(4, OriginalScroll.EdgeCode(400, 599, 800, 600));
    }

    [TestMethod]
    public void Only_the_exact_border_pixel_scrolls()
    {
        // No margin: the pixel next to the border is inside the screen.
        Assert.AreEqual(OriginalScroll.None, OriginalScroll.EdgeCode(1, 300, 800, 600));
        Assert.AreEqual(OriginalScroll.None, OriginalScroll.EdgeCode(798, 300, 800, 600));
        Assert.AreEqual(OriginalScroll.None, OriginalScroll.EdgeCode(400, 1, 800, 600));
        Assert.AreEqual(OriginalScroll.None, OriginalScroll.EdgeCode(400, 598, 800, 600));
        Assert.AreEqual(6, OriginalScroll.EdgeCode(0, 1, 800, 600));
        Assert.AreEqual(0, OriginalScroll.EdgeCode(1, 0, 800, 600));
    }

    [TestMethod]
    public void The_rule_holds_for_any_screen_size_and_outside_positions_are_not_border_pixels()
    {
        Assert.AreEqual(3, OriginalScroll.EdgeCode(1439, 899, 1440, 900));
        Assert.AreEqual(2, OriginalScroll.EdgeCode(1439, 450, 1440, 900));
        Assert.AreEqual(OriginalScroll.None, OriginalScroll.EdgeCode(1440, 450, 1440, 900));
        Assert.AreEqual(OriginalScroll.None, OriginalScroll.EdgeCode(-1, 450, 1440, 900));
        Assert.AreEqual(OriginalScroll.None, OriginalScroll.EdgeCode(450, 900, 1440, 900));
        Assert.AreEqual(OriginalScroll.None, OriginalScroll.EdgeCode(0, 0, 0, 0));
        Assert.AreEqual(OriginalScroll.None, OriginalScroll.EdgeCode(0, 0, 1, 1));
    }

    [TestMethod]
    public void A_code_is_a_unit_step_on_screen_and_arrows_give_the_same_codes()
    {
        (int X, int Y)[] expected = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];
        for (var code = 0; code < 8; code++)
        {
            Assert.AreEqual(expected[code], OriginalScroll.Direction(code));
            Assert.AreEqual(code, OriginalScroll.CodeOf(expected[code].X, expected[code].Y));
        }
        Assert.AreEqual((0, 0), OriginalScroll.Direction(OriginalScroll.None));
        Assert.AreEqual((0, 0), OriginalScroll.Direction(8));
        Assert.AreEqual(OriginalScroll.None, OriginalScroll.CodeOf(0, 0));
        Assert.AreEqual((-1, 1), OriginalScroll.EdgeDirection(0, 599, 800, 600));
        Assert.AreEqual((0, 0), OriginalScroll.EdgeDirection(10, 10, 800, 600));
    }

    [TestMethod]
    public void The_speed_table_is_the_executables_and_the_game_runs_at_level_1()
    {
        // 0x40E940: levels 0 to 3 give 8/4, 20/10, 28/14 and 56/28 pixels across/up; 0x40DFCF sets level 1 and nothing changes it.
        int[] across = [8, 20, 28, 56], up = [4, 10, 14, 28];
        for (var level = 0; level < 4; level++) { Assert.AreEqual(across[level], OriginalScroll.HorizontalStep(level)); Assert.AreEqual(up[level], OriginalScroll.VerticalStep(level)); }
        Assert.AreEqual(20, OriginalScroll.HorizontalStep());
        Assert.AreEqual(10, OriginalScroll.VerticalStep());
        Assert.AreEqual(20, OriginalScroll.HorizontalStep(9), "an unknown level falls back to the default");
    }

    [TestMethod]
    public void A_step_is_constant_and_a_diagonal_adds_four_and_two()
    {
        // Jump table 0x40E58C: straight (X, 0) or (0, Y); diagonal (X + 4, Y + 2), at level 1 (24, 12).
        Assert.AreEqual((0, -10), OriginalScroll.Step(0));
        Assert.AreEqual((24, -12), OriginalScroll.Step(1));
        Assert.AreEqual((20, 0), OriginalScroll.Step(2));
        Assert.AreEqual((24, 12), OriginalScroll.Step(3));
        Assert.AreEqual((0, 10), OriginalScroll.Step(4));
        Assert.AreEqual((-24, 12), OriginalScroll.Step(5));
        Assert.AreEqual((-20, 0), OriginalScroll.Step(6));
        Assert.AreEqual((-24, -12), OriginalScroll.Step(7));
        Assert.AreEqual((0, 0), OriginalScroll.Step(OriginalScroll.None));
        Assert.AreEqual((12, -6), OriginalScroll.Step(1, 0), "level 0 diagonal");
    }

    [TestMethod]
    public void The_clock_lets_one_step_through_per_16_ms_and_restarts_from_the_step()
    {
        var clock = new OriginalScrollClock();
        Assert.IsTrue(clock.TryStep(), "the first call after start passes");
        Assert.IsFalse(clock.TryStep(), "a second call in the same pass does not");
        clock.Advance(.015); Assert.IsFalse(clock.TryStep());
        clock.Advance(.002); Assert.IsTrue(clock.TryStep(), "17 ms after the step");
        clock.Advance(.5); Assert.IsTrue(clock.TryStep(), "a slow frame still gives one step, not 30");
        Assert.IsFalse(clock.TryStep());
        clock.Advance(double.NaN); clock.Advance(-1); Assert.IsFalse(clock.TryStep(), "a bad delta adds nothing");
        clock.Reset(); Assert.IsTrue(clock.TryStep());
    }
}
