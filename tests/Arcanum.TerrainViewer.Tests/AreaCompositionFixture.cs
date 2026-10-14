using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using System.Security.Cryptography;

namespace Arcanum.TerrainViewer.Tests;

// Independently authored numeric source inputs and literal normal-branch oracles.
// Shared by Core tests and the real Godot compositor; no rendered golden output,
// projection/depth helper or production constant computes an expected result.
internal static class AreaCompositionFixture
{
    internal const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal static readonly AreaObjectKey ActorKey = new("controlled-session", 5051, AreaSourceKind.Mobile, null, int.MaxValue, 0, "controlled-session", 0);
    internal sealed record Case(string Name, int X, int Y, int OffsetX, int OffsetY,
        TerrainRect ActorRectangle, TerrainRect CanopyRectangle, string Order, string Winner,
        int Width = 160, int Height = 160, int HotX = 40, int HotY = 80, int Scale = 100,
        uint CanopyFlags = 0, uint SceneryFlags = 0);

    internal static IReadOnlyList<Case> Cases { get; } = new Case[]
    {
        new("same-behind",0,0,40,-40,new(36,-23,9,7),new(0,-60,160,160),"B,actor,A","A"),
        new("same-between",0,0,40,-20,new(36,-3,9,7),new(0,-60,160,160),"B,actor,A","A"),
        new("same-front",0,0,40,10,new(36,27,9,7),new(0,-60,160,160),"B,A,actor","actor"),
        new("same-tie",0,0,0,0,new(-4,17,9,7),new(0,-60,160,160),"B,A,actor","actor-right"),
        new("row-behind-anchor-ahead",2,-1,160,0,new(36,37,9,7),new(0,-60,160,160),"actor,B,A","A"),
        new("row-ahead-anchor-behind",-2,1,-80,0,new(36,-3,9,7),new(0,-60,160,160),"B,A,actor","actor"),
        new("column-behind",-1,0,0,0,new(36,-3,9,7),new(0,-60,160,160),"actor,B,A","A"),
        new("column-ahead",1,0,80,0,new(36,37,9,7),new(0,-60,160,160),"B,A,actor","actor"),
        new("canopy-size",0,0,40,-40,new(36,-23,9,7),new(0,-60,320,320),"B,actor,A","A",Width:320,Height:320),
        new("canopy-hotspot",0,0,40,-40,new(36,-23,9,7),new(-40,-80,160,160),"B,actor,A","A",HotX:80,HotY:100),
        new("canopy-scale",0,0,40,-40,new(36,-23,9,7),new(10,-40,120,120),"B,actor,A","A",Scale:75),
        new("canopy-shrunk",0,0,40,-20,new(36,-3,9,7),new(20,-20,80,80),"B,actor,A","A",CanopyFlags:0x80),
        new("signed-hotspot",0,-1,100,70,new(56,67,9,7),new(52,60,160,160),"actor,B,A","A",HotX:-12,HotY:-40),
        new("flat-before-upright",0,0,40,-40,new(36,-23,9,7),new(0,-60,160,160),"A,B,actor","actor",CanopyFlags:4),
        new("flat-under-all",0,0,40,-40,new(36,-23,9,7),new(0,-60,160,160),"B,A,actor","actor",CanopyFlags:4,SceneryFlags:4),
        new("hidden",0,0,40,-40,new(36,-23,9,7),new(0,-60,160,160),"B,actor","actor",CanopyFlags:0x100),
        new("inventory",0,0,40,-40,new(36,-23,9,7),new(0,-60,160,160),"B,actor","actor",CanopyFlags:0x1000),
        new("invisible",0,0,40,-40,new(36,-23,9,7),new(0,-60,160,160),"B,actor","actor",CanopyFlags:0x200),
        new("destroyed",0,0,40,-40,new(36,-23,9,7),new(0,-60,160,160),"B,actor","actor",CanopyFlags:1),
        new("off",0,0,40,-40,new(36,-23,9,7),new(0,-60,160,160),"B,actor","actor",CanopyFlags:2)
    };

