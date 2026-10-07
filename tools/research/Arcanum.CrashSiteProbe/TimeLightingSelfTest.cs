using System.Buffers.Binary;
using System.Text;
using Arcanum.Data;

namespace Arcanum.CrashSiteProbe;

internal static partial class Program
{
    private static partial class TimeLightingProbe
    {
        // All fixtures are authored here, not extracted. Expectations are literal and
        // independent of evaluator constants; no original colors, strings or pixels.
        public static void SelfTest()
        {
            var checks = 0;
            void Equal<T>(T actual, T expected, string label) { checks++; if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new InvalidOperationException(label); }
            void Sequence(IEnumerable<int> actual, int[] expected, string label) { checks++; if (!actual.SequenceEqual(expected)) throw new InvalidOperationException(label); }
            void Reject(Action action, string label)
            {
                checks++;
                try { action(); }
                catch (Exception e) when (e is InspectionFailure or InvalidDataException or OverflowException or DatException or ObjectDisposedException or NotSupportedException or OperationCanceledException) { return; }
                throw new InvalidOperationException("ACCEPTED_" + label);
            }
            void NoPublication(Action action, string label)
            {
                var original = Console.Out; using var output = new StringWriter();
                try { Console.SetOut(output); Reject(action,label); }
                finally { Console.SetOut(original); }
                Equal(output.ToString(),string.Empty,label+"-stdout");
            }
            byte[] Text(string value) => Encoding.ASCII.GetBytes(value);
            Dictionary<int, string> Table(string row = "10,20,30,90,80,70") => Enumerable.Range(0, 24).ToDictionary(h => h, _ => row);

            // T1: wire independent literal bytes; normalized clock != source carry/calendar.
            Equal(WireClock([2,0,0,0,0x78,0x56,0x34,0x12]), (2u,305419896u), "wire");
            Equal(WireClock(new byte[8]), (0u,0u), "zero-wire");
            Equal(WireClock(Enumerable.Repeat((byte)255,8).ToArray()), (4294967295u,4294967295u), "raw-unsigned");
            Reject(() => WireClock(new byte[7]), "clock-truncated");
            foreach (var (ms, day, hour, isDay) in new (long,long,int,bool)[] {
                (0,0,0,false), (21599999,0,5,false), (21600000,0,6,true), (21600001,0,6,true),
                (64799999,0,17,true), (64800000,0,18,false), (64800001,0,18,false),
                (86399999,0,23,false), (86400000,1,0,false), (86400001,1,0,false), (176400000,2,1,false) })
                Equal(new Clock(ms,0).Query(), (day,hour,isDay), "clock-boundary");
            Equal(SourceCarry(0,86399999,1), (0u,86400000u), "strict-equality");
            Equal(SourceCarry(0,86399999,2), (1u,1u), "strict-after");
            Equal(SourceCarry(0,86399998,1), (0u,86399999u), "strict-before");
            foreach (var (days, d, m, y, cd, cm) in new (uint,int,int,long,int,int)[] {
                (0,1,1,1885,1,1), (29,30,1,1885,1,30), (30,1,2,1885,2,1),
                (359,30,12,1885,12,30), (360,1,1,1886,1,1), (361,2,1,1886,1,2) })
                Equal(SourceCalendar(days,1885), (d,m,y,cd,cm), "calendar-accessors");
            Reject(() => SourceCarry(0,4294967295,1), "wire-sum-overflow");
            Reject(() => SourceCarry(4294967295,86400000,1), "day-overflow");
            Reject(() => new Clock(-1,0).Query(), "negative-time");

            // T2: explicit proposal admission, not a ticking service or callback-pause proof.
            var start = new Clock(21599999,7); var paused = start.Advance(100000000,true);
            Equal(paused,start,"pause-future"); Equal(start.Advance(0,false),start,"zero-idempotence");
            Equal(start.Advance(1,false),new Clock(21600000,8),"resume");
            Equal(start.Query(),start.Query(),"query-idempotence");
            Equal(new Clock(0,0).Advance(176400000,false),new Clock(176400000,1),"jump");
            Reject(() => start.Advance(-1,false),"negative-duration");
            Reject(() => new Clock(long.MaxValue,0).Advance(1,false),"time-overflow");
            Reject(() => new Clock(0,long.MaxValue).Advance(1,false),"version-overflow");
            foreach (var (delta,gate,tb,expected) in new (int,bool,bool,(int,int,int))[] {
                (4,false,false,(0,0,0)),(5,false,false,(5,40,40)),(250,false,false,(250,2000,2000)),
                (251,false,false,(250,2000,2000)),(100,true,false,(100,0,0)),(100,false,true,(100,0,800)),(100,true,true,(100,0,0)) })
                Equal(SourceElapsed(delta,gate,tb),expected,"source-admission");
            Reject(() => SourceElapsed(-1,false,false),"negative-elapsed");
            Equal(RelativeDeadline(21599999,1),21600000L,"initial-relative");
            Equal(RelativeDeadline(21600001,3600000),25200001L,"relative-not-aligned");
            Equal(RelativeDeadline(9,0),10L,"zero-delay-one");
            Equal(AlreadyDue(10,10),true,"due-while-clock-frozen"); Equal(AlreadyDue(9,10),false,"not-due");
            Reject(() => RelativeDeadline(long.MaxValue,1),"deadline-overflow");

            // T3-T4: channels, sentinel/default/override and noncyclic strict gap proposal.
            Equal(Resolve(7,0),7,"sentinel"); Equal(Resolve(7,9),9,"override");
            Equal(Resolve(8,0),8,"changed-default"); Equal(Resolve(8,9),9,"stable-override");
            Reject(() => Resolve(7,null),"absent-metadata"); Reject(() => Resolve(7,-1),"negative-scheme"); Reject(() => Resolve(0,0),"missing-default");
            var first = new Scheme(Table());
            for (var h=0;h<24;h++) Sequence(first.At(h),[10,20,30,90,80,70],"channel-order");
            var returned = first.At(0); Reject(() => ((IList<int>)returned)[0]=222,"immutable-snapshot");
            var gaps = Table(); gaps[0]="11,20,30,90,80,70"; gaps[1]=""; gaps[2]=""; gaps[3]="21,10,30,60,80,71";
            gaps[4]=""; gaps[5]="7,17,30,89,80,65";
            var normalized = new Scheme(gaps);
            Sequence(normalized.At(1),[14,17,30,80,80,70],"positive-negative-truncation");
            Sequence(normalized.At(2),[17,14,30,70,80,70],"consecutive-gap");
            Sequence(normalized.At(4),[14,13,30,74,80,68],"separated-gap-independent-anchors");
            Sequence(new Scheme(Table("1,2,3,4,5,6")).At(1),[1,2,3,4,5,6],"replacement-fresh");
            Sequence(first.At(1),[10,20,30,90,80,70],"previous-detached");
            foreach (var change in new Action<Dictionary<int,string>>[] {
                t=>t[0]="",t=>t[23]="",t=>{for(var i=0;i<24;i++)t[i]="";}, t=>t.Remove(9),
                t=>t[4]="1,2,3",t=>t[4]="1,2,3,4,5,6,7",t=>t[4]="x,2,3,4,5,6",
                t=>t[4]="-1,2,3,4,5,6",t=>t[4]="256,2,3,4,5,6",t=>t[4]="2147483648,2,3,4,5,6" })
            { var bad=Table(); change(bad); Reject(()=>new Scheme(bad),"scheme-malformed"); }
            Reject(()=>first.At(-1),"hour-negative"); Reject(()=>first.At(24),"hour-too-large");
            Equal(MapDefault(Text("LightScheme:7\n")),7,"map-default");
            foreach(var bad in new[]{"","LightScheme:x","LightScheme:0","LightScheme:7\nLightScheme:8"}) Reject(()=>MapDefault(Text(bad)),"map-default-bad");
            Equal(Mes(Text("; comment\n{7}{fixture}\n// comment\n{8}{}"))[8],"","empty-key");
            Equal(Mes(Text("{0}{1,2,3,4,5,6} 12AM\n{1}{} 1PM"))[1],"","bounded-hour-annotation");
            foreach(var bad in new[]{"", "{x}{v}","{1}{v", "{1}{v}{1}{w}","junk{1}{v}","{1}wrong"}) Reject(()=>Mes(Text(bad)),"mes-malformed");
            foreach(var bad in new[]{"../x","x/y","x\\y","","/absolute","x:drive"}) Reject(()=>Token(bad),"unsafe-token");
            WithSyntheticArchive(CreateSyntheticDat(new SyntheticDatEntry(NameBytes("rules/fixture.mes"),Text("{1}{v}"))), (a,_)=>Equal(Unique(a,"RULES/FIXTURE.MES").Ordinal,0,"unique-resource"));
            WithSyntheticArchive(CreateSyntheticDat(),(a,_)=>Reject(()=>Unique(a,"rules/fixture.mes"),"missing-resource"));
            WithSyntheticArchive(CreateSyntheticDat(new SyntheticDatEntry(NameBytes("rules/fixture.mes"),[1]),new SyntheticDatEntry(NameBytes("RULES/FIXTURE.MES"),[2])),(a,_)=>Reject(()=>Unique(a,"rules/fixture.mes"),"ambiguous-resource"));

            // T5: literal wire pattern independent of parser field definitions.
            var wire=Convert.FromHexString("01000000000000000200000000000000FDFFFFFF070000000B00008004030281112233445566778899AABBCCDDEEFF00");
            Equal(ReadLight(wire),new Light(1,2,-3,7,2147483659,2164392708,17,34,51,68,2289526357,3434850969,16772829),"all-light-fields");
            Reject(()=>ReadLight(wire[..47]),"light-truncated"); Reject(()=>ReadLight(new byte[49]),"light-extra");
            Equal(StationaryCount(new byte[4]),0,"zero-lights");
            var many=new byte[49156]; many[1]=4; Equal(StationaryCount(many),1024,"light-cap-equality");
            many[0]=1; Reject(()=>StationaryCount(many),"light-cap-plus-one");
            Reject(()=>StationaryCount(new byte[3]),"count-truncated"); Reject(()=>StationaryCount([1,0,0,0]),"record-truncated");
            Reject(()=>StationaryCount([255,255,255,255]),"negative-count");
            Reject(()=>CountBytes(int.MaxValue,48,int.MaxValue),"checked-multiply");
            // Independently fixed metadata words: town=9, aptitude=777, scheme=17.
            var sector = CreateFramedFixture(0,0xAA0003);
            BinaryPrimitives.WriteInt32LittleEndian(sector.AsSpan(16412),9);
            BinaryPrimitives.WriteInt32LittleEndian(sector.AsSpan(16416),777);
            BinaryPrimitives.WriteInt32LittleEndian(sector.AsSpan(16420),17);
            var framing = SectorFraming.Parse(sector,0);
            Equal(Metadata(sector,framing),((int?)9,(int?)17),"metadata-slot-not-aptitude");
            Reject(()=>Metadata(sector[..16435],framing),"metadata-truncated");
            var oldSector=CreateFramedFixture(0,0xAA0001);
            Equal(Metadata(oldSector,SectorFraming.Parse(oldSector,0)),((int?)null,(int?)null),"metadata-absent");
            var directObject=new InstanceInfo(0,ObjectId.A(7001),3,null,null,null,null,null) {NumericFields=new Dictionary<int,int>{{13,0}}};
            var prototype=new PrototypeInfo(ObjectId.A(7001),3,0,0) {NumericFields=new Dictionary<int,int>{{12,18},{13,-1}}};
            Equal(EffectiveNumeric(directObject,prototype)[13],0,"primary-explicit-zero");
            Equal(EffectiveNumeric(directObject,prototype)[12],18,"primary-flags-default");
            Equal(EffectiveNumeric(directObject with{NumericFields=new Dictionary<int,int>()},prototype)[13],-1,"primary-absent-default");
            var absent=OverlayField([],[],16,false); var nullArray=OverlayField([],[],16,true);
            Equal(absent.Presence,"ABSENT","array-absent"); Equal(nullArray.Presence,"NULL","array-null");
            byte[] empty= [1,4,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0];
            var emptyArray=OverlayField(empty,[new ArraySpan(16,0,21,4,0,13,1)],16,true); Equal(emptyArray.Presence,"EMPTY","array-empty");
            byte[] values=[1,4,0,0,0,2,0,0,0,0,0,0,0,0,0,0,0,255,255,255,255,1,0,0,0,5,0,0,0];
            var projected=OverlayField(values,[new ArraySpan(16,0,29,4,2,21,1)],16,true);
            Equal(projected.Values[0],0u,"explicit-zero"); Equal(projected.Values[2],4294967295u,"invalid-art");
            Equal(EffectiveOverlay(absent,projected),projected,"inherit-prototype"); Equal(EffectiveOverlay(nullArray,projected),nullArray,"null-overrides"); Equal(EffectiveOverlay(emptyArray,projected),emptyArray,"empty-overrides");
            Reject(()=>((IDictionary<int,uint>)projected.Values).Add(1,3),"overlay-immutable");
            values[25]=16; Reject(()=>OverlayField(values,[new ArraySpan(16,0,29,4,2,21,1)],16,true),"overlay-index");

            // T6: literal flags and full distinct load/hour-refresh decision table.
            Equal(Contribution(1),"OFF","off"); Equal(Contribution(3),"OFF","off-dark"); Equal(Contribution(2),"SUBTRACT","dark"); Equal(Contribution(0),"ADD","add");
            Equal(Tint(8),"INDOOR","indoor-tint"); Equal(Tint(16),"OUTDOOR","outdoor-tint"); Equal(Tint(24),"INDOOR","both-tint"); Equal(Tint(0),"CUSTOM","custom-tint");
            Equal(UnknownFlags(0x08000020),0x08000020u,"named-unknown-flags"); Equal(UnknownFlags(0xf000009f),0u,"recognized-flags");
            foreach(var (refresh,type,of,sf,tile,h,expected) in new (bool,int,uint,uint,int,int,(bool,bool,bool))[] {
                (false,3,0,4,1,6,(false,true,true)),(false,3,2,4,1,18,(true,false,true)),
                (false,3,2,4,0,6,(true,false,true)),(false,3,4096,4,1,6,(false,false,false)),
                (false,3,0,1028,1,6,(false,true,true)),(false,2,0,4,1,6,(true,false,false)),
                (false,3,2,0,1,6,(true,true,false)),(true,3,0,4,1,6,(true,true,true)),
                (true,3,2,4,1,6,(false,true,false)),(true,3,2,4,1,18,(true,false,true)),
                (true,3,2,4,0,6,(true,false,true)),(true,3,4096,4,1,6,(true,true,true)),
                (true,3,0,1028,1,6,(false,false,false)),(true,2,0,4,1,6,(false,false,false)) })
                Equal(ObjectGate(refresh,type,of,sf,tile,h),expected,"load-hour-gate");

            // T7: shared world support, not a renderer/3x3 radius certificate.
            var aKey=new Emitter("map1/sector1/record0",1,63,4); var bKey=new Emitter("map1/sector2/record0",2,64,2);
            var orderA=Neighbours([aKey,bKey,aKey],new HashSet<int>{1,2},new HashSet<int>{1,2});
            var orderB=Neighbours([bKey,aKey],new HashSet<int>{1,2},new HashSet<int>{1,2});
            Equal(orderA.Count,2,"deduplicate"); Equal(orderA.SequenceEqual(orderB),true,"stable-order-query");
            Reject(()=>Neighbours([aKey],new HashSet<int>{1,2},new HashSet<int>{1}),"missing-neighbour");
            Reject(()=>Neighbours([aKey,aKey with{Width=8}],new HashSet<int>{1},new HashSet<int>{1}),"identity-conflict");
            Equal(Footprint(64,30,4,6,1,2),(63,28,67,34),"cross-seam-footprint"); Reject(()=>Footprint(int.MaxValue,0,4,1,0,0),"footprint-overflow");
            foreach(var (art,tile,ground,on,want) in new (int,int,bool,bool,string)[] {
                (0,0,false,true,"INDOOR"),(0,1,false,true,"OUTDOOR"),(11,0,false,true,"INDOOR"),
                (1,0,false,true,"OUTDOOR"),(2,0,false,true,"OUTDOOR"),(3,0,false,true,"OUTDOOR"),
                (4,0,false,true,"OUTDOOR"),(7,0,false,true,"OUTDOOR"),(10,0,false,true,"OUTDOOR"),
                (12,0,false,true,"OUTDOOR"),(13,0,false,true,"OUTDOOR"),(6,0,true,true,"OUTDOOR"),
                (6,0,false,true,"NONE"),(5,0,false,true,"NONE"),(8,0,false,true,"NONE"),(9,0,false,true,"NONE"),(0,0,false,false,"NONE") })
                Equal(Layer(art,tile,ground,on),want,"layer-palette");
            Equal(Compose(250,255,20,false,false),255,"saturating-add"); Equal(Compose(5,255,20,true,false),0,"saturating-sub");
            Equal(Compose(80,127,100,false,false),129,"quantized-tint"); Equal(Compose(80,255,255,false,true),80,"key-transparency");

            // T8: real helpers' explicit policies plus selected input lifecycle.
            foreach(var cap in new long[]{536870912,67108864,131072,4096,1048576,32,8192,1024})
            { Admit(cap,cap); checks++; Reject(()=>Admit(cap+1,cap),"cap-plus-one"); Reject(()=>Admit(-1,cap),"negative-budget"); }
            Equal(Options().MaxArchiveBytes,536870912u,"archive-policy"); Equal(Options().MaxIndexBytes,67108864u,"index-policy"); Equal(Options().MaxTotalNameBytes,67108864u,"names-policy");
            Equal(Options().MaxEntryCount,131072,"entries-policy"); Equal(Options().MaxNameBytes,4096u,"name-policy"); Equal(Options().MaxStoredEntryBytes,1048576u,"stored-policy"); Equal(Options().MaxDecodedEntryBytes,1048576u,"decoded-policy");
            Equal(Mes(Text(string.Concat(Enumerable.Range(0,8192).Select(i=>$"{{{i}}}{{}}")))).Count,8192,"mes-cap-equality");
            Reject(()=>Mes(Text(string.Concat(Enumerable.Range(0,8193).Select(i=>$"{{{i}}}{{}}")))),"mes-cap-plus-one");
            Equal(Neighbours(Enumerable.Repeat(aKey,4096),new HashSet<int>{1},new HashSet<int>{1}).Count,1,"emitter-admission-before-dedup");
            using(var canceled=new CancellationTokenSource()) { canceled.Cancel(); NoPublication(()=>Run([],canceled.Token),"canceled-no-publication"); }
            NoPublication(()=>Run(["--unknown","fixture"]),"unsupported-arguments");
            Equal(Diagnostic(new IOException("private/path/payload")),"TIME_LIGHTING_INSPECTION_FAILED","sanitized-error");
            Reject(()=>Neighbours(Enumerable.Repeat(aKey,4097),new HashSet<int>{1},new HashSet<int>{1}),"emitter-cap-plus-one");
            var root=Path.Combine(Path.GetTempPath(),"time-lighting-synthetic-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var path=Path.Combine(root,"input"); File.WriteAllBytes(path,[1,2,3,4]);
                using(var input=new Input(path,4)) { Equal(input.Bytes().SequenceEqual(new byte[]{1,2,3,4}),true,"input-bytes"); input.Check(); checks++; File.WriteAllBytes(path,[4,3,2,1]); Reject(input.Check,"changed-input"); }
                File.WriteAllBytes(path,[1,2,3,4]);
                using(var input=new Input(path,4)) { File.Move(path,path+".old"); File.WriteAllBytes(path,[1,2,3,4]); Reject(input.Check,"replaced-same-content"); }
                Reject(()=>new Input(path,3),"input-cap");
                var disposed=new Input(path,4); disposed.Dispose(); Reject(disposed.Check,"disposed-source");
                // Detached pure channel description survives source-owner disposal.
                Sequence(returned,[10,20,30,90,80,70],"detached-lifetime");
                NoPublication(()=>Run(["--module",path,"--prototype-root",root]),"failed-inspection-no-publication");
            }
            finally { Directory.Delete(root,true); } // Only this newly owned synthetic tree.
            Console.WriteLine($"time-lighting-self-test: PASS checks={checks} matrix=T1-T8 synthetic-only original-runtime=NOT_VERIFIED");
        }
    }
}
