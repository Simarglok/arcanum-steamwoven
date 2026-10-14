using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaNearestAxisTests
{
    [TestMethod]
    public void HalfZoomTiesHaveLiteralDiscreteReflectionAndHalfOpenOwnership()
    {
        var normal = new AreaNearestAxis(100,.5f,0,8,2,8,false);
        var reflected = new AreaNearestAxis(100,.5f,0,8,2,8,true);
        CollectionAssert.AreEqual(new[]{-1,3,5,7,9,-1}, Enumerable.Range(99,6).Select(normal.SourceIndex).ToArray());
        CollectionAssert.AreEqual(new[]{-1,8,6,4,2,-1}, Enumerable.Range(99,6).Select(reflected.SourceIndex).ToArray());
        Assert.AreEqual(3,new AreaNearestAxis(99.75f,.5f,0,8,2,8,false).SourceIndex(100));
        Assert.AreEqual(2,new AreaNearestAxis(100.5f,.5f,0,8,2,8,false).SourceIndex(100));
    }

    [TestMethod]
    public void SignedOriginsAndNonUnitDestinationScaleHaveIndependentLiteralIndices()
    {
        var axis=new AreaNearestAxis(-3.25f,2,-7,13,4,9,false);
        CollectionAssert.AreEqual(new[]{4,5,6,7,8,9,10,11,12},
            new[]{-17,-14,-11,-8,-5,-2,1,4,7}.Select(axis.SourceIndex).ToArray());
        Assert.AreEqual(-1,axis.SourceIndex(-18)); Assert.AreEqual(-1,axis.SourceIndex(9));
    }

    [TestMethod]
    public void ActualSinglePrecisionInputsAreNotDecimalApproximations()
    {
        // 0.1f lies strictly ABOVE rational 1/10. At center0.5, five source
        // texels therefore select3, not the decimal-tie index4.
        Assert.AreEqual(3,new AreaNearestAxis(.1f,1,0,1,0,10,false).SourceIndex(0));
        Assert.AreEqual(6,new AreaNearestAxis(.1f,1,0,1,0,10,true).SourceIndex(0));
    }

    [TestMethod]
    public void InvalidTransformsAndUnencodableSourceDomainsFail()
    {
        foreach(var zoom in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
            Assert.ThrowsExactly<InvalidOperationException>(()=>new AreaNearestAxis(0,zoom,0,8,0,8,false));
        Assert.ThrowsExactly<InvalidOperationException>(()=>new AreaNearestAxis(0,1,0,0,0,8,false));
        Assert.ThrowsExactly<InvalidOperationException>(()=>new AreaNearestAxis(0,1,0,8,65530,8,false));
    }
}
