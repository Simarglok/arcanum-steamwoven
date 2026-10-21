using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-005 hover pick, names and pointer decisions over invented records (no original data).</summary>
[TestClass]
public sealed class AreaHoverTests
{
    // Invented description numbers and text.
    private static readonly ObjectDescriptionCatalog Catalog = ObjectDescriptionCatalog.Create(
        MesFile.Parse(Encoding.Latin1.GetBytes("{100}{Synthetic Crate}{101}{Synthetic Stranger}{102}{Synthetic Hermit}{103}{}{104}{Caf\u00e9}{105}{Bad\u0081}")).File!);

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    private static AreaObjectRecord Row(int index, int type, uint flags = 0, params AreaFieldValue[] fields) =>
        new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], [Field(AreaHoverIndex.FlagsSlot, flags), .. fields], true, true, true);

    private static AreaDrawCommand Body(int index) => new(default, null!, Key(index), default, default, false, false, 4, 0, 0, 0, 0, 255, false);

    [TestMethod]
    public void PickReturnsTheFrontmostOpaqueBodyAndSkipsClickThroughBodies()
    {
        var rows = new[] { Row(0, 2), Row(1, 16), Row(2, 3, AreaHoverIndex.ClickThroughFlag), Row(3, 3, 0x804) };
        // Terrain commands carry no identity and are never picked.
        var commands = new[] { new AreaDrawCommand(default, null!, null, default, default, false, false, 0, 0, 0, 0, 0, 255, false), Body(0), Body(1), Body(2), Body(3) };
        var index = new AreaHoverIndex(rows, commands, Catalog);
        CollectionAssert.AreEqual(new AreaObjectKey?[] { Key(0), Key(1) }, index.Bodies.Select(b => b.Identity).ToArray(), "draw order kept, click-through bodies dropped");
        Assert.IsTrue(AreaHoverIndex.ClickThrough(rows[2]) && AreaHoverIndex.ClickThrough(rows[3]) && !AreaHoverIndex.ClickThrough(rows[1]));

        var opaque = new HashSet<AreaObjectKey>();
        bool At(AreaDrawCommand command) => opaque.Contains(command.Identity!.Value);
        Assert.IsNull(index.Pick(At), "nothing opaque under the pointer");
        opaque.UnionWith([Key(0), Key(1), Key(2), Key(3)]);
        Assert.AreEqual(Key(1), index.Pick(At), "the last drawn opaque body wins; click-through bodies in front are passed over");
        opaque.Remove(Key(1));
        Assert.AreEqual(Key(0), index.Pick(At), "an object behind is found once the front one is transparent there");
    }

    [TestMethod]
    public void AnAdmittedRowIsPickedOnlyWhereAMovingCommandDrawsIt()
    {
        // ARC-RENDER-033: a placed item without a row of the map is admitted; it is picked where its moving command is drawn.
        var index = new AreaHoverIndex([Row(0, 2)], [Body(0)], Catalog);
        bool All(AreaDrawCommand command) => true;
        Assert.AreEqual(Key(0), index.Pick(All, [Body(5)]), "an unknown key is not picked");
        Assert.IsFalse(index.Contains(Key(5)));
        index.Admit(Row(5, 5));
        Assert.AreEqual(Key(5), index.Pick(All, [Body(5)]));
        Assert.AreEqual(Key(0), index.Pick(All), "not from the plan");
        Assert.IsNotNull(index.Name(Key(5)));
    }

    [TestMethod]
    public void NamesFollowTheExamineChainAndWallsShowNothing()
    {
        var rows = new[]
        {
            Row(0, 2, 0, Field(ObjectDisplayName.DescriptionSlot, 100)),
            Row(1, 16, 0, Field(ObjectDisplayName.DescriptionSlot, 102), Field(ObjectDisplayName.CritterUnknownDescriptionSlot, 101)),
            Row(2, 0, 0, Field(ObjectDisplayName.DescriptionSlot, 100)),
            Row(3, 4, 0, Field(ObjectDisplayName.DescriptionSlot, 100)),
            Row(4, 3, 0, Field(ObjectDisplayName.DescriptionSlot, 103)),
            Row(5, 3, 0, Field(ObjectDisplayName.DescriptionSlot, 999)),
            Row(6, 3),
            Row(7, 3, 0, Field(ObjectDisplayName.DescriptionSlot, 104)),
            Row(8, 3, 0, Field(ObjectDisplayName.DescriptionSlot, 105)),
        };
        var index = new AreaHoverIndex(rows, rows.Select(r => Body(r.Key.SourceIndex)).ToArray(), Catalog);
        Assert.AreEqual("Synthetic Crate", index.Name(Key(0)));
        Assert.AreEqual("Synthetic Stranger", index.Name(Key(1)), "an NPC is shown as not yet met (new-game state)");
        Assert.IsNull(index.Name(Key(2)), "walls are picked but the original's message window shows nothing");
        Assert.IsNull(index.Name(Key(3)), "projectiles likewise");
        Assert.IsNull(index.Name(Key(4)), "an empty description shows nothing");
        Assert.IsNull(index.Name(Key(5)), "a number without text: the original shows an empty name");
        Assert.AreEqual("[scenery: FieldUnresolved]", index.Name(Key(6)));
        Assert.AreEqual("Caf\u00e9", index.Name(Key(7)), "text is decoded as Windows-1252");
        Assert.AreEqual("[scenery: name not decodable]", index.Name(Key(8)), "a byte Windows-1252 leaves undefined is not guessed");
        Assert.AreSame(index.Name(Key(0)), index.Name(Key(0)), "names are resolved once per object");

        var unnamed = new AreaHoverIndex(rows, [], null);
        Assert.AreEqual("[container: name unavailable]", unnamed.Name(Key(0)));
        Assert.AreEqual("[NPC: name unavailable]", unnamed.Name(Key(1)));
        Assert.IsNull(unnamed.Name(Key(2)));
    }

    [TestMethod]
    public void TooltipShowsTheNameAndTheLeftClickAction()
    {
        Assert.IsNull(AreaHover.Tooltip(null, AreaHoverAction.Walk), "no tooltip without a named object");
        Assert.AreEqual("Crate", AreaHover.Tooltip("Crate", AreaHoverAction.None), "a guarded click: the name only");
        Assert.AreEqual("Crate\nLeft click: walk here", AreaHover.Tooltip("Crate", AreaHoverAction.Walk));
        var labels = Enum.GetValues<AreaHoverAction>().Select(AreaHover.Label).ToArray();
        Assert.AreEqual(labels.Length, labels.Distinct().Count(), "every action has its own label");
        Assert.AreEqual("walk to talk", AreaHover.Label(AreaHoverAction.WalkToTalk));
    }

    private sealed class Steps(HashSet<CellCoord> cells, HashSet<(CellCoord From, NeighborDirection Direction)> open) : IRouteStepSource
    {
        public bool Contains(CellCoord cell) => cells.Contains(cell);
        public RouteStepVerdict Assess(CellCoord from, NeighborDirection direction) =>
            open.Contains((from, direction)) ? RouteStepVerdict.Open : RouteStepVerdict.Blocked;
    }

    [TestMethod]
    public void AGoalIsWalkableOnlyWhenAnAdmittedNeighbourEntersItByAnOpenStep()
    {
        var goal = new CellCoord(6, 1);
        var cells = new HashSet<CellCoord>();
        for (var x = 4; x <= 8; x++) for (var y = -1; y <= 3; y++) cells.Add(new(x, y));
        Assert.IsFalse(AreaHover.GoalEnterable(new Steps(cells, []), goal), "every entry blocked: NoRoute");
        // Each of the eight neighbours, alone, makes the goal enterable through the step that ends on it.
        foreach (var direction in Enum.GetValues<NeighborDirection>())
        {
            var delta = RoutePlanner.Neighbor(default, direction);
            var from = new CellCoord(goal.X - delta.X, goal.Y - delta.Y);
            Assert.AreEqual(goal, RoutePlanner.Neighbor(from, direction));
            Assert.IsTrue(AreaHover.GoalEnterable(new Steps(cells, [(from, direction)]), goal), direction.ToString());
            cells.Remove(from);
            Assert.IsFalse(AreaHover.GoalEnterable(new Steps(cells, [(from, direction)]), goal), "a neighbour outside the admitted cells does not count");
            cells.Add(from);
        }
        // An open step out of the goal is not an entry.
        Assert.IsFalse(AreaHover.GoalEnterable(new Steps(cells, [(goal, NeighborDirection.XPlus)]), goal));
        Assert.IsFalse(AreaHover.GoalEnterable(new Steps(cells, []), new CellCoord(int.MaxValue, int.MinValue)), "edge goals do not overflow");
    }

    [TestMethod]
    public void GroundActionMatchesThePlannersRefusalsWithoutASearch()
    {
        var cells = new HashSet<CellCoord>();
        for (var x = -40; x <= 40; x++) for (var y = -2; y <= 2; y++) cells.Add(new(x, y));
        var open = new HashSet<(CellCoord, NeighborDirection)>();
        foreach (var cell in cells) foreach (var direction in Enum.GetValues<NeighborDirection>()) open.Add((cell, direction));
        var steps = new Steps(cells, open);
        var actor = new CellCoord(0, 0);
        Assert.AreEqual(AreaHoverAction.Walk, AreaHover.Ground(steps, actor, actor), "the actor's own cell");
        Assert.AreEqual(AreaHoverAction.Walk, AreaHover.Ground(steps, actor, new(32, 0)), "at the axis reach");
        Assert.AreEqual(AreaHoverAction.Walk, AreaHover.Ground(steps, actor, new(40, 0)), "beyond the axis reach: a corridor (ARC-WORLD-042)");
        Assert.AreEqual(AreaHoverAction.NoRoute, AreaHover.Ground(steps, actor, new(0, 3)), "outside the area: ROUTE_TARGET_OUTSIDE_AREA");
        Assert.AreEqual(AreaHoverAction.NoRoute, AreaHover.Ground(steps, new(0, 9), new(1, 0)), "actor outside: ROUTE_SOURCE_OUTSIDE_AREA");
        Assert.AreEqual(AreaHoverAction.NoRoute, AreaHover.Ground(new Steps(cells, []), actor, new(3, 0)), "no entry: ROUTE_NOT_FOUND pre-check");
    }

    [TestMethod]
    public void NamesNeedTheModuleArchiveInsideAGameFolder()
    {
        Assert.AreEqual(AreaNamesStatus.ModuleOutsideGameRoot, AreaHover.LoadNames("synthetic-module").Status);
        var root = Directory.CreateTempSubdirectory("arc-ui-005-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            var module = Path.Combine(root.FullName, "modules", "Synthetic.dat");
            File.WriteAllBytes(module, []);
            Assert.AreEqual(AreaNamesStatus.ModuleOutsideGameRoot, AreaHover.LoadNames(Path.Combine(root.FullName, "Synthetic.dat")).Status, "not under modules/");
            Assert.AreEqual(AreaNamesStatus.ModuleOutsideGameRoot, AreaHover.LoadNames(Path.Combine(root.FullName, "modules", "Synthetic.txt")).Status);
            var load = AreaHover.LoadNames(module);
            Assert.AreEqual(AreaNamesStatus.MountFailed, load.Status, "no tig.dat: the game folder does not mount");
            Assert.IsFalse(load.Detail.Contains(root.FullName, StringComparison.Ordinal), "details never carry a path");
            Assert.IsNull(load.Catalog);
        }
        finally { root.Delete(true); }
    }
}
