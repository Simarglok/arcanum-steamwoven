using System.Diagnostics;
using Arcanum.Data.Area;
using Arcanum.Data.Messages;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-UI-005 check on the installed original (ARC_AREA_ORIGINAL=1 and the ARC_AREA_* inputs). Counts and
/// comparisons only; no original text is asserted or printed.</summary>
[TestClass]
public sealed class AreaHoverOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public void CrashSiteHoverPicksSkipClickThroughSceneryAndNameEveryPickableBody()
    {
        if (Environment.GetEnvironmentVariable("ARC_AREA_ORIGINAL") != "1")
            Assert.Inconclusive("Opt-in authenticated AREA hover gate; not a synthetic PASS.");
        string Required(string role) => Environment.GetEnvironmentVariable(role) is { Length: > 0 } value ? value
            : throw new AssertFailedException("Requested original AREA gate is missing an input role: " + role);
        var clock = Stopwatch.StartNew();
        var names = AreaHover.LoadNames(Required("ARC_AREA_MODULE"));
        Assert.AreEqual(AreaNamesStatus.Ready, names.Status, names.Detail);
        var loadMs = clock.ElapsedMilliseconds;
        using var owner = AreaOriginalValidator.OpenFiles(Required("ARC_AREA_MODULE"), Required("ARC_AREA_RESOURCE1"), Required("ARC_AREA_RESOURCE2"),
            Required("ARC_AREA_MOBILE"), Required("ARC_AREA_PROTOTYPES"));
        var snapshot = owner.LoadStrict();
        var plan = AreaScenePlan.Create(snapshot);
        var index = new AreaHoverIndex(snapshot.Objects, plan.Commands, names.Catalog);
        var rows = snapshot.Objects.ToDictionary(o => o.Key);
        var drawn = plan.Commands.Where(c => c.Identity is not null).Select(c => rows[c.Identity!.Value]).ToArray();

        // 419 of the 426 drawn scenery bodies (trees, plants, blood) carry OF_CLICK_THROUGH and are never hover targets.
        Assert.AreEqual(499, drawn.Length);
        Assert.AreEqual(419, drawn.Count(AreaHoverIndex.ClickThrough));
        Assert.IsTrue(drawn.Where(AreaHoverIndex.ClickThrough).All(o => o.Type == 3));
        Assert.AreEqual(80, index.Bodies.Count);
        var pickable = index.Bodies.Select(c => rows[c.Identity!.Value]).ToArray();
        Assert.AreEqual((2, 7, 21, 16, 34), (pickable.Count(o => o.Type == 2), pickable.Count(o => o.Type == 3), pickable.Count(o => o.Type == 9),
            pickable.Count(o => o.Type == 14), pickable.Count(o => o.Type == 16)), "containers, scenery, food, generic items, NPCs");
        var shown = index.Bodies.Select(c => index.Name(c.Identity!.Value)).ToArray();
        Assert.IsTrue(shown.All(n => n is not null && !n.StartsWith('[')), "every pickable Crash Site body has a resolved, decoded name");

        // Virgil, the one dialog NPC, is shown by his not-yet-met description, which differs from his own name.
        var virgil = AreaConversationHost.FindSpeakers(snapshot).Single().Row;
        string Text(CritterRecognition recognition) => MesText.Decode(ObjectDisplayName.Resolve(virgil, names.Catalog!, recognition).Message!, AreaHoverIndex.TextProfile).Text!;
        Assert.AreEqual(Text(CritterRecognition.NotRecognized), index.Name(virgil.Key));
        Assert.AreNotEqual(Text(CritterRecognition.Recognized), index.Name(virgil.Key));
        Console.WriteLine($"AREA hover PASS: names {loadMs} ms, drawn {drawn.Length}, click-through {drawn.Count(AreaHoverIndex.ClickThrough)}, pickable {index.Bodies.Count}, all named. Not original cursor parity.");
    }
}
