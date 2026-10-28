using System.Numerics;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-WORLD-081 check (DAT_ROOT = the game folder): a main-map window around a generated elven forest sector lists its
/// template sectors' scripts, the plant generator 30285 runs on entry with the PC beside a marker, and its herbs lie next to the marker,
/// up to the Steamwoven limit, and past it under the original's rule.</summary>
[TestClass]
public sealed class TemplateSectorPlantsOriginalTests
{
    private const int Generator = 30285, MarkerName = 1673, NameSlot = 21;

    [TestMethod, TestCategory("RealData")]
    public void The_plant_generator_of_a_generated_sector_creates_its_herbs_beside_the_marker()
    {
        var root = Root();
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Path.Combine(root, "modules", "Arcanum.dat"),
            "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
            "--game-root", root, "--area-map", "Arcanum1-024-fixed", "--area-map-sector", "1384,239"]);
        var result = AreaLoadOperation.Execute(5, arguments, default);
        var snapshot = result.Area!.Snapshot;
        // Every scripted template sector of the window runs the generator; the generated elven forest sector asked is one of them.
        Assert.IsTrue(result.MapTemplateScripts.Count > 0);
        Assert.IsTrue(result.MapTemplateScripts.All(p => p.Script == Generator && p.Trigger == MapTransitionTrigger.SectorEntry));
        var placed = result.MapTemplateScripts.Single(p => p.Sector == new WorldSectorCoordinate(1384, 239));
        // Each template holds exactly one marker: the scenery whose instance name is 1673 (ARC-RES-073).
        var marker = snapshot.Objects.Single(row => row.Type == 3 && row.WorldX is { } x && row.WorldY is { } y
            && WorldSectorCoordinate.Containing(new WorldLocation(x, y)) == placed.Sector
            && row.EffectiveFields.FirstOrDefault(f => f.Slot == NameSlot)?.Number == MarkerName);
        var at = new CellCoord(checked((int)marker.WorldX!.Value), checked((int)marker.WorldY!.Value));
        // Products: A = 10000 + counter 2 and B = 10000 + counter 0 (the garbage index reads counter 0, PG6), prototypes 20 on (PG8).
        var products = new[] { 10020 + (int)(placed.Counters >> 16 & 0xFF), 10020 + (int)(placed.Counters & 0xFF) };
        var rounds = BitOperations.PopCount(placed.LocalFlags);
        Assert.IsTrue(rounds is >= 1 and <= 3, $"R {rounds}");

        var found = AreaContentRoot.Discover(arguments.ModulePath);
        using var items = new AreaPrototypeItems(ContentStack.Open(found.Plan!).Stack!);
        using var content = ConversationSessionFixture.Scripts(root);
        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        var drop = new AreaDropMap(result.AreaStepSnapshot!, snapshot.Objects, new HashSet<AreaObjectKey>(), new HashSet<CellCoord> { at }, _ => false, _ => false);
        var host = new AreaUseScriptHost(session, snapshot.RuntimeMap, snapshot.Objects, [], content,
            new AreaScriptWorld(() => at, _ => false, items.ScriptItem, () => drop));
        int Entered(int times)
        {
            for (var entry = 0; entry < times; entry++)
            {
                var ran = host.RunPlaced(placed, session.Rules.Scripts);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            }
            return session.QueryPlacedItems().Count(p => CreatedItemRules.Near(at, p.Cell));
        }

        Assert.AreEqual(CreatedItemRules.Limit, Entered(40), "forty crossings fill the patch to the Steamwoven limit");
        var created = host.TakeCreations();
        Assert.IsTrue(created.All(c => c.Near.Code == AreaUseScriptHost.EntityId(marker)), "only the asked sector's marker is in the vicinity");
        Assert.IsTrue(session.QueryPlacedItems().All(p => p.Source == SessionPlacedSource.Created && products.Contains(p.Kind.Prototype)
            && p.Kind.Type == SessionItemType.Food), string.Join(", ", session.QueryPlacedItems().Select(p => p.Kind)));
        Assert.IsTrue(created.Any(c => c.Result.Disposition == GameSessionDisposition.NoChange), "a full patch takes no more");
        session.SelectRules(RuleMode.Original);
        Assert.IsTrue(Entered(40) > CreatedItemRules.Limit, "the original piles them up");
    }

    private static string Root()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is { Length: > 0 } root) return root;
        Assert.Inconclusive("DAT_ROOT is required for the opt-in template sector plant check; this is not a synthetic pass.");
        return string.Empty;
    }
}
