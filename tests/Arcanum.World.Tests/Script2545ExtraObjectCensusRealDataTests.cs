using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-SCRIPT-2514 opt-in (<c>DAT_ROOT</c>, the game folder): the census behind ledger BS3e. Item use script 2545 tests that its extra object (the object the item is used on) carries one name and then destroys the extra object.
/// Which kind of object carries that name? Numbers only: script and prototype numbers, object types and counts, no original text.
/// </summary>
[TestClass]
public sealed class Script2545ExtraObjectCensusRealDataTests
{
    private const int NameSlot = 21, ScriptsSlot = 31, UseScript = 2545, NpcType = 16, ItemType = 9;

    [TestMethod, TestCategory("RealData")]
    public void The_extra_object_of_script_2545_is_an_NPC_never_a_scenery()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var content = ScriptContent.Over(stack);
        var program = content.Script(UseScript).Program;
        Assert.IsNotNull(program, "script 2545 reads");
        // The test of line 0 names the extra object (kind 12) and one name number; line 2 destroys (68) the extra object.
        var test = program.Lines.Single(l => l.Condition.Type == ScrCatalogue.Condition.IsNamed);
        Assert.AreEqual((byte)ScrFocusKind.ExtraObject, test.Condition.Kind(0));
        var name = test.Condition.Value(1);
        var destroys = program.Lines.SelectMany(l => new[] { l.OnTrue, l.OnFalse }).Where(c => c.Type == ScrCatalogue.Action.Destroy).ToArray();
        Assert.AreEqual(1, destroys.Length);
        Assert.AreEqual((byte)ScrFocusKind.ExtraObject, destroys[0].Kind(0));

        var catalog = WorldCatalog.Open(stack);
        var prototypes = new Dictionary<int, WorldObject?>();
        WorldFieldValue? Effective(WorldObject item, int slot)
        {
            if (item.Fields.TryGetValue(slot, out var own)) return own;
            if (item.PrototypeNumber is not { } number) return null;
            if (!prototypes.TryGetValue(number, out var prototype)) prototypes[number] = prototype = catalog.ReadPrototype(number).Prototype;
            return prototype is not null && prototype.Fields.TryGetValue(slot, out var held) ? held : null;
        }
        var named = new SortedDictionary<int, int>();
        var holders = new SortedDictionary<int, int>();
        void Look(WorldObject item)
        {
            if (Effective(item, NameSlot) is { Number: { } number } && number == name) named[item.Type] = named.GetValueOrDefault(item.Type) + 1;
            if (Effective(item, ScriptsSlot) is { Presence: WorldFieldPresence.Array } scripts)
                foreach (var element in scripts.Elements)
                    if (element.Words.Count == 3 && element.Words[2] == UseScript) holders[item.Type] = holders.GetValueOrDefault(item.Type) + 1;
        }
        foreach (var number in catalog.PrototypeNumbers) if (catalog.ReadPrototype(number).Prototype is { } prototype) Look(prototype);
        foreach (var map in catalog.Maps)
        {
            foreach (var coordinate in map.SectorFiles)
                if (catalog.ReadSector(map, coordinate).Sector is { } sector && sector.ObjectIssue is null) foreach (var item in sector.Objects) Look(item);
            foreach (var item in catalog.ReadMobiles(map).Objects) Look(item);
        }
        Console.WriteLine($"SCRIPT-2545 name {name}: carried by types {string.Join(",", named.Select(p => $"{p.Key}={p.Value}"))}; script held by types {string.Join(",", holders.Select(p => $"{p.Key}={p.Value}"))}");
        CollectionAssert.AreEqual(new[] { NpcType }, named.Keys.ToArray(), "the extra object's name sits on another kind of object");
        Assert.AreEqual(3, named[NpcType], "the NPCs that carry the name changed");
        CollectionAssert.AreEqual(new[] { ItemType }, holders.Keys.ToArray(), "script 2545 is held by an item only");
        Assert.AreEqual(1, holders[ItemType]);
    }
}
