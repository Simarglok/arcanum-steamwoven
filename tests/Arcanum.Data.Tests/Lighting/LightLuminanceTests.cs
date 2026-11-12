using Arcanum.Data.Lighting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Lighting;

// ARC-WORLD-1149 (FR-LIGHT-011, ledger LL1): the grey level a light-aware check reads. Hand-computed from the weights 76.5, 150.45 and 28.05
// (table entry i = trunc(i / 255 × weight)); invented colours, no original data.
[TestClass]
public sealed class LightLuminanceTests
{
    [TestMethod]
    public void Black_reads_zero_and_white_reads_254_which_the_darkness_term_counts_as_full_light()
    {
        Assert.AreEqual(0, LightLuminance.Gray(new(0, 0, 0)));
        Assert.AreEqual(LightLuminance.White, LightLuminance.Gray(AmbientColor.Neutral));
        var white = LightLuminance.Gray(AmbientColor.Neutral);
        Assert.AreEqual(76 + 150 + 28, white, "each table's last entry is truncated");
        Assert.AreEqual(0, (255 - white) * 30 / 255, "the penalty of 254 is the penalty of 255");
    }

    [TestMethod]
    public void The_channels_weigh_in_the_order_green_red_blue()
    {
        Assert.AreEqual(76, LightLuminance.Gray(new(255, 0, 0)));
        Assert.AreEqual(150, LightLuminance.Gray(new(0, 255, 0)));
        Assert.AreEqual(28, LightLuminance.Gray(new(0, 0, 255)));
    }

    [TestMethod]
    public void A_channel_value_takes_its_tables_truncated_entry_and_the_entries_add()
    {
        // 100/255 × 76.5 = 30 and × 28.05 = 11 reach their integers; × 150.45 stays just below 59 (the weight is stored a little under) and truncates to 58.
        Assert.AreEqual(30, LightLuminance.Gray(new(100, 0, 0)));
        Assert.AreEqual(58, LightLuminance.Gray(new(0, 100, 0)));
        Assert.AreEqual(11, LightLuminance.Gray(new(0, 0, 100)));
        Assert.AreEqual(30 + 58 + 11, LightLuminance.Gray(new(100, 100, 100)));
        // 128: 38.4 -> 38, 75.5 -> 75, 14.08 -> 14.
        Assert.AreEqual(38 + 75 + 14, LightLuminance.Gray(new(128, 128, 128)));
        // A single step of one channel never lowers the level.
        for (var value = 1; value <= 255; value++)
        {
            Assert.IsTrue(LightLuminance.Gray(new((byte)value, 17, 3)) >= LightLuminance.Gray(new((byte)(value - 1), 17, 3)));
            Assert.IsTrue(LightLuminance.Gray(new(9, (byte)value, 3)) >= LightLuminance.Gray(new(9, (byte)(value - 1), 3)));
            Assert.IsTrue(LightLuminance.Gray(new(9, 17, (byte)value)) >= LightLuminance.Gray(new(9, 17, (byte)(value - 1))));
        }
    }

    [TestMethod]
    public void Every_colour_reads_within_zero_and_254()
    {
        var max = 0;
        for (var r = 0; r <= 255; r += 5)
            for (var g = 0; g <= 255; g += 5)
                for (var b = 0; b <= 255; b += 5)
                    max = Math.Max(max, LightLuminance.Gray(new((byte)r, (byte)g, (byte)b)));
        Assert.IsTrue(max <= 254);
        Assert.AreEqual(254, LightLuminance.Gray(new(255, 255, 255)));
    }
}
