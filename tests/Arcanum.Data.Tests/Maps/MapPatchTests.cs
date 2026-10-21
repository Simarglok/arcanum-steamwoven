using System.Collections.ObjectModel;
using System.Text;
using Arcanum.Data.Maps;
using Arcanum.Data.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Maps;

/// <summary>ARC-DATA-028: map.patch reading and application on synthetic lines and records (no original data).</summary>
[TestClass]
public sealed class MapPatchTests
{
    private const string Id = "G_01234567_89AB_CDEF_0123_456789ABCDEF";
    private static readonly Guid Guid = new([0x67, 0x45, 0x23, 0x01, 0xAB, 0x89, 0xEF, 0xCD, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF]);
    private const int Portal = 1, Container = 2, Weapon = 5, Armor = 7, Npc = 16;

    private static MapPatchFile Parse(string text) => MapPatchFile.Parse(Encoding.Latin1.GetBytes(text));

    [TestMethod]
    public void Lines_are_read_as_the_original_reads_them()
    {
        var file = Parse($"# comment\r\n\r\n{Id}:obj_flag OF_OFF\r\nG_bad:faction 1\r\nH_handle\r\nA_00001\r\n{Id} no colon\n{Id}:faction 7");
        Assert.AreEqual(Guid, file.Lines[0].ObjectId, "the first three groups in the Windows memory layout, as object files store them");
        CollectionAssert.AreEqual(new[] { 3, 7, 8 }, file.Lines.Select(l => l.Line).ToArray());
        Assert.AreEqual("obj_flag OF_OFF", file.Lines[0].Attributes);
        Assert.IsNull(file.Lines[1].Attributes);
        Assert.AreEqual("faction ", file.Lines[2].Attributes, "a final line without a newline loses its last character");
        CollectionAssert.AreEqual(new[] { "MalformedObjectId 4", "MalformedObjectId 5", "UnsupportedObjectId 6" }, file.Issues.Select(i => $"{i.Code} {i.Line}").ToArray());
        Assert.IsTrue(file.HasUnmodelledLines);
        Assert.AreEqual(2, file.For(Guid).Count(l => l.Attributes is not null));

        // A read of 4,096 characters or more overruns the original's 4,096-byte buffer, comment or not: not modelled.
        foreach (var head in new[] { Id + ":", "#" })
        {
            var overrun = Parse(Id + ":faction 1\n" + head + new string(' ', MapPatchFile.OriginalLineStorage - head.Length) + "\n" + Id + ":faction 2\n");
            Assert.AreEqual(1, overrun.Lines.Count);
            Assert.AreEqual((MapPatchIssueCode.LineOverflowsBuffer, 2), (overrun.Issues.Single().Code, overrun.Issues.Single().Line));
            Assert.IsTrue(overrun.HasUnmodelledLines);
        }
        Assert.AreEqual(2, Parse(Id + ":faction 1\n#" + new string(' ', MapPatchFile.OriginalLineStorage - 3) + "\n" + Id + ":faction 2\n").Lines.Count);
        // Each GUID group is read by strtoul after a check of its first character, so "0x" may open a longer group.
        Assert.AreEqual(new Guid([0x67, 0x45, 0x23, 0x00, 0xAB, 0x89, 0xEF, 0xCD, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF]),
            Parse("G_0x234567_89AB_CDEF_0123_456789ABCDEF:x\n").Lines.Single().ObjectId);

        // Bytes the reader treats by source make the file unmodelled; a GUID needs every hex digit.
        foreach (var odd in new[] { Id + ":faction 1\r", Id + ":fac\0tion 1\n", "\x1A" })
            Assert.AreEqual(MapPatchIssueCode.UnsupportedByte, Parse(odd).Issues.Single().Code, odd);
        Assert.AreEqual(MapPatchIssueCode.MalformedObjectId, Parse(Id.Replace('F', 'G') + ":x\n").Issues.Single().Code);
        Assert.AreEqual(0, Parse("").Lines.Count);
        // A one-character final line is empty after the cut: malformed, not an error.
        Assert.AreEqual((MapPatchIssueCode.MalformedObjectId, 2), (Parse(Id + ":x\n ").Issues.Single().Code, Parse(Id + ":x\n ").Issues.Single().Line));
        // The last two groups are read in two-digit units, which cannot hold "0x".
        Assert.AreEqual(MapPatchIssueCode.MalformedObjectId, Parse("G_01234567_89AB_CDEF_0x23_456789ABCDEF:x\n").Issues.Single().Code);
        Assert.AreEqual(MapPatchIssueCode.MalformedObjectId, Parse("G_01234567_89AB_CDEF_0123_0x6789ABCDEF:x\n").Issues.Single().Code);
    }

