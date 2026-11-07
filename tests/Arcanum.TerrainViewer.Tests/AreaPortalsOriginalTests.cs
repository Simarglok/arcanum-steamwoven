using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-WORLD-051 check (DAT_ROOT = the game folder): the doors and windows of the installed interiors at
/// runtime, with every frame of their ART in the map's repository.</summary>
[TestClass]
public sealed class AreaPortalsOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_interior_door_has_its_frames_and_a_closed_unlocked_door_opens_by_a_step()
    {
        var root = Root();
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        int maps = 0, doors = 0, windows = 0, multiFrame = 0, unlockedClosed = 0, opened = 0;
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            TerrainLoadResult result;
            try { result = AreaLoadOperation.Execute(1, Arguments(root, map.Name), default); }
            catch (Exception error) when (error is InvalidOperationException or AreaException) { continue; }
            var area = result.Area!;
            var portals = AreaPortals.From(area.Snapshot, area.Plan);
            if (portals.All.Count == 0) continue;
            maps++;
            using var session = ConversationSessionFixture.Create();
            foreach (var portal in portals.All)
            {
                if (portal.Art.Door) doors++; else windows++;
                if (portal.Frames.Count > 1)
                {
                    multiFrame++;
                    // Every frame of the ART is a visual of the map and its pixels are in the repository.
                    Assert.AreEqual(portal.Art.FrameCount, portal.Frames.Count, $"{map.Name} {portal.Entity}");
                    foreach (var frame in portal.Frames) Assert.IsTrue(area.Frames.ContainsKey(AreaFrameKey.From(frame)), $"{map.Name} {portal.Entity}");
                }
                if (!portal.Art.Door || portal.OpenFrame == 0 || portal.Baseline.Frame != 0 || portal.UnlockScript) continue;
                if (portals.Passages(session)[portal.Key] != AreaPortalPassage.Open) continue;
                unlockedClosed++;
                // A step through the door opens it to the last frame of its rotation's range (ledger LK10).
                var steps = result.AreaStepSnapshot!;
                var crossing = Crossing(steps, portal, portals, session);
                if (crossing is null) continue;
                var crossed = portals.OpenCrossed(session, crossing);
                Assert.AreEqual(portal, crossed.Single());
                Assert.AreEqual(portal.OpenFrame, portals.Frame(session, portal));
                Assert.AreEqual(portal.Art.Rotation is 0 or 1 or 6 or 7 ? 6 : 3, portal.OpenFrame);
                opened++;
            }
        }
        Console.WriteLine($"ARC-WORLD-051 portals maps={maps} doors={doors} windows={windows} multi-frame={multiFrame} unlocked-closed={unlockedClosed} opened={opened}");
        Assert.IsTrue(maps > 10 && doors > 50 && multiFrame > 50 && opened > 10, $"maps={maps} doors={doors} multi={multiFrame} opened={opened}");
    }

    // The cardinal step across the door's edge from its own tile, when it is Open by the runtime passages.
    private static AreaStepAssessment? Crossing(AreaStepSnapshot steps, AreaPortal portal, AreaPortals portals, Arcanum.Session.GameSessionOwner session)
    {
        foreach (var (direction, dx, dy) in new[] { (CardinalTraversalDirection.XMinus, -1, 0), (CardinalTraversalDirection.YPlus, 0, 1),
            (CardinalTraversalDirection.XPlus, 1, 0), (CardinalTraversalDirection.YMinus, 0, -1) })
        {
            var cell = portal.Cell;
            var to = new AreaStepCell(cell.X + dx, cell.Y + dy);
            if (!steps.IsAdmitted(cell) || !steps.IsAdmitted(to)) continue;
            var step = steps.Assess(steps.Request(cell, to, direction), portals.Passages(session));
            if (step.Portals.Contains(portal.Key) && step.Disposition == CardinalTraversalDisposition.Open) return step;
        }
        return null;
    }

    private static string Root()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } root) return root;
        Assert.Inconclusive("DAT_ROOT is required for the opt-in portal check; this is not a synthetic pass.");
        return string.Empty;
    }

    private static TerrainViewerArguments Arguments(string root, string map) => TerrainViewerArguments.Parse(["--mode", "crash-site-area",
        "--module", Path.Combine(root, "modules", "Arcanum.dat"), "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test",
        "--actor-resource", Path.Combine(root, "arcanum1.dat"), "--game-root", root, "--area-map", map]);
}