    internal static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
    internal static AreaObjectRecord Body(int index, uint flags = 0, int scale = 100, long scenery = 0, uint blit = 0)
    {
        var key = new AreaObjectKey(Digest,5051,AreaSourceKind.Static,100,index,index*100,Digest,100);
        var fields = new[] { Field(18,flags),Field(8,blit),Field(9,0xFFFFFF),Field(10,255),Field(11,scale),Field(36,scenery) };
        return new(key,119,3,1,Digest,Digest,Digest,Digest,AreaScope.Core,null,92928,82560,
            Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,fields,fields,true,true,true);
    }
    internal static AreaVisualRecord Visual(AreaObjectRecord body, int width, int height, int hotX, int hotY, int ox = 0, int oy = 0, bool mirrored = false)
        => new(new(AreaSourceKind.Static,new(Digest,5051,100),body.Key.SourceIndex,Digest,Object:body.Key),0x40000000,0,
            new(new(AreaSourceRole.Resource2),body.Key.SourceIndex+1,0,1,1,0,Digest),Digest,1,0,0,0,0,0,0,mirrored,
            width,height,hotX,hotY,777,-999,ox,oy,100,1,width*height*4,
            Convert.ToHexString(SHA256.HashData(Pixels(width,height,body.Key.SourceIndex == 0))).ToLowerInvariant(),"synthetic");

    internal static byte[] Pixels(int width, int height, bool canopy)
    {
        var bytes = new byte[checked(width*height*4)];
        for (var y=0;y<height;y++) for (var x=0;x<width;x++)
        {
            var at=(y*width+x)*4;
            bytes[at]=canopy ? (byte)10 : (byte)18;
            bytes[at+1]=canopy ? (byte)20 : (byte)197;
            bytes[at+2]=canopy ? (byte)200 : (byte)71;
            bytes[at+3]=x<2 || y<2 || x>=width-2 || y>=height-2 ? (byte)0 : (byte)255;
            if (canopy && x is >=10 and <14 && y is >=10 and <14)
            { bytes[at]=250; bytes[at+1]=150; bytes[at+2]=50; }
        }
        return bytes;
    }

    internal static (AreaScenePlan Plan, AreaObjectRecord A, AreaObjectRecord B, AreaVisualRecord Av, AreaVisualRecord Bv) Create(Case item)
    {
        var a = Body(0,item.CanopyFlags,item.Scale);
        var b = Body(1,item.SceneryFlags,scenery:item.SceneryFlags == 4 ? 512 : 0);
        var av = Visual(a,item.Width,item.Height,item.HotX,item.HotY);
        var bv = Visual(b,160,200,80,100,40,-40);
        // Reverse enumeration is intentional: the oracle cannot pass through
        // caller ordering or texture size. Identity remains distinct for null IDs.
        return (AreaScenePlan.Create([], [a,b], [bv,av]),a,b,av,bv);
    }
    internal static ActorDiagnosticPlacementInput Placement(Case item) => new(item.X,item.Y,0,0,item.OffsetX,item.OffsetY,
        ActorDiagnosticRenderBranch.NormalUncached,100,false,ActorDiagnosticAction.Idle,0,0,9,7,4,3,0,0);
    internal static string Name(AreaDrawCommand command) => command.Identity == ActorKey ? "actor"
        : command.Identity?.SourceIndex == 0 ? "A" : "B";
    internal static string Order(AreaScenePlan plan, ActorDiagnosticPlacementInput input, ActorDiagnosticDrawCommand actor)
        => string.Join(',',plan.Commands.Where(command => command.Phase < 4)
            .Concat(AreaScenePlan.UprightOrder(plan.Commands,input,actor,ActorKey)).Select(Name));
}
