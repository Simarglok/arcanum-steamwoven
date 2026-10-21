using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Objects;
using Arcanum.Data.Traversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

/// <summary>
/// ARC-DATA-028 opt-in check of the installed original (DAT_ROOT = the game folder): which objects the installed
/// <c>Rules\map.patch</c> changes, on which map, with which fields before and after; every interior still plans, loads and
/// builds step evidence, and no step evidence is Unknown for a map-patch reason. Only numbers are printed (line numbers,
/// object types, prototype numbers, field slots and values), never original text.
/// </summary>
[TestClass]
public sealed class MapPatchRealDataTests
{
    // Measured 2026-10-18: the installed patch's lines that name a mobile object of an interior.
    private static readonly (string Map, int Line)[] Interiors =
    [
        ("ashbury-scienticians underground", 97), ("caladon-panarrii temple", 99), ("caladons sewers", 63),
        ("cave of the bangellian scourge", 101), ("cave of the bangellian scourge", 103), ("iron clan hq", 89), ("iron clan hq", 91),
        ("iron clan hq", 93), ("iron clan hq", 95), ("temple of the derian ka", 83), ("temple of the derian ka", 87),
    ];

    // The lines that attach script 0 to the dialog slot: the original cannot load script 0 by number, so they change nothing.
    private static readonly int[] NoEffect = [89, 91, 93, 95, 97];

    [TestMethod, TestCategory("RealData")]
    public void The_installed_patch_changes_the_objects_it_names_and_every_interior_still_steps()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var catalog = WorldCatalog.Open(stack);
        var winner = ContentPhase.Candidate(stack, "rules/map.patch", ContentLoadPhase.ModuleLoaded)!;
        Assert.AreEqual(ContentSourceRole.ModulePatch, winner.Source.Role);
        var file = MapPatchFile.Parse(stack.Read(winner).Bytes!);
        Assert.AreEqual((50, 0), (file.Lines.Count, file.Issues.Count));

        // Which lines name a mobile object of which map, by the object's own GUID.
        var touched = new List<(string Map, int Line)>();
        foreach (var map in catalog.Maps.Where(m => m.AggregatePath is not null))
        {
            var bytes = stack.Read(stack.Resolve(map.AggregatePath!).Winner!).Bytes!; var offset = 16; var options = new WorldLoaderOptions();
            while (offset < bytes.Length)
            {
                var record = ObjectWireReader.Read(bytes.AsSpan(offset), options.Wire, maxRecordBytes: options.MaxMobileBytes); offset += record.Length;
                if (record.Own.Entity is { Kind: 2 } entity) touched.AddRange(file.For(MapPatchFile.ObjectGuid(entity)).Select(l => (map.Name, l.Line)));
            }
        }
        Assert.AreEqual(32, touched.Count(t => t.Map == "arcanum1-024-fixed"), "lines naming objects of the main world map, which cannot load whole yet");
        CollectionAssert.AreEquivalent(Interiors, touched.Where(t => t.Map != "arcanum1-024-fixed").ToArray());
        Assert.AreEqual(7, file.Lines.Count - touched.Count, "lines naming no object of any installed map");

