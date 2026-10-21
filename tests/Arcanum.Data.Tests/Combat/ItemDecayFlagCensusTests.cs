using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Combat;

/// <summary>Opt-in ARC-WORLD-062 census (DAT_ROOT = the game folder): the item prototypes with item flag 0x20000, which the original's
/// decay test reads (ledger ID rows); an item without it decays when dropped.</summary>
[TestClass]
public sealed class ItemDecayFlagCensusTests
{
    [TestMethod, TestCategory("RealData")]
    public void Item_prototypes_with_the_no_decay_flag_are_counted()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census."); return; }
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var prototypes = ContentPrototypes.Open(stack);
        var items = prototypes.Numbers.Select(number => (number, fields: CombatFieldReader.ReadPrototype(prototypes.Read, number).Fields))
            .Where(p => p.fields?.Item is not null).ToArray();
        var flagged = items.Where(p => (p.fields!.Item!.Flags & 0x20000) != 0).Select(p => p.number).ToArray();
        Console.WriteLine($"ARC-WORLD-062 items={items.Length} no-decay={flagged.Length} prototypes={string.Join(",", flagged.Take(40))}");
        Assert.IsTrue(items.Length > 0);
    }
}
