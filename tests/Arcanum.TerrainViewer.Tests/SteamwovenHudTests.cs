using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2452: the Steamwoven prototype's plan (strip scale and places from the layout model) (the generated board is retired, ARC-UI-3143)</summary>
[TestClass]
public sealed class SteamwovenHudTests
{
    [TestMethod]
    public void The_original_strip_heights_keep_the_strips_at_three_point_six_times_f_and_centred()
    {
        // 3840 x 2160 in the unfitted column (150 and 570): f = 1, strips 2880 px wide centred (480 each side), the 41-unit top strip (147.6 -> 148 px) stands on the 150 px band's lower edge,
        // the bottom strip hangs from the bottom band's upper edge; the insets are the middle band's.
        var plan = SteamwovenHud.Plan(3840, 2160, SteamwovenLayoutOptions.ColumnOriginal)!;
        Assert.AreEqual(3.6, plan.StripScale, 1e-9);
        Assert.AreEqual(480, plan.StripLeft);
        Assert.AreEqual(2, plan.TopStripY);
        Assert.AreEqual(2, plan.TopGap);
        Assert.AreEqual(1590, plan.BottomStripY);
        Assert.AreEqual(150, plan.TopInset);
        Assert.AreEqual(570, plan.BottomInset);
    }

    [TestMethod]
    public void The_fitted_column_the_game_uses_has_the_strips_exactly_41_and_159_units_high()
    {
        // 3840 x 2160: u = 3.6, the strips are round(41 u) = 148 and 159 u = 572 high (no sliver, no overhang).
        var plan = SteamwovenHud.Plan(3840, 2160)!;
        Assert.AreEqual(SteamwovenLayoutOptions.ColumnFitted, plan.Options);
        Assert.AreEqual(3.6, plan.StripScale, 1e-9);
        Assert.AreEqual(480, plan.StripLeft);
        Assert.AreEqual(148, plan.TopInset);
        Assert.AreEqual(572, plan.BottomInset);
        Assert.AreEqual(0, plan.TopGap);
        Assert.AreEqual(2160 - 572, plan.BottomStripY);
        Assert.IsTrue(plan.TopStripY >= plan.Layout.Top.Y);
    }

    [TestMethod]
    public void The_strip_scale_follows_f()
    {
        // 1920 x 1080 in the unfitted column: f = 0.5, scale 1.8, 800 units = 1440 px, the model's top 75 and bottom 285.
        var plan = SteamwovenHud.Plan(1920, 1080, SteamwovenLayoutOptions.ColumnOriginal)!;
        Assert.AreEqual(1.8, plan.StripScale, 1e-9);
        Assert.AreEqual(plan.Column.Column.X, plan.StripLeft);
        Assert.AreEqual(240, plan.StripLeft);
        Assert.AreEqual(75, plan.TopInset);
        Assert.AreEqual(285, plan.BottomInset);
        // The fitted column at 16:9 is the same scale.
        Assert.AreEqual(1.8, SteamwovenHud.Plan(1920, 1080)!.StripScale, 1e-9);
    }

    [TestMethod]
    public void A_letterboxed_window_puts_the_bars_in_the_insets()
    {
        // 1024 x 768 in the unfitted column: a 1024 x 640 frame, 64 px bars at the top and bottom; the middle band lies between them.
        var plan = SteamwovenHud.Plan(1024, 768, SteamwovenLayoutOptions.ColumnOriginal)!;
        Assert.AreEqual(64, plan.Layout.Bars.Top);
        Assert.AreEqual(64 + 40, plan.TopInset);
        Assert.AreEqual(64 + 152, plan.BottomInset);
        Assert.AreEqual(plan.Layout.Middle.Height, 768 - plan.TopInset - plan.BottomInset);
    }

    [TestMethod]
    public void No_plan_below_the_supported_scale_or_for_options_that_are_invalid()
    {
        Assert.IsNull(SteamwovenHud.Plan(800, 450, SteamwovenLayoutOptions.Default)); // f = 0.208
        Assert.IsNull(SteamwovenHud.Plan(1920, 1080, new SteamwovenLayoutOptions(new SteamwovenColumnMode(new Ratio(1, 2)))));
    }
}
