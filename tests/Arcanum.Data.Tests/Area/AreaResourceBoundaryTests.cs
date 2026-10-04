using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

// Independent literal packed selectors and tiny FILE/DAT/ART fixtures. These
// isolate appearance selection with a scenery FILE envelope; actual original
// NPC/item type/default/layer chains are separately checked by the full oracle.
[TestClass]
public sealed class AreaResourceBoundaryTests
{
    [TestMethod]
    public void Missing_duplicate_and_unsafe_mapping_sources_never_fall_back_or_publish()
    {
        foreach (var mapping in new[] { "{1}{fixture.art}", "{0}{fixture.art}{0}{other.art}", "{0}{../fixture.art}", "{0}{fixture.art" })
        {
            using var f = Make(0x40000000, mapping: mapping);
            var report = f.Owner.Inspect();
            Assert.IsFalse(report.IsComplete); Assert.IsNull(f.Owner.PublishedSnapshot);
            Throws<AreaIncompleteException>(() => f.Owner.LoadStrict());
        }
        using (var missing = Make(0x40000000, resourceName: "art/scenery/other.art"))
            Reject(missing, AreaErrorCode.MissingSource);
        using (var duplicate = Make(0x40000000, duplicate: true))
            Reject(duplicate, AreaErrorCode.AmbiguousSource);
    }

    [TestMethod]
    public void Invalid_physical_group_frame_and_item_state_never_clamp_or_use_exact_original_exceptions()
    {
        foreach (var raw in new uint[] { 0x40000800, 0x40004000, 0x60001000, 0x30000000 })
        {
            using var f = Make(raw);
            var report = f.Owner.Inspect(); Assert.IsFalse(report.IsComplete);
            Assert.IsNull(f.Owner.PublishedSnapshot);
            Assert.IsTrue(report.Issues.Any(i => i.Code is AreaErrorCode.UnsupportedPhysicalSelection or AreaErrorCode.UnsupportedArtClassOrState));
        }
        var trailing = Art().Concat(new byte[] { 0 }).ToArray();
        using var malformed = Make(0x40000000, art: trailing);
        Reject(malformed, AreaErrorCode.UnsupportedPhysicalSelection);
    }

    [TestMethod]
    public void Selected_palette_geometry_and_independent_pixel_copy_are_identity_bound()
    {
        var art = Art();
        BinaryPrimitives.WriteInt32LittleEndian(art.AsSpan(1168), -7);
        BinaryPrimitives.WriteInt32LittleEndian(art.AsSpan(1172), 11);
        using var f = Make(0x40000010, art: art); // requested palette1 absent -> decoder's explicit palette0 policy
        var snapshot = f.Owner.LoadStrict(); var visual = snapshot.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
        Assert.AreEqual(1, visual.RequestedPalette); Assert.AreEqual(0, visual.EffectivePalette);
        Assert.AreEqual(2, visual.Width); Assert.AreEqual(1, visual.Height);
        Assert.AreEqual(-7, visual.HotX); Assert.AreEqual(11, visual.HotY);
        using var lease = f.Owner.AcquireSelectedFrame(snapshot, visual.Key);
        CollectionAssert.AreEqual(new byte[] { 1,2,3,255,4,5,6,255 }, lease.CopyRgba());
        Throws<AreaException>(() => f.Owner.AcquireSelectedFrame(snapshot, visual.Key));
        Throws<AreaException>(() => f.Owner.LoadStrict());
    }

