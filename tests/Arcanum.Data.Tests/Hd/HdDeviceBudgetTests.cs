using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

[TestClass]
public sealed class HdDeviceBudgetTests
{
    private const long MiB = 1024 * 1024;

    [TestMethod]
    public void Ceilings_are_the_contract_values_at_2x_and_4x()
    {
        Assert.AreEqual(new HdDeviceBudget(327_412_384, 512, 128 * MiB, 256 * MiB, 768 * MiB), HdDeviceBudget.Ceiling(2));
        Assert.AreEqual(new HdDeviceBudget(1_309_649_536, 512, 512 * MiB, 1024 * MiB, 3072 * MiB), HdDeviceBudget.Ceiling(4));
        // 4x terrain chunk: 2048² × 4 B = 16 MiB, so the ceiling keeps the Legacy 192 chunks.
        Assert.AreEqual(192, HdDeviceBudget.Ceiling(4).TerrainBytes / (2048L * 2048 * 4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HdDeviceBudget.Ceiling(1));
    }

    [TestMethod]
    public void A_device_file_may_lower_limits_but_never_raise_them()
    {
        var ceiling = HdDeviceBudget.Ceiling(4);
        Assert.AreEqual(ceiling, HdDeviceBudgetCodec.DeserializeOrCeiling(null, 4));
        Assert.AreEqual(ceiling, HdDeviceBudgetCodec.DeserializeOrCeiling("", 4));
        Assert.AreEqual(ceiling, HdDeviceBudgetCodec.DeserializeOrCeiling("{\"schema\":1}", 4));
        foreach (var bad in new[] { "{", "[]", "{\"schema\":2}", "{\"schema\":1,\"worldBytes\":-1}", "{\"schema\":1,\"worldBytes\":1.5}",
                     "{\"schema\":1,\"unknown\":1}", "{\"schema\":1,\"worldBytes\":1,\"worldBytes\":2}", "{\"worldBytes\":1}" })
            Assert.AreEqual(ceiling, HdDeviceBudgetCodec.DeserializeOrCeiling(bad, 4), bad);

        // A 16 GB shared-memory handheld keeping 48 terrain chunks and no WORLD HD.
        var handheld = HdDeviceBudgetCodec.DeserializeOrCeiling("{\"schema\":1,\"terrainBytes\":805306368,\"worldBytes\":0,\"areaVisibleBytes\":9999999999999}", 4);
        Assert.AreEqual(ceiling with { TerrainBytes = 768 * MiB, WorldBytes = 0 }, handheld);
        Assert.AreEqual(handheld, HdDeviceBudgetCodec.DeserializeOrCeiling(HdDeviceBudgetCodec.Serialize(handheld, 4), 4));
        Assert.AreEqual(HdDeviceBudget.Ceiling(2), HdDeviceBudgetCodec.DeserializeOrCeiling(HdDeviceBudgetCodec.Serialize(ceiling, 2), 2), "clamped to the scale's ceiling");
        Assert.AreEqual(new HdDeviceBudget(0, 0, 0, 0, 0), new HdDeviceBudget(-5, -1, -1, -1, -1).Clamped(2));
    }
}
