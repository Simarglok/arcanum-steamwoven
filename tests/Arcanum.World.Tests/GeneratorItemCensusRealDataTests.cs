using System.Security.Cryptography;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-WORLD-1707 (ledger GS5) opt-in (<c>DAT_ROOT</c>, the game folder): what the held items of the installed monster generators carry beyond their prototypes, read from every map's mobile aggregate. A generator's copy rebuilds each
/// item from its prototype (<c>GameSessionOwner.CopyGeneratorItems</c>), so this census says which instance fields of the 83 installed items that loses. Only counts are printed.
/// </summary>
[TestClass]
public sealed class GeneratorItemCensusRealDataTests
{
    private const int NpcType = 16, NpcFlagsSlot = 69, GeneratorBit = 0x80000, CritterListSlot = 56, ItemFlagsSlot = 36, ObjectFlagsSlot = 18, SpellFlagsSlot = 19, ScriptsSlot = 31;
    // The placement and holder bookkeeping an item carries in a list: location 1, offsets 2 and 3, object flags 18, parent 37, inventory location 43, quantity of a gold pile or ammunition 60.
    private static readonly HashSet<int> Bookkeeping = [1, 2, 3, 18, 37, 43, 60];
    private const long RestockPersistent = 0x2000, ItemFlagsTheSessionReads = 0x10 | 0x20 | 0x2000 | 0x20000, ObjectFlagsTheSessionReads = 0x2 | 0x400000;

    [TestMethod, TestCategory("RealData")]
    public void The_installed_generators_items_carry_one_content_override_and_none_of_the_flags_the_session_reads()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var patch = AreaMapPlans.ReadMapPatch(stack);
        var raw = new byte[24];
        int generators = 0, items = 0, unlisted = 0, persistent = 0, itemFlagsRead = 0, objectFlagsRead = 0, withScripts = 0, spellFlags = 0, itemFlagsStored = 0;
        var content = new List<string>();
        foreach (var map in catalog.Maps)
        {
            var read = patch is null ? catalog.ReadMobiles(map) : patch.ReadMobiles(catalog, map);
            if (read.Source != WorldMobileSource.Aggregate || read.Issues.Count > 0) continue;
            var byId = read.Objects.GroupBy(o => o.OwnIdSha256).ToDictionary(g => g.Key, g => g.First());
            long Effective(WorldObject o, int slot)
            {
                if (o.Fields.TryGetValue(slot, out var own) && own.Presence == WorldFieldPresence.Value && own.Number is { } n) return n;
                var proto = o.PrototypeNumber is { } p ? catalog.ReadPrototype(p).Prototype : null;
                return proto is not null && proto.Fields.TryGetValue(slot, out var f) && f.Presence == WorldFieldPresence.Value && f.Number is { } v ? v : 0;
            }
            foreach (var generator in read.Objects.Where(o => o.Type == NpcType && (Effective(o, NpcFlagsSlot) & GeneratorBit) != 0))
            {
                generators++;
                if (!generator.Fields.TryGetValue(CritterListSlot, out var list) || list.Presence != WorldFieldPresence.Array) continue;
                foreach (var element in list.Elements)
                {
                    for (var w = 0; w < 6; w++) System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(w * 4), (uint)element.Words[w]);
                    if (!byId.TryGetValue(Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(), out var item)) { unlisted++; continue; }
                    items++;
                    var proto = catalog.ReadPrototype(item.PrototypeNumber!.Value).Prototype!;
                    // The slots the instance stores with a value other than its prototype's, beyond the bookkeeping.
                    var differing = item.Fields.Where(pair => !Bookkeeping.Contains(pair.Key) && pair.Value.Presence == WorldFieldPresence.Value &&
                        (!proto.Fields.TryGetValue(pair.Key, out var own) || own.Number != pair.Value.Number)).Select(pair => pair.Key).Order().ToArray();
                    if (differing.Length > 0) content.Add($"type {item.Type} slots {string.Join(",", differing)}");
                    if (item.Fields.ContainsKey(ScriptsSlot)) withScripts++;
                    if (item.Fields.ContainsKey(SpellFlagsSlot)) spellFlags++;
                    if ((Effective(item, ItemFlagsSlot) & RestockPersistent) != 0) persistent++;
                    if ((Effective(item, ItemFlagsSlot) & ItemFlagsTheSessionReads) != 0) itemFlagsRead++;
                    if ((Effective(item, ObjectFlagsSlot) & ObjectFlagsTheSessionReads) != 0) objectFlagsRead++;
                    if (item.Fields.ContainsKey(ItemFlagsSlot)) itemFlagsStored++;
                }
            }
        }
        Console.WriteLine($"ARC-WORLD-1707 generators={generators} items={items} unlisted={unlisted} persistent={persistent} sessionItemFlags={itemFlagsRead} sessionObjectFlags={objectFlagsRead} withScripts={withScripts} itemFlagsStored={itemFlagsStored} withSpellFlags={spellFlags} content=[{string.Join("; ", content)}]");
        Assert.AreEqual((27, 83, 0), (generators, items, unlisted), "the census changed: update ledger GS5");
        // No installed item carries the persistent flag the restock keeps (the original's clear keeps such an item, 0x463C60), none sets an item flag or an object flag the session reads from an item's facts, and no
        // instance stores flags (item field 36), scripts or spell flags of its own.
        Assert.AreEqual((0, 0, 0, 0, 0, 0), (persistent, itemFlagsRead, objectFlagsRead, withScripts, spellFlags, itemFlagsStored));
        CollectionAssert.AreEqual(new[] { "type 7 slots 25" }, content, "the one content override is the armor class of one armor");
    }
}
