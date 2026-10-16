using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

[TestClass]
public sealed class AreaCaveOriginalTests
{
    [TestMethod]
    public void Cave_plan_is_a_separate_pinned_profile_with_both_wall_tables()
    {
        var plan = CaveProfile.Plan();
        Assert.AreEqual(5078, plan.CatalogueMap); Assert.AreEqual(79, plan.RuntimeMap);
        Assert.AreEqual(new AreaSectorCoordinate(32, 31), plan.Start); Assert.AreEqual(141, plan.Townmap);
        Assert.AreEqual("arc-world-009-cave-file-effective-source-v1", plan.Profile);
        Assert.AreEqual(4 + CaveProfile.Prototypes.Count, plan.Identities.Count);
        // Exactly the ten roles of the pinned profile; the portal names (ARC-DATA-020) are bound only by whole-map plans.
        CollectionAssert.AreEquivalent(new[] { AreaMappingRole.Tile, AreaMappingRole.Facade, AreaMappingRole.Scenery, AreaMappingRole.Container, AreaMappingRole.ItemGround,
            AreaMappingRole.MonsterNames, AreaMappingRole.UniqueNpcNames, AreaMappingRole.MissingPolicy, AreaMappingRole.WallStructure, AreaMappingRole.WallName },
            plan.Mappings.Select(m => m.Role).ToArray());
        Assert.AreEqual(CaveProfile.NormalGameMobileSha256, plan.Identities.Single(i => i.Key.Role == AreaSourceRole.NormalGameMobile).Sha256);
        // The exterior keeps its own profile, mappings and identity.
        var exterior = OriginalProfile.Plan();
        Assert.AreEqual("arc-data-009-complete-file-effective-source-v1", exterior.Profile);
        Assert.AreEqual(8, exterior.Mappings.Count);
        Assert.AreNotEqual(CaveProfile.NormalGameMobileSha256, exterior.Identities.Single(i => i.Key.Role == AreaSourceRole.NormalGameMobile).Sha256);
    }

    [TestMethod, TestCategory("RealData")]
    public void Cave_owner_publishes_the_pinned_cave_profile_and_repeats()
    {
        var cave = Environment.GetEnvironmentVariable("ARC_CAVE_MOBILE");
        if (string.IsNullOrEmpty(cave)) Assert.Inconclusive("Opt-in original inputs and ARC_CAVE_MOBILE are required.");
        using var owner = AreaOriginalValidator.OpenCaveFiles(Required("ARC_AREA_MODULE"), Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"), cave, Required("ARC_AREA_PROTOTYPES"));
        var report = owner.Inspect();
        Assert.IsTrue(report.IsComplete, JsonSerializer.Serialize(report.Issues));
        var snapshot = report.Snapshot!;
        Assert.AreEqual("arc-world-009-cave-file-effective-source-v1", snapshot.Profile);
        Assert.AreEqual(5078, snapshot.CatalogueMap); Assert.AreEqual(79, snapshot.RuntimeMap);
        Assert.AreEqual(CaveProfile.ExpectedCounts, snapshot.Reconciliation);

        var endpoint = snapshot.Endpoints.Single();
        Assert.AreEqual((2080374816L, 99, 1, 1532, 5000, 1, 92913L, 82598L),
            (endpoint.Sector, endpoint.Ordinal, endpoint.Sap, endpoint.Script, endpoint.CatalogueDestination, endpoint.RuntimeDestination, endpoint.ArrivalX, endpoint.ArrivalY));

        // Walls: 125 static class-1 visuals, single group and frame, rotation kept as facing, flip bit as mirror.
        var walls = snapshot.Visuals.Where(v => v.Key.Kind == AreaSourceKind.Static && v.RawArt >> 28 == 1).ToArray();
        Assert.AreEqual(125, walls.Length);
        Assert.IsTrue(walls.All(v => v.PhysicalGroup == 0 && v.Frame == 0 && v.Policy == "strict-full-entry" &&
            v.NominalFacing == (int)((v.RawArt >> 11) & 7) && v.Mirrored == ((v.RawArt & 1) != 0)));
        CollectionAssert.AreEquivalent(new[] { 1, 3, 5, 7 }, walls.Select(v => v.NominalFacing).Distinct().ToArray());
        // The two exception kinds are each pinned to exact objects.
        Assert.AreEqual(4, snapshot.Visuals.Count(v => v.Policy == "arc-world-009-exact-art-view-v1"));
        Assert.AreEqual(1, snapshot.Visuals.Count(v => v.Policy == "arc-world-009-exact-single-group-alias-v1"));
        Assert.AreEqual(snapshot.Visuals.Count - 5, snapshot.Visuals.Count(v => v.Policy == "strict-full-entry"));

        var firstJson = JsonSerializer.Serialize(snapshot);
        var second = owner.LoadStrict();
        Assert.AreEqual(firstJson, JsonSerializer.Serialize(second), "Repeated cave snapshots must be identical.");
        var wall = second.Visuals.First(v => v.Key.Kind == AreaSourceKind.Static && v.RawArt >> 28 == 1 && v.Mirrored);
        using (var lease = owner.AcquireSelectedFrame(second, wall.Key))
            Assert.AreEqual(wall.RgbaSha256, Convert.ToHexString(SHA256.HashData(lease.CopyRgba())).ToLowerInvariant());
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new AssertInconclusiveException(name + " is required.");
}