        var patchedObjects = 0;
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            var plan = AreaMapPlans.Create(stack, catalog, map.Name);
            Assert.AreEqual(AreaMapPlanStatus.Ready, plan.Status, map.Name);
            Assert.IsNotNull(plan.Plan!.Plan.MapPatch, map.Name);
            using var owner = plan.Plan.Open();
            var snapshot = owner.LoadStrict();
            var patched = snapshot.Objects.Where(o => o.MapPatch is not null).ToArray();
            CollectionAssert.AreEquivalent(Interiors.Where(i => i.Map == map.Name).Select(i => i.Line).ToArray(), patched.SelectMany(o => o.MapPatch!.Lines).ToArray(), map.Name);
            Assert.IsTrue(patched.All(o => o.MapPatch!.Unmodelled.Count == 0), map.Name);
            patchedObjects += patched.Sum(o => o.MapPatch!.Lines.Count);
            if (patched.Length > 0)
            {
                // Before: the mobile file's own fields over the prototype's; after: the snapshot's effective fields.
                var raw = catalog.ReadMobiles(map).Objects.ToDictionary(o => o.OwnIdSha256);
                foreach (var o in patched)
                {
                    var before = raw[o.Key.ObjectIdSha256].Fields;
                    var prototype = catalog.ReadPrototype(o.PrototypeNumber).Prototype!.Fields;
                    var changes = o.EffectiveFields.Where(f => f.Origin == AreaValueOrigin.Instance).Select(f =>
                    {
                        var old = before.GetValueOrDefault(f.Slot) ?? prototype.GetValueOrDefault(f.Slot);
                        var oldText = old is null ? "-" : old.Number?.ToString() ?? $"[{string.Join(" ", old.Elements.Select(e => $"{e.Key}:{string.Join("/", e.Words)}"))}]";
                        var newText = f.Number?.ToString() ?? $"[{string.Join(" ", f.Elements.Select(e => $"{e.Key}:{string.Join("/", e.Words)}"))}]";
                        return oldText == newText ? null : $"slot {f.Slot}: {oldText} -> {newText}";
                    }).Where(c => c is not null).ToArray();
                    Assert.AreEqual(o.MapPatch!.Lines.All(NoEffect.Contains), changes.Length == 0, $"{map.Name} line {string.Join(",", o.MapPatch!.Lines)}");
                    Console.WriteLine($"ARC-DATA-028 map={map.Name} lines={string.Join(",", o.MapPatch!.Lines)} type={o.Type} prototype={o.PrototypeNumber} {string.Join("; ", changes)}");
                }
            }
            var steps = AreaStepInspector.InspectWholeMap(snapshot, stack, map.AggregatePath, 1);
            Assert.AreEqual(0, steps.TerrainUnknownCells, map.Name);
            Assert.IsFalse(steps.ObjectEvaluations.Any(e => e.Reason.StartsWith("MAP_PATCH", StringComparison.Ordinal)), map.Name);
        }
        Assert.AreEqual(Interiors.Length, patchedObjects, "lines applied to interior objects");
    }

    [TestMethod, TestCategory("RealData")]
    public void Under_Steamwoven_the_script_zero_lines_empty_the_dialog_slot()
    {
        // ARC-DATA-037 (MP7): the five script-0 lines empty attachment 9 of the object they name, which held a script before.
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var opened = ContentStack.Open(ContentMountPlan.Discover(root).Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var catalog = WorldCatalog.Open(stack);
        var emptied = 0;
        foreach (var name in Interiors.Where(i => NoEffect.Contains(i.Line)).Select(i => i.Map).Distinct())
        {
            long[]? Dialog(Arcanum.Data.Fixes.DataFixProfile data, int line)
            {
                var plan = AreaMapPlans.Create(stack, catalog, name, data);
                Assert.AreEqual(AreaMapPlanStatus.Ready, plan.Status, name);
                using var owner = plan.Plan!.Open();
                var o = owner.LoadStrict().Objects.Single(o => o.MapPatch is { } p && p.Lines.Contains(line));
                return o.EffectiveFields.SingleOrDefault(f => f.Slot == 31)?.Elements.SingleOrDefault(e => e.Key == 9)?.Words.ToArray();
            }
            foreach (var line in Interiors.Where(i => i.Map == name && NoEffect.Contains(i.Line)).Select(i => i.Line))
            {
                var before = Dialog(Arcanum.Data.Fixes.DataFixProfile.Original, line);
                Assert.IsTrue(before is { Length: 3 } && before[2] != 0, $"{name} line {line}: Original keeps a dialog script");
                CollectionAssert.AreEqual(new long[] { 0, 0, 0 }, Dialog(Arcanum.Data.Fixes.DataFixProfile.Steamwoven, line), $"{name} line {line}");
                Console.WriteLine($"ARC-DATA-037 map={name} line={line} dialog {before![2]} -> 0");
                emptied++;
            }
        }
        Assert.AreEqual(NoEffect.Length, emptied);
    }
}