    [TestMethod]
    public void Item_and_three_npc_classes_use_literal_paths_and_reject_ambiguous_closed_source_sets()
    {
        (uint Raw, string Path)[] cases = [
            (0x60000000, "art/item/fixture.art"),
            (0x20000000, "art/critter/HMF/HMFUWXAa.art"),
            (0xC0000000, "art/monster/fixture/fixtureUWXAa.art"),
            (0xD0000000, "art/unique_npc/fixture/fixtureXAa.art")];
        foreach (var (raw, path) in cases)
        {
            using var f = Make(raw, resourceName: path, allMappings: true);
            var snapshot = f.Owner.LoadStrict(); var visual = snapshot.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
            Assert.AreEqual(raw, visual.RawArt); Assert.AreEqual(0, visual.Frame); Assert.AreEqual(0, visual.PhysicalGroup);
            Assert.IsFalse(visual.Mirrored);
            using var lease = f.Owner.AcquireSelectedFrame(snapshot, visual.Key);
            CollectionAssert.AreEqual(new byte[] { 1,2,3,255,4,5,6,255 }, lease.CopyRgba());
            if (raw >> 28 is 2 or 12 or 13)
            {
                using var duplicate = Make(raw, resourceName: path, allMappings: true, secondResource: Dat((path, Art())));
                Reject(duplicate, AreaErrorCode.AmbiguousSource);
            }
        }
        // Packed facing1 aliases physical7 by the source rule, not modulo.
        using var wrongGroup = Make(0x20000800, resourceName: cases[1].Path, allMappings: true);
        Reject(wrongGroup, AreaErrorCode.UnsupportedPhysicalSelection);
    }

    [TestMethod]
    public void Mapping_bound_source_wins_only_for_its_explicit_role_and_pixels_keep_transparency_row_order()
    {
        var art = new byte[1188]; Art().CopyTo(art, 0);
        BinaryPrimitives.WriteInt32LittleEndian(art.AsSpan(1160), 2); // height2
        BinaryPrimitives.WriteInt32LittleEndian(art.AsSpan(1164), 4); // four raw indexed pixels
        BinaryPrimitives.WriteInt32LittleEndian(art.AsSpan(1176), -13);
        BinaryPrimitives.WriteInt32LittleEndian(art.AsSpan(1180), 17);
        art[1184] = 1; art[1185] = 0; art[1186] = 2; art[1187] = 1;
        using var f = Make(0x40000000, art: art, secondResource: Dat(("art/scenery/fixture.art", Art())));
        var snapshot = f.Owner.LoadStrict(); var visual = snapshot.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
        Assert.AreEqual(AreaSourceRole.Resource1, visual.Entry.Source.Role); // explicit scenery mapping, not global precedence
        Assert.AreEqual(-13, visual.ArtOffsetX); Assert.AreEqual(17, visual.ArtOffsetY);
        using var lease = f.Owner.AcquireSelectedFrame(snapshot, visual.Key);
        CollectionAssert.AreEqual(new byte[] { 1,2,3,255, 0,0,0,0, 4,5,6,255, 1,2,3,255 }, lease.CopyRgba());
    }

    [TestMethod]
    public void Inventory_state_and_required_composite_flags_retain_facts_but_never_hide_missing_layers()
    {
        foreach (var (flags, expected) in new[] { (4096, AreaDisposition.ExteriorSectorInsertionRefusedByInventory), (1, AreaDisposition.BodySuppressedBySerializedState), (0, AreaDisposition.BodyEligibleConditionally) })
        {
            using var f = Make(0x40000000, flags: flags);
            var s = f.Owner.LoadStrict(); Assert.AreEqual(expected, s.Objects.Single().Disposition);
            Assert.AreEqual(2, s.Visuals.Count); // body reference retained, even if source draw disposition refuses it
        }
        using var layered = Make(0x40000000, flags: 32768);
        var report = layered.Owner.Inspect(); Assert.IsFalse(report.IsComplete);
        Assert.IsFalse(report.Snapshot!.Objects.Single().RequiredLayersReady);
        Assert.IsNull(layered.Owner.PublishedSnapshot);
        Throws<AreaIncompleteException>(() => layered.Owner.LoadStrict());
    }

