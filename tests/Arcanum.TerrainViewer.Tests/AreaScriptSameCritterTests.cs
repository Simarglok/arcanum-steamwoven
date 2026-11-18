using Arcanum.Scripting.Scr;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1899: the script hosts tell one critter named by two maps (a carried follower by its home map and by the shown map) by its own id; sector objects and the same code on one map keep the reference test.</summary>
[TestClass]
public sealed class AreaScriptSameCritterTests
{
    [TestMethod]
    public void A_mobile_named_by_two_maps_is_one_object()
    {
        Assert.IsTrue(AreaScriptHost.SameCritter(new(3, "oid:ab12"), new(7, "oid:ab12")));
        Assert.IsTrue(AreaScriptHost.SameCritter(new(3, AreaScriptHost.PcCode), new(7, AreaScriptHost.PcCode)));
        Assert.IsTrue(AreaScriptHost.SameCritter(new(3, "oid:ab12"), new(3, "oid:ab12")));
    }

    [TestMethod]
    public void Other_ids_and_other_mobiles_are_told_apart_by_the_reference()
    {
        Assert.IsFalse(AreaScriptHost.SameCritter(new(3, "oid:ab12"), new(3, "oid:cd34")));
        Assert.IsFalse(AreaScriptHost.SameCritter(new(3, "sector:5:2"), new(7, "sector:5:2")), "a sector object's code is its map's own");
        Assert.IsTrue(AreaScriptHost.SameCritter(new(3, "sector:5:2"), new(3, "sector:5:2")));
    }
}
