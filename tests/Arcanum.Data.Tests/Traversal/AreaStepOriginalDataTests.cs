using System.Text.Json;
using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Traversal;

[TestClass]
public sealed class AreaStepOriginalDataTests
{
    private static readonly AreaStepCell Spawn = new(92934, 82561);

    [TestMethod, TestCategory("RealData")]
    public void Original_core_profile_resolves_every_cell_and_matches_the_closure_research()
    {
        var module = Environment.GetEnvironmentVariable("ARC_AREA_MODULE");
        if (string.IsNullOrEmpty(module)) Assert.Inconclusive("Opt-in original Area inputs plus ARC_AREA_EFFECTS and ARC_AREA_PATCH are required.");
        using var owner = AreaOriginalValidator.OpenFiles(module, Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"),
            Required("ARC_AREA_MOBILE"), Required("ARC_AREA_PROTOTYPES"));
        var area = owner.LoadStrict();
        var snapshot = AreaStepInspector.ValidateOriginalData(area, module, Required("ARC_AREA_RESOURCE2"), Required("ARC_AREA_EFFECTS"),
            Required("ARC_AREA_PATCH"), Required("ARC_AREA_MOBILE"), 7);
        Console.WriteLine(JsonSerializer.Serialize(new { snapshot.EvidenceSha256, snapshot.AdmittedCells, snapshot.TerrainBlockedCells,
            snapshot.TerrainUnknownCells, snapshot.ObjectBlockerCells, snapshot.UnknownObjects }));
        Assert.AreEqual(AreaStepInputKind.AuthenticatedOriginal, snapshot.InputIdentity.Kind);
        Assert.AreEqual(9 * 4096, snapshot.AdmittedCells);
        Assert.AreEqual(0, snapshot.TerrainUnknownCells);
        Assert.AreEqual(0, snapshot.UnknownObjects);

        // Frozen production observations at spawn (research F7), now from the area profile.
        var expected = new (CardinalTraversalDirection Direction, CardinalTraversalDisposition Disposition)[]
        {
            (CardinalTraversalDirection.XMinus, CardinalTraversalDisposition.Blocked), (CardinalTraversalDirection.XPlus, CardinalTraversalDisposition.Open),
            (CardinalTraversalDirection.XMinusYMinus, CardinalTraversalDisposition.Blocked), (CardinalTraversalDirection.XMinusYPlus, CardinalTraversalDisposition.Blocked),
            (CardinalTraversalDirection.XPlusYPlus, CardinalTraversalDisposition.Open), (CardinalTraversalDirection.XPlusYMinus, CardinalTraversalDisposition.Open),
        };
        foreach (var (direction, disposition) in expected)
        {
            var result = snapshot.Assess(snapshot.Request(Spawn, Step(Spawn, direction), direction));
            Assert.AreEqual(CardinalTraversalCoverage.Supported, result.Coverage, direction.ToString());
            Assert.AreEqual(disposition, result.Disposition, direction.ToString());
            Assert.AreEqual(0, result.Gaps.Count);
        }
        var back = snapshot.Assess(snapshot.Request(Step(Spawn, CardinalTraversalDirection.XPlus), Spawn, CardinalTraversalDirection.XMinus));
        Assert.AreEqual((CardinalTraversalCoverage.Supported, (CardinalTraversalDisposition?)CardinalTraversalDisposition.Open), (back.Coverage, back.Disposition), "Reverse step back to spawn.");

        // Breadth-first reachability inside the admitted core equals the research's core share (F8: 7,150 cells).
        var reached = new HashSet<AreaStepCell> { Spawn }; var queue = new Queue<AreaStepCell>([Spawn]); var refused = new Dictionary<string, int>();
        while (queue.Count != 0)
        {
            var cell = queue.Dequeue();
            foreach (var direction in Enum.GetValues<CardinalTraversalDirection>())
            {
                var next = Step(cell, direction);
                var result = snapshot.Assess(snapshot.Request(cell, next, direction));
                if (result.Coverage == CardinalTraversalCoverage.Unknown) Assert.Fail($"Unknown step {cell}->{next}: {result.ReasonCode}");
                if (result.Disposition == CardinalTraversalDisposition.Open) { if (reached.Add(next)) queue.Enqueue(next); }
                else if (result.Coverage == CardinalTraversalCoverage.Unsupported) refused[result.ReasonCode] = refused.GetValueOrDefault(result.ReasonCode) + 1;
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Reached = reached.Count, Refused = refused }));
        Assert.AreEqual(7150, reached.Count);
        Assert.IsTrue(reached.All(snapshot.IsAdmitted));
        Assert.IsTrue(refused.Count != 0 && refused.Keys.All(k => k is "DESTINATION_OUTSIDE_ADMITTED_AREA" or "SHOULDER_OUTSIDE_ADMITTED_AREA"));

        var repeat = AreaStepInspector.ValidateOriginalData(owner.LoadStrict(), module, Required("ARC_AREA_RESOURCE2"), Required("ARC_AREA_EFFECTS"),
            Required("ARC_AREA_PATCH"), Required("ARC_AREA_MOBILE"), 7);
        Assert.AreEqual(snapshot.EvidenceSha256, repeat.EvidenceSha256, "Construction is deterministic.");
    }

    private static AreaStepCell Step(AreaStepCell cell, CardinalTraversalDirection direction) => direction switch
    {
        CardinalTraversalDirection.XMinusYMinus => new(cell.X - 1, cell.Y - 1),
        CardinalTraversalDirection.XMinus => new(cell.X - 1, cell.Y),
        CardinalTraversalDirection.XMinusYPlus => new(cell.X - 1, cell.Y + 1),
        CardinalTraversalDirection.YPlus => new(cell.X, cell.Y + 1),
        CardinalTraversalDirection.XPlusYPlus => new(cell.X + 1, cell.Y + 1),
        CardinalTraversalDirection.XPlus => new(cell.X + 1, cell.Y),
        CardinalTraversalDirection.XPlusYMinus => new(cell.X + 1, cell.Y - 1),
        _ => new(cell.X, cell.Y - 1),
    };

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value
        : throw new AssertInconclusiveException($"{name} is required for the opt-in original check.");
}
