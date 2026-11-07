using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-161 (B37): the Control Box of the Panarii temple (description 30605) as the viewer's attack targets read it from the
/// installed data (opt-in, DAT_ROOT = the game folder; counts and numbers only).</summary>
[TestClass]
public sealed class AreaControlBoxRealDataTests
{
    private const string MapName = "shrouded hills-panarii temple lev 1";
    private const int ControlBoxDescription = 30605, BustPoint = 30;

    [TestMethod, TestCategory("RealData")]
    public void The_control_box_is_a_200_hit_point_metal_scenery_with_a_bust_script()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var module = Path.Combine(root, "modules", "Arcanum.dat");
        var result = AreaLoadOperation.Execute(1, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", module,
            "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
            "--game-root", root, "--area-map", MapName]), default);
        var snapshot = result.Area!.Snapshot;
        var boxes = snapshot.Objects.Where(o => o.Type == CombatFieldReader.SceneryType &&
            CombatFieldReader.ReadBreakable(o).Fields is { } f && f.Object.Description == ControlBoxDescription).ToArray();
        Assert.AreEqual(1, boxes.Length, "one Control Box on the map");
        var fields = CombatFieldReader.ReadBreakable(boxes[0]).Fields!;
        Assert.AreEqual((4032, 200, 0, 5), (boxes[0].PrototypeNumber, fields.Object.HitPoints, fields.Object.HitPointDamage, fields.Object.Material),
            "prototype 4032: 200 hit points, no damage, metal");
        Assert.AreEqual(0u, fields.Object.Flags & 0x400000, "not invulnerable");
        CollectionAssert.AreEqual(new[] { BustPoint }, fields.Object.ScriptPoints.ToArray(), "only the bust script point");

        using var session = ConversationSessionFixture.Create();
        var targets = new AreaObjectTargets(snapshot.RuntimeMap, snapshot.Objects, new Dictionary<AreaObjectKey, (string Entity, SessionOpenableBaseline Baseline)>(), null);
        var target = targets.Target(session, boxes[0].Key);
        Assert.IsNotNull(target?.Object, target?.Refusal);
        var baseline = target.Object.Baseline;
        Assert.AreEqual((DamageableObjectKind.Scenery, 200, ObjectMaterial.Metal, false), (baseline.Kind, baseline.Points + baseline.Adjustment, baseline.Material, baseline.Invulnerable));
    }
}