    // ---- application ----

    private static ObjectWireRecord Record(int type, params (int Slot, ObjectWireValue Value)[] fields)
    {
        var id = new ObjectWireId(2, null, new string('a', 64), new ObjectEntityId(2, 1, 2, 0));
        return new(type, new ObjectWireId(1, 7001, new string('b', 64), new ObjectEntityId(1, 7001, 0, 0)), id, 100, new string('c', 64), new string('d', 64),
            new ReadOnlyDictionary<int, ObjectWireValue>(fields.ToDictionary(f => f.Slot, f => f.Value)));
    }
    private static ObjectWireValue Int(int value) => new(ObjectWirePresence.Value, value, []);
    private static ObjectWireValue Array(params (int Key, long[] Words)[] elements) =>
        new(ObjectWirePresence.Array, null, elements.Select(e => new ObjectWireElement(e.Key, e.Words)).ToArray());

    private static MapPatchApplier.Result Apply(ObjectWireRecord record, ObjectWireRecord prototype, string attributes, Func<int, MapPatchScriptHeader?>? scripts = null,
        bool scriptZeroClears = false) =>
        MapPatchApplier.Apply(record, prototype, [new MapPatchLine(9, Guid, attributes)], scripts ?? (_ => null), scriptZeroClears);

    private static long? Number(MapPatchApplier.Result result, int slot) => result.Record.Fields.TryGetValue(slot, out var v) ? v.Number : null;

    [TestMethod]
    public void Scalar_attributes_write_the_fields_of_the_objects_type()
    {
        var npc = Record(Npc); var proto = Record(Npc);
        var result = Apply(npc, proto, "INTERNAL_NAME 12 known_name 34 origin 5 faction 6 level_scheme 7 daystand 10 20 nightstand -1 3 magic_hit_adjust 9 notify_npc 8");
        Assert.AreEqual((12L, 34L, 5L, 6L, 7L), (Number(result, 21), Number(result, 22), Number(result, 80), Number(result, 81), Number(result, 63)));
        Assert.AreEqual(10L | (20L << 32), Number(result, 78));
        Assert.AreEqual(-1L | (3L << 32), Number(result, 79), "strtoll values combined as the original's location");
        Assert.IsNull(Number(result, 62), "a weapon field on an NPC is not written");
        CollectionAssert.AreEqual(new[] { 9 }, result.Application.Lines.ToArray());
        Assert.AreEqual(0, result.Application.Unmodelled.Count);

        Assert.AreEqual(4L, Number(Apply(Record(Portal), Record(Portal), "notify_npc 4"), 39));
        Assert.AreEqual(4L, Number(Apply(Record(Container), Record(Container), "notify_npc 4"), 42));
        var weapon = Apply(Record(Weapon), Record(Weapon), "magic_hit_adjust -3 magic_tech_complexity 40 faction 1");
        Assert.AreEqual((-3L, 40L, (long?)null), (Number(weapon, 62), Number(weapon, 45), Number(weapon, 81)));
        // An absent value reads 0; an unknown key stops the line.
        var stopped = Apply(Record(Npc), proto, "faction bogus 1 origin 2");
        Assert.AreEqual(0L, Number(stopped, 81));
        Assert.IsNull(Number(stopped, 80));
        Assert.AreEqual(0L, Number(Apply(Record(Npc), proto, "faction"), 81));
    }

