using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RenderDisposition = Arcanum.TerrainViewer.Core.AreaDisposition;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaScenePlanTests
{
    private static readonly string Digest = new('a', 64);
    private static AreaVisualKey TerrainKey(long sector, int index) => new(AreaSourceKind.Terrain, new(Digest, 5051, sector), index, "");
    private static AreaObjectRecord Body(int index, long x = 92928, long y = 82560, uint flags = 0, int scale = 100, long scenery = 0)
    {
        var key = new AreaObjectKey(Digest, 5051, AreaSourceKind.Static, 86570436012, index, index * 100, Digest, 100);
        var fields = new[] { Field(18, flags), Field(8, 0), Field(9, 0xFFFFFF), Field(10, 255), Field(11, scale), Field(36, scenery) };
        return new(key, 119, 3, 1, Digest, Digest, Digest, Digest, AreaScope.Core, null, x, y,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, fields, fields, true, true, true);
    }
    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    private static AreaVisualRecord Visual(AreaVisualKey key, int width = 80, int height = 40, int hotX = 0, int hotY = 0, int ox = 0, int oy = 0, int scale = 100)
        => new(key, key.Kind == AreaSourceKind.Terrain ? 0u : 0x40000000u, 0,
            new(new(AreaSourceRole.Resource2), 1, 0, 100, 100, 0, Digest), Digest, 100, 0, 0, 0, 0, 0, 0,
            false, width, height, hotX, hotY, 777, -999, ox, oy, scale, 1, width * height * 4, Digest, "synthetic");
    private static AreaVisualRecord BodyVisual(AreaObjectRecord body, int width = 120, int height = 240, int hotX = 60, int hotY = 220, int ox = 0, int oy = 0, int scale = 100)
        => Visual(new(body.Key.Kind, new(Digest, 5051, body.Key.Sector!.Value), body.Key.SourceIndex, Digest, Object: body.Key), width, height, hotX, hotY, ox, oy, scale);

    [TestMethod]
    public void AdjacentSeamsRetainOccurrenceKeysAndGlobalChart()
    {
        var a = TerrainKey(100, 63); var b = TerrainKey(101, 0); var c = TerrainKey(102, 0);
        var records = new[] { new AreaTerrainRecord(a, 63, 0, 92991, 82560, 0, 0, AreaScope.Core, a),
            new AreaTerrainRecord(b, 0, 0, 92992, 82560, 0, 0, AreaScope.Core, b),
            new AreaTerrainRecord(c, 0, 0, 92992, 82624, 0, 0, AreaScope.Core, b) };
        var plan = AreaScenePlan.Create(records, [], [Visual(a), Visual(b)]);
        Assert.AreEqual(new TerrainRect(-2519,1260,78,40), plan.Commands.Single(command => command.Visual == a).Destination);
        Assert.AreEqual(new TerrainRect(-2559,1280,78,40), plan.Commands.Single(command => command.Visual == b).Destination);
        Assert.AreEqual(new TerrainRect(1,2560,78,40), plan.Commands.Single(command => command.Visual == c).Destination);
        Assert.AreEqual(3, plan.Commands.Select(command => command.Visual).Distinct().Count());
        Assert.AreEqual(1, plan.Commands.Select(command => command.Frame).Distinct().Count());
    }
    [TestMethod]
    public void EachBodyFindsItsOwnVisualAndTwoBodyVisualsForOneObjectAreRefused()
    {
        // ARC-RENDER-019: bodies find their visual by object key (a lookup, not a scan per body).
        var bodies = Enumerable.Range(0, 200).Select(index => Body(index, 92928 + index, 82560)).ToArray();
        var plan = AreaScenePlan.Create([], bodies, bodies.Select((body, index) => BodyVisual(body, 10 + index, 20)).Reverse().ToArray());
        Assert.AreEqual(200, plan.Commands.Count);
        foreach (var command in plan.Commands) Assert.AreEqual(10 + command.Identity!.Value.SourceIndex, command.Destination.Width);
        var twice = BodyVisual(bodies[0]) with { Key = BodyVisual(bodies[0]).Key with { SourceIndex = 9999 } };
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaScenePlan.Create([], [bodies[0]], [BodyVisual(bodies[0]), twice]));
        // A body without a visual is still refused.
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaScenePlan.Create([], [bodies[0]], []));
    }
    [TestMethod]
    public void BodyCenterSignedHotspotsAndSerializedScaleDoNotUseArtOffsets()
    {
        var body = Body(1, scale: 75); var visual = BodyVisual(body, 167,321,-12,-40,scale:75);
        var command = AreaScenePlan.Create([], [body], [visual]).Commands.Single();
        Assert.AreEqual(new TerrainRect(49,50,125,240), command.Destination);
        Assert.AreEqual(92928, command.MapX); Assert.AreEqual(82560, command.MapY);
        var shrunk = Body(1, flags: 0x80, scale:75);
        Assert.AreEqual(new TerrainRect(44,35,62,120), AreaScenePlan.Create([], [shrunk], [visual]).Commands.Single().Destination);
    }
    [TestMethod]
    public void GlobalMapOrderDiffersFromProjectedAnchorYAndIsIndependentOfCanopySize()
    {
        var a = Body(0, 92940,82560); var b = Body(1,92928,82561);
        var plan = AreaScenePlan.Create([], [b,a], [BodyVisual(a,1000,1000), BodyVisual(b,10,10)]);
        CollectionAssert.AreEqual(new[] { a.Key,b.Key }, plan.Commands.Select(command => command.Identity!.Value).ToArray());
        var second = AreaScenePlan.Create([], [a,b], [BodyVisual(a,10,10),BodyVisual(b,1000,1000)]);
        CollectionAssert.AreEqual(plan.Commands.Select(command => command.Identity!.Value).ToArray(),second.Commands.Select(command => command.Identity!.Value).ToArray());
    }
    [TestMethod]
    public void LocalSignedRanksAndFlatUnderAllHaveLiteralOrdering()
    {
        Assert.AreEqual((27,-23), AreaScenePlan.SameCellRanks(-11,3));
        Assert.AreEqual((20,-20), AreaScenePlan.SameCellRanks(0,0));
        Assert.AreEqual((-1,1), AreaScenePlan.SameCellRanks(43,1));
        var upright = Body(0); var flat = Body(1, flags:4); var under = Body(2, flags:4,scenery:512);
        var plan = AreaScenePlan.Create([], [upright,flat,under], [BodyVisual(upright),BodyVisual(flat),BodyVisual(under)]);
        CollectionAssert.AreEqual(new[] { under.Key,flat.Key,upright.Key }, plan.Commands.Select(command => command.Identity!.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 2,3,4 }, plan.Commands.Select(command => command.Phase).ToArray());
    }
    [TestMethod]
    public void InventoryAndHiddenBodiesAreKeyedReasonsNotDroppedResources()
    {
        var a = Body(0,flags:0x1000); var b = Body(1,flags:0x100); var c = Body(2,flags:0x200); var d = Body(3);
        var plan = AreaScenePlan.Create([], [a,b,c,d], [BodyVisual(a),BodyVisual(b),BodyVisual(c),BodyVisual(d)]);
        Assert.AreEqual(1,plan.Commands.Count); Assert.AreEqual(4,plan.Coverage.Count);
        Assert.AreEqual("ExteriorSectorInsertionRefusedByInventory", plan.Coverage.Single(row=>row.Identity==a.Key).Reason);
        Assert.AreEqual("BodySuppressedBySerializedState", plan.Coverage.Single(row=>row.Identity==b.Key).Reason);
        Assert.AreEqual("InvisibleNoDetectionObserver", plan.Coverage.Single(row=>row.Identity==c.Key).Reason);
        Assert.AreEqual(RenderDisposition.UprightBody,plan.Coverage.Single(row=>row.Identity==d.Key).Disposition);
    }
    [TestMethod]
    public void MissingRequiredFieldAndUnsupportedBlendAreFrontiers()
    {
        var body = Body(0);
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaScenePlan.Create([], [body with { EffectiveFields = body.EffectiveFields.Where(field=>field.Slot!=18).ToArray() }], [BodyVisual(body)]));
        Assert.ThrowsExactly<InvalidOperationException>(() => AreaScenePlan.Create([], [body with { EffectiveFields = body.EffectiveFields.Select(field=>field.Slot==8?Field(8,0x20):field).ToArray() }], [BodyVisual(body)]));
    }
    private static AreaObjectRecord With(AreaObjectRecord body, params (int Slot, long Value)[] values)
    {
        var fields = body.EffectiveFields.Select(field => values.Any(v => v.Slot == field.Slot) ? Field(field.Slot, values.Single(v => v.Slot == field.Slot).Value) : field).ToArray();
        return body with { DirectFields = fields, EffectiveFields = fields };
    }
    private static AreaDrawCommand Drawn(AreaObjectRecord body, int height = 240)
        => AreaScenePlan.Create([], [body], [BodyVisual(body, height: height)]).Commands.Single();
    [TestMethod]
    public void BlendStatesFollowTheOriginalPrecedenceAndStayUnlitExceptAdditive()
    {
        var body = Body(0);
        var multiply = Drawn(With(body, (8, 0x40)));
        Assert.IsTrue(multiply.Multiply && multiply.Unlit && !multiply.Additive && multiply.Alpha == 255 && multiply.Blended);
        // Additive wins over multiply and constant alpha; it keeps the existing (lit) rule.
        var additive = Drawn(With(body, (8, 0x150)));
        Assert.IsTrue(additive.Additive && !additive.Multiply && !additive.Unlit && additive.Alpha == 255);
        var constant = Drawn(With(body, (8, 0x101), (10, 95)));
        Assert.IsTrue(constant.Alpha == 95 && constant.Unlit && !constant.Multiply && !constant.Additive && constant.FlipX && constant.Blended);
        var mulOverAlpha = Drawn(With(body, (8, 0x140)));
        Assert.IsTrue(mulOverAlpha.Multiply && mulOverAlpha.Alpha == 255);
        var opaque = Drawn(body);
        Assert.IsTrue(!opaque.Multiply && !opaque.Unlit && opaque.WadeRows == 0 && !opaque.Blended);
        var plan = AreaScenePlan.Create([], [With(body, (8, 0x40))], [BodyVisual(body)]);
        Assert.AreEqual(AreaAmbientChannel.Unlit, AreaAmbientPolicy.Channels(plan, [], [BodyVisual(body)])[multiply.Visual]);
        Assert.AreEqual(AreaAmbientChannel.Outdoor, AreaAmbientPolicy.Channels(AreaScenePlan.Create([], [body], [BodyVisual(body)]), [], [BodyVisual(body)])[opaque.Visual]);
        // Subtract, other alpha modes, an alpha the drawn branch never reads and an out-of-range alpha stay refused.
        foreach (var (blit, alpha) in new[] { (0x20L, 255L), (0x200L, 255L), (0x400L, 255L), (0x40L, 95L), (0x150L, 95L), (0x140L, 95L), (0L, 95L), (0x100L, 256L), (0x100L, -1L) })
            Assert.ThrowsExactly<InvalidOperationException>(() => Drawn(With(body, (8, blit), (10, alpha))), $"blit 0x{blit:X} alpha {alpha}");
        // A frozen body takes the original's frozen tint before any blit blend; it is not drawn as an ordinary body.
        Assert.ThrowsExactly<InvalidOperationException>(() => Drawn(Body(0, flags: 0x10000000)));
    }
    [TestMethod]
    public void WadingBodiesDrawTheirLowerFifteenDestinationRowsOrAFlatBodyWhole()
    {
        Assert.AreEqual(AreaScenePlan.WadingRows, Drawn(Body(0, flags: 0x20000)).WadeRows);
        // Destination rows, after scale: a half-size body still wades in its lowest 15 drawn rows.
        var scaled = AreaScenePlan.Create([], [Body(0, flags: 0x20000, scale: 50)], [BodyVisual(Body(0), height: 240)]).Commands.Single();
        Assert.AreEqual((120, 15), (scaled.Destination.Height, scaled.WadeRows));
        Assert.AreEqual(240, Drawn(Body(0, flags: 0x20004)).WadeRows);
        Assert.AreEqual(10, Drawn(Body(0, flags: 0x20000), height: 10).WadeRows);
        Assert.AreEqual(0, Drawn(Body(0, flags: 0x60000)).WadeRows, "water walking");
        Assert.AreEqual(0, Drawn(With(Body(0, flags: 0x20000), (8, 0x10))).WadeRows, "additive keeps its blend");
        Assert.AreEqual(0, Drawn(With(Body(0, flags: 0x20000), (8, 0x40))).WadeRows, "multiply keeps its blend");
        var ghost = Drawn(With(Body(0, flags: 0x20000), (8, 0x100), (10, 95)));
        Assert.IsTrue(ghost.WadeRows == 15 && ghost.Alpha == 95 && ghost.Blended);
    }
    [TestMethod]
    public void AreaModeRequiresAllRolesAndRejectsCallerActorOverrides()
    {
        var args = new[] { "--mode","crash-site-area","--module","module","--resource","resource2","--actor-resource","resource1","--prototypes","directory","--area-mobile","mobile","--area-effects","resource3","--area-patch","patch","--source-id","synthetic" };
        var parsed = TerrainViewerArguments.Parse(args);
        Assert.AreEqual(TerrainViewerMode.CrashSiteArea,parsed.Mode); Assert.AreEqual("mobile",parsed.AreaMobilePath);
        Assert.AreEqual(ActorDiagnosticAction.Idle,parsed.ActorInput!.Action);
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Concat(new[]{"--actor-facing","5"}).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Where((_,i)=>i!=10&&i!=11).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Concat(new[]{"--area-mobile","second"}).ToArray()));
        Assert.AreEqual(("resource3","patch"),(parsed.AreaEffectsPath,parsed.AreaPatchPath));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Where((_,i)=>i!=12&&i!=13).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Where((_,i)=>i!=14&&i!=15).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(args.Concat(new[]{"--area-patch","second"}).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(()=>TerrainViewerArguments.Parse(["--mode","world-diagnostic","--module","m","--resource","r","--actor-resource","a","--prototypes","p","--area-effects","e","--source-id","s"]));
    }
}
