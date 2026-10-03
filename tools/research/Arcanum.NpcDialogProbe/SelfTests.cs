using System.Buffers.Binary;
using System.Text;
using Arcanum.Data;

namespace Arcanum.NpcDialogProbe;

internal static class SelfTests
{
    private static int checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception("SyntheticCheck:" + name); checks++; }
    private static void Reject(Action action, string code)
    {
        try { action(); } catch (ProbeFailure e) { Check(e.Message == code, code); return; }
        throw new Exception("Expected:" + code);
    }
    private static void Put(byte[] b, int o, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), v);
    private static byte[] Script()
    {
        var b = new byte[460]; Put(b, 52, 3); Put(b, 56, 3);
        // line0: follows(attachee, triggerer)? goto2 : NEXT; line1: dialog81; line2: dialog44.
        Put(b, 64, 18); b[68] = 1; b[69] = 0; Put(b, 108, 3); b[112] = 3; Put(b, 120, 2);
        Put(b, 240, 4); b[244] = 3; Put(b, 252, 81);
        Put(b, 372, 4); b[376] = 3; Put(b, 384, 44);
        return b;
    }
    private static byte[] Array(int script, uint bitmap = 512)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write((byte)1); w.Write(12); w.Write(1); w.Write(0); w.Write(0u); w.Write(0u); w.Write(script);
        w.Write(2); w.Write(bitmap); w.Write(0u); return stream.ToArray();
    }
    private static byte[] Archive(params string[] names)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s, Encoding.ASCII, true);
        for (var i = 0; i < names.Length; i++) w.Write((byte)7);
        var start = s.Position; w.Write(names.Length);
        foreach (var (name, i) in names.Select((v, i) => (v, i)))
        {
            var b = Encoding.ASCII.GetBytes(name + "\0"); w.Write(b.Length); w.Write(b);
            w.Write(0); w.Write(1u); w.Write(1u); w.Write(1u); w.Write((uint)i);
        }
        var tail = s.Position - start + 28; w.Write(new byte[16]); w.Write(Encoding.ASCII.GetBytes("1TAD"));
        w.Write((uint)names.Sum(n => n.Length + 1)); w.Write((uint)tail); return s.ToArray();
    }
    private static byte[] Object()
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(119); w.Write((short)1); w.Write(new byte[6]); w.Write(451); w.Write(new byte[12]);
        w.Write((short)2); w.Write(new byte[22]); w.Write(16); w.Write((short)1);
        w.Write(1u); w.Write(0u); w.Write(0u); w.Write(77); return s.ToArray();
    }
    public static void Run()
    {
        var script = Script();
        Check(Program.Resolve(script, 0, false, false) == (1, 81), "caller entry not lowest node");
        Check(Program.Resolve(script, 0, true, false) == (2, 44), "conditional branch");
        Reject(() => Program.Resolve(script, 0, null, false), "FollowingQueryFrontier");
        var wrong = script.ToArray(); wrong[68] = 0; Reject(() => Program.Resolve(wrong, 0, false, false), "WrongParticipants");
        var unknown = script.ToArray(); Put(unknown, 64, 901); Reject(() => Program.Resolve(unknown, 0, false, false), "UnsupportedConditionFrontier");
        var effect = script.ToArray(); Put(effect, 152, 99); Reject(() => Program.Resolve(effect, 0, false, false), "UnsupportedActionFrontier");
        var cycle = script.ToArray(); Put(cycle, 120, 0); Reject(() => Program.Resolve(cycle, 0, true, false), "ScriptCycleOrEntryBounds");
        var outside = script.ToArray(); Put(outside, 120, 3); Reject(() => Program.Resolve(outside, 0, true, false), "ScriptCycleOrEntryBounds");
        var capacity = script.ToArray(); Put(capacity, 56, 2); Reject(() => Program.Resolve(capacity, 0, false, false), "ScriptCount");
        Reject(() => Program.Resolve(script[..^1], 0, false, false), "ScriptCount");
        var zero = script.ToArray(); Put(zero, 252, 0); Reject(() => Program.Resolve(zero, 0, false, false), "InvalidDlgNode");
        var local = script.ToArray(); Put(local, 64, 3); local[68] = 3; Put(local, 76, 7);
        Check(Program.Resolve(local, 128, false, false).Node == 44, "LF set");
        Check(Program.Resolve(local, 0, false, false).Node == 81, "LF clear");
        Put(local, 76, 32); Reject(() => Program.Resolve(local, 0, false, false), "InvalidFlag");
        var jilt = script.ToArray(); Put(jilt, 64, 39); Reject(() => Program.Resolve(jilt, 0, false, null), "JiltedQueryFrontier");
        Check(Program.Resolve(jilt, 0, false, true).Node == 44, "Jilted branch");
        // Inactive slots deliberately contain invalid-looking data, which must remain opaque.
        var opaque = script.ToArray(); opaque[250] = 255; Check(Program.Resolve(opaque, 0, false, false).Node == 81, "inactive slots");
        var a = (Dictionary<int, Binding>)new Cursor(Array(684)).Field("ascript")!;
        Check(a.Count == 1 && a[9].Script == 684, "attachment bitmap rank");
        var oneWord = Array(684)[..33]; Put(oneWord, 25, 1);
        Check(((Dictionary<int, Binding>)new Cursor(oneWord).Field("ascript")!).Keys.Single() == 9, "one-word bitmap valid");
        var zeroWords = Array(684); Put(zeroWords, 25, 0); Reject(() => new Cursor(zeroWords).Field("ascript"), "ArrayBitmapLimit");
        Reject(() => new Cursor(Array(-1)).Field("ascript"), "InvalidScriptId");
        Reject(() => new Cursor(Array(7, 0)).Field("ascript"), "ArrayBitmapPopulation");
        var invalidEvent = Array(7, 0); BinaryPrimitives.WriteUInt32LittleEndian(invalidEvent.AsSpan(33), 16);
        Reject(() => new Cursor(invalidEvent).Field("ascript"), "InvalidAttachmentEvent");
        Reject(() => new Cursor(Array(7)[..^1]).Field("ascript"), "TruncatedRecord");
        Reject(() => new Cursor(new byte[] { 2 }).Field("p64"), "InvalidPresence");
        var id = new byte[24]; id[0] = 8; Reject(() => new Cursor(id).Id(false), "MalformedObjectIdentity");
        var layout = new Dictionary<int, Layout> { [16] = new(new[] { "i", "i", "i" }, new[] { 1, 1, 1 }) };
        var obj = Object(); var rows = Program.Objects(obj, layout, false, false);
        Check(rows.Count == 1 && rows[0].PrototypeId == 451 && rows[0].End == 74, "object consumed not scanned");
        Check(Program.SelectNpc(rows, rows[0].ObjectHash).PrototypeId == 451, "object selection production seam");
        Reject(() => Program.SelectNpc(rows.Concat(rows), rows[0].ObjectHash), "MissingAmbiguousOrWrongRoleObject");
        Reject(() => Program.SelectNpc(new[] { rows[0] with { Type = 1 } }, rows[0].ObjectHash), "MissingAmbiguousOrWrongRoleObject");
        Reject(() => Program.SelectNpc(rows, new string('0', 64)), "MissingAmbiguousOrWrongRoleObject");
        Reject(() => Program.ValidateObjectId("not-a-hash"), "InvalidObjectId");
        Reject(() => Program.ValidateObjectId(new string('A', 64)), "InvalidObjectId");
        var inherited = new Dictionary<int, Binding> { [9] = new(0, 0, 684) };
        Check(Program.DialogBinding(null, inherited).Script == 684, "whole array inheritance");
        Reject(() => Program.DialogBinding(new(), inherited), "MissingDialogAttachment");
        Check(Program.DialogBinding(new() { [9] = new(0, 0, 88) }, inherited).Script == 88, "instance whole-field override");
        Reject(() => Program.Objects(obj[..^1], layout, false, false), "TruncatedRecord");
        var badMask = obj.ToArray(); Put(badMask, 58, 2); Reject(() => Program.Objects(badMask, layout, false, false), "ReservedMaskBit");
        var badCount = obj.ToArray(); badCount[56] = 0; Reject(() => Program.Objects(badCount, layout, false, false), "FieldCountMismatch");
        using (var dat = DatArchive.Open(new MemoryStream(Archive("scr\\00684Test.scr"))))
        {
            Check(Program.Unique(dat, "scr\\00684Test.scr", 1).SequenceEqual(new byte[] { 7 }), "production DAT exact");
            Reject(() => Program.Unique(dat, "scr\\00685Test.scr", 1), "MissingOrAmbiguousResource");
            Reject(() => Program.Unique(dat, "../secret", 1), "InvalidResourceSelector");
            Reject(() => Program.Unique(dat, "scr\\00684Test.scr", 0), "PayloadLimit");
        }
        using (var dat = DatArchive.Open(new MemoryStream(Archive("a", "a"))))
            Reject(() => Program.Unique(dat, "a", 1), "MissingOrAmbiguousResource");
        var dlg = DlgReader.Read(Encoding.ASCII.GetBytes("{81}{Synthetic NPC}{}{}{}{}{}\n{82}{Reply}{}{5}{}{44}{}\n{44}{Destination}{}{}{}{}{}"));
        Check(Program.Group(dlg, 81, false).Select(r => r.Id).SequenceEqual(new[] { 82 }), "serialized role group");
        Check(Program.Group(dlg, 81, true).Select(r => r.Id).SequenceEqual(new[] { 82 }), "selected numeric group");
        Reject(() => Program.Group(dlg, 82, false), "InvalidNpcStart");
        Reject(() => Program.Group(dlg, 99, false), "InvalidNpcStart");
        var reorder = DlgReader.Read(Encoding.ASCII.GetBytes("{81}{NPC}{}{}{}{}{}\n{82}{Reply}{}{5}{}{44}{}\n{60}{Stop}{}{}{}{}{}\n{83}{Other}{}{5}{}{44}{}"));
        Check(!Program.Group(reorder, 81, false).Select(r => r.Id).SequenceEqual(Program.Group(reorder, 81, true).Select(r => r.Id)), "ordering policies differ");
        Check(Program.Tokens(Encoding.ASCII.GetBytes("Hi @PCName@ @npcNAME@")).SequenceEqual(new[] { "PC_NAME", "NPC_NAME" }), "case insensitive categories");
        Check(Program.Tokens(Encoding.ASCII.GetBytes("@other@")).Single() == "UNKNOWN_TOKEN", "unknown token frontier");
        Check(Program.NpcVariant(new byte[] { 65 }, new byte[] { 66 }, true, false).Single() == 66, "PC gender selects alternate");
        Check(Program.NpcVariant(new byte[] { 65 }, new byte[] { 66 }, false, false).Single() == 65, "noncritter ordinary variant");
        Check(Program.NpcVariant(new byte[] { 65 }, System.Array.Empty<byte>(), true, false).Length == 0, "missing alternate no fallback");
        Check(Program.Eligibility(-1, 0, 6, 6, false, 4), "positive IQ equality");
        Check(!Program.Eligibility(-1, 0, 6, 5, false, 4), "positive IQ below");
        Check(Program.Eligibility(-1, 0, -4, 4, false, 4), "negative IQ equality");
        Check(!Program.Eligibility(-1, 0, -4, 5, false, 4), "negative IQ above");
        Check(!Program.Eligibility(1, 0, 6, 9, false, 4), "gender mismatch");
        Check(Program.Eligibility(1, 1, -4, 9, true, 4), "dumb override before IQ");
        Check(!Program.Eligibility(-1, 0, -2, 4, true, 4), "dumb low IQ unchanged");
        Reject(() => Program.Eligibility(-1, 0, 0, 4, false, 4), "InvalidPlayerIq");
        var display = Encoding.ASCII.GetBytes("{10}{x}{}{}{}{}{}"); display[5] = 0xc0;
        Check(DlgReader.Read(display).Records[0].Text.Bytes.Single() == 0xc0, "production high display byte preserved");
        Reject(() => Program.Tokens(DlgReader.Read(display).Records[0].Text.Bytes), "TextEncodingUnknown");
        Reject(() => Program.Tokens(Encoding.ASCII.GetBytes("Hi @pcname")), "UnmatchedSubstitution");
        Reject(() => Program.Tokens(new byte[] { 0xc3, 0xa9 }), "TextEncodingUnknown");
        Reject(() => Program.Tokens(new byte[] { 0xff }), "TextEncodingUnknown");
        Check(Program.Obfuscate("Az-by") == "no-oN", "pinned non ROT13 wrap");
        Console.WriteLine($"PASS {checks} literal synthetic checks; no original data accessed");
    }
}