    [TestMethod]
    public void Visual_cardinality_cap_is_admitted_before_next_art_request()
    {
        using var f = Make(0x40000000, limits: new() { MaxVisuals = 1 });
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var report = f.Owner.Inspect(); Assert.IsFalse(report.IsComplete);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Error = "visual-cap", AllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated,
            f.Owner.Usage, report.Issues }));
        Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.BudgetExceeded));
        Assert.AreEqual(1L, f.Owner.Usage.FrameRequests); // terrain only; object ART was not read/decompressed/decoded
        Assert.IsNull(f.Owner.PublishedSnapshot);
    }

    [TestMethod]
    public void Input_count_and_prototype_count_caps_reject_before_authentication_and_close_owned_streams()
    {
        var bytes = Dat(); var streams = new[] { new MemoryStream(bytes), new MemoryStream(bytes), new MemoryStream(bytes) };
        var keys = new AreaInputKey[] { new(AreaSourceRole.Module), new(AreaSourceRole.Prototype, 1), new(AreaSourceRole.Prototype, 2) };
        var plan = new AreaSourcePlan(1,1,new(1,1),2,"maps/test",keys.Select(k => new AreaInputIdentity(k,bytes.Length,Hash(bytes))),[]);
        var error = Throws<AreaException>(() => AreaDataOwner.Open(new(keys.Select((k,i) => new AreaInput(k,streams[i]))),plan,new() { MaxPrototypes = 1 }));
        Assert.AreEqual(AreaErrorCode.BudgetExceeded,error.Issue.Code);
        Assert.IsTrue(streams.All(s => !s.CanRead));
    }

    [TestMethod]
    public void Path_replacement_after_open_fails_before_publication_and_disposes_handles()
    {
        var directory = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? AppContext.BaseDirectory, "area-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var path = Path.Combine(directory,"module.dat");
        var bytes = Dat(("maps/test/67108865.sec",Sector())); File.WriteAllBytes(path,bytes);
        var plan = new AreaSourcePlan(1,1,new(1,1),2,"maps/test",[new(new(AreaSourceRole.Module),bytes.Length,Hash(bytes))],[]);
        try
        {
            using var owner = AreaDataOwner.Open(new([AreaInput.FromFile(new(AreaSourceRole.Module),path)]),plan,new());
            File.Move(path,path+".old"); File.WriteAllBytes(path,bytes); // same content, different inode
            var report = owner.Inspect(); Assert.IsFalse(report.IsComplete);
            Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.InputChangedDuringRead));
            Assert.IsNull(owner.PublishedSnapshot);
        }
        finally { File.Delete(path); File.Delete(path+".old"); Directory.Delete(directory); }
    }

    private static void Reject(Fixture f, AreaErrorCode code)
    {
        var report = f.Owner.Inspect(); Assert.IsFalse(report.IsComplete);
        Assert.IsTrue(report.Issues.Any(i => i.Code == code), string.Join(',',report.Issues));
        Assert.IsNull(f.Owner.PublishedSnapshot); Throws<AreaIncompleteException>(() => f.Owner.LoadStrict());
    }
    private static Fixture Make(uint raw, string mapping = "{0}{fixture.art}", string resourceName = "art/scenery/fixture.art", byte[]? art = null,
        bool duplicate = false, int flags = 0, bool allMappings = false, byte[]? secondResource = null, AreaLoaderOptions? limits = null)
    {
        var prototype = Prototype();
        BinaryPrimitives.WriteUInt32LittleEndian(prototype.AsSpan(68),raw); // 4-byte version + two24-byte IDs + type + three masks
        BinaryPrimitives.WriteInt32LittleEndian(prototype.AsSpan(119),flags); // literal slot18, with nullable fields represented by one byte
        var entries = new List<(string Name,byte[] Payload)> {
            ("tile.mes",Encoding.ASCII.GetBytes("{0}{stone}{100}{stone}{200}{stone}{300}{stone}{400}{end}")),
            ("facade.mes",Encoding.ASCII.GetBytes("{0}{fixture}")), ("scenery.mes",Encoding.ASCII.GetBytes(mapping)) };
        if (allMappings) entries.AddRange(new (string,byte[])[] { ("item.mes",Encoding.ASCII.GetBytes("{0}{fixture.art}")),
            ("monster.mes",Encoding.ASCII.GetBytes("{0}{fixture}")), ("npc.mes",Encoding.ASCII.GetBytes("{0}{fixture}")) });
        entries.Add(("art/tile/stonebse0a.art",Art())); entries.Add((resourceName,art ?? Art()));
        if (duplicate) entries.Add((resourceName,Art()));
        return new([(67108865,[Record(Id(0))])],prototype:prototype,resourceEntries:entries.ToArray(),secondResource:secondResource,limits:limits,
            mappingRoles: allMappings ? [AreaMappingRole.Tile,AreaMappingRole.Facade,AreaMappingRole.Scenery,AreaMappingRole.ItemGround,AreaMappingRole.MonsterNames,AreaMappingRole.UniqueNpcNames] : null);
    }
}
