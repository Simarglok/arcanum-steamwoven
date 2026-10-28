using System.Text;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

/// <summary>ARC-RENDER-034: the terrain types' editor colours (message 100 + type, <c>R,G,B</c>) of an invented terrain.mes.</summary>
[TestClass]
public sealed class TerrainEditorColourTests
{
    [TestMethod]
    public void Colours_are_read_by_type_and_a_malformed_one_gives_none()
    {
        var mes = "{0}{grass}{1}{water /b}{2}{rock}{3}{sand}{100}{ 10,20, 30}{101}{0,0,255}{102}{1,2}{103}{256,0,0}\r\n";
        var file = MesFile.Parse(Encoding.ASCII.GetBytes(mes)).File!;
        var colours = TerrainEditorColours.Read(file, 5);
        Assert.AreEqual(5, colours.Count);
        Assert.AreEqual(new TerrainColour(10, 20, 30), colours[0], "white space around the numbers");
        Assert.AreEqual(new TerrainColour(0, 0, 255), colours[1]);
        Assert.IsNull(colours[2], "two numbers");
        Assert.IsNull(colours[3], "a value above 255");
        Assert.IsNull(colours[4], "no message");
        Assert.AreEqual(0, TerrainEditorColours.Read(file, 0).Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TerrainEditorColours.Read(file, TerrainTypeTable.MaxTypes + 1));
        Assert.IsNull(TerrainEditorColours.Read(MesFile.Parse(Encoding.ASCII.GetBytes("{100}{-1,2,3}\r\n")).File!, 1)[0], "a sign");
    }
}