    [TestMethod]
    public void Flags_set_or_clear_by_name_from_the_effective_value()
    {
        var proto = Record(Portal, (18, Int(0x1000)), (36, Int(0b10001)));
        var result = Apply(Record(Portal), proto, "portal_flag !OPF_LOCKED portal_flag !opf_always_locked obj_flag of_off portal_flag OPF_NOT_A_FLAG");
        Assert.AreEqual(0L, Number(result, 36), "cleared from the prototype's value");
        Assert.AreEqual(0x1002L, Number(result, 18));
        var item = Apply(Record(Armor, (36, Int(0))), Record(Armor), "item_flag OIF_IDENTIFIED item_flag OIF_NO_NPC_PICKUP critter_flag OCF_NO_FLEE");
        Assert.AreEqual((1L << 0) | (1L << 19), Number(item, 36));
        var npc = Apply(Record(Npc), Record(Npc, (69, Int(1 << 12))), "npc_flag !ONF_WANDERS npc_flag ONF_ALOOF critter_flag OCF_FATIGUE_LIMITING");
        Assert.AreEqual(1L << 17, Number(npc, 69));
        Assert.AreEqual(unchecked((long)(int)(1u << 31)), Number(npc, 36));
    }

    [TestMethod]
    public void Arrays_copy_the_prototypes_array_before_one_element_changes()
    {
        var proto = Record(Npc, (38, Array((0, [8]), (19, [0]), (27, [2]))), (31, Array((9, [0, 0, 1324]))));
        var result = Apply(Record(Npc), proto, "alignment -2000 script 9 2000 script 1 30", n => n == 2000 ? new MapPatchScriptHeader(5, 6) : null);
        var stats = result.Record.Fields[38].Elements;
        CollectionAssert.AreEqual(new[] { 0, 19, 27 }, stats.Select(e => e.Key).ToArray());
        Assert.AreEqual(unchecked((uint)-1000), (uint)stats.Single(e => e.Key == 19).Words[0], "clamped to the stat's range");
        var scripts = result.Record.Fields[31].Elements;
        CollectionAssert.AreEqual(new long[] { 5, 6, 2000 }, scripts.Single(e => e.Key == 9).Words.ToArray(), "the script file's header and its number");
        Assert.AreEqual(1, scripts.Count, "a script the original cannot load attaches nothing");
        Assert.IsFalse(Apply(Record(Weapon), Record(Weapon), "alignment 5").Record.Fields.ContainsKey(38), "only critters have stats");
    }

    [TestMethod]
    public void Script_zero_clears_the_attachment_only_when_the_switch_says_so()
    {
        // ARC-DATA-037 (ledger MP7): the original attaches nothing for script 0; Steamwoven empties the attachment, copying the
        // prototype's other attachments as any element write does.
        var proto = Record(Npc, (31, Array((1, [3, 4, 30]), (9, [5, 6, 2000]))));
        var original = Apply(Record(Npc), proto, "script 9 0");
        Assert.IsFalse(original.Record.Fields.ContainsKey(31), "Original: nothing attached, the prototype's dialog script stays");
        CollectionAssert.AreEqual(new[] { 9 }, original.Application.Lines.ToArray());

        var cleared = Apply(Record(Npc), proto, "script 9 0", scriptZeroClears: true);
        var scripts = cleared.Record.Fields[31].Elements;
        CollectionAssert.AreEqual(new[] { 1, 9 }, scripts.Select(e => e.Key).ToArray());
        CollectionAssert.AreEqual(new long[] { 0, 0, 0 }, scripts.Single(e => e.Key == 9).Words.ToArray(), "no flags, no counters, script 0");
        CollectionAssert.AreEqual(new long[] { 3, 4, 30 }, scripts.Single(e => e.Key == 1).Words.ToArray());
        Assert.AreEqual(0, cleared.Application.Unmodelled.Count);

        // Only a written zero clears: an unlisted number, an absent or a non-numeric value attach nothing; a header still attaches.
        foreach (var text in new[] { "script 9 77", "script 9", "script", "script 9 x", "script 9 0x10", "script 9 -" })
            Assert.IsFalse(Apply(Record(Npc), proto, text, scriptZeroClears: true).Record.Fields.ContainsKey(31), text);
        CollectionAssert.AreEqual(new long[] { 0, 0, 0 }, Apply(Record(Npc), proto, "script 9 -00", scriptZeroClears: true).Record.Fields[31].Elements.Single(e => e.Key == 9).Words.ToArray());
        CollectionAssert.AreEqual(new long[] { 7, 8, 42 }, Apply(Record(Npc), proto, "script 9 42", n => n == 42 ? new MapPatchScriptHeader(7, 8) : null, scriptZeroClears: true)
            .Record.Fields[31].Elements.Single(e => e.Key == 9).Words.ToArray());
        // A header answered for script 0 is never asked for.
        Assert.IsFalse(Apply(Record(Npc), proto, "script 9 0", _ => new MapPatchScriptHeader(1, 1)).Record.Fields.ContainsKey(31));
        // Under Steamwoven a negative slot is unmodelled, as a script with a header at a negative slot is in both modes.
        StringAssert.Contains(Apply(Record(Npc), proto, "script -1 0", scriptZeroClears: true).Application.Unmodelled.Single(), "attachment -1");
        Assert.AreEqual(0, Apply(Record(Npc), proto, "script -1 0").Application.Unmodelled.Count, "Original: script 0 attaches nothing at any slot");
    }

    [TestMethod]
    public void Changes_the_original_makes_differently_are_reported_unmodelled()
    {
        var proto = Record(Npc);
        CollectionAssert.AreEqual(new[] { "line 9: training" }, Apply(Record(Npc), proto, "training ST_EXPERT bs_dodge").Application.Unmodelled.ToArray());
        var noColon = MapPatchApplier.Apply(Record(Npc), proto, [new MapPatchLine(4, Guid, null)], _ => null).Application;
        Assert.AreEqual(0, noColon.Lines.Count);
        StringAssert.Contains(noColon.Unmodelled.Single(), "line 4");
        StringAssert.Contains(Apply(Record(Npc), proto, "faction 99999999999").Application.Unmodelled.Single(), "outside int32");
        StringAssert.Contains(Apply(Record(15), Record(15), "alignment 5").Application.Unmodelled.Single(), "player alignment");
        StringAssert.Contains(Apply(Record(Npc), proto, "script -1 7", _ => new MapPatchScriptHeader(0, 0)).Application.Unmodelled.Single(), "attachment -1");
        StringAssert.Contains(Apply(Record(Npc), proto, "daystand 99999999999999999999 1").Application.Unmodelled.Single(), "outside int64");
        Assert.AreEqual(0, Apply(Record(Weapon), Record(Weapon), "training st_expert bs_dodge").Application.Unmodelled.Count, "no skills to train on an item");
        // An unknown tier word leaves the skill name to be read as a key, which stops the line.
        var notTier = Apply(Record(Npc), proto, "training expert faction 3");
        Assert.AreEqual(0, notTier.Application.Unmodelled.Count);
        Assert.AreEqual(3L, Number(notTier, 81));
        // The executable's npc flag table ends in a null entry: an unknown name fails there.
        StringAssert.Contains(Apply(Record(Npc), proto, "npc_flag ONF_NOT_A_FLAG").Application.Unmodelled.Single(), "npc flag");
    }
}
