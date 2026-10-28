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
    public void Invalid_physical_group_frame_and_item_state_never_clamp_and_a_tail_is_no_part_of_the_art()
    {
        // A frame the ART lacks, an unadmitted item state and a portal without portal names. (Rotation 1 of a
        // single-rotation ART is its group 0: Item_and_three_npc_classes_use_literal_paths_and_reject_ambiguous_closed_source_sets.)
        foreach (var raw in new uint[] { 0x40004000, 0x60001000, 0x30000000 })
        {
            using var f = Make(raw);
            var report = f.Owner.Inspect(); Assert.IsFalse(report.IsComplete);
            Assert.IsNull(f.Owner.PublishedSnapshot);
            Assert.IsTrue(report.Issues.Any(i => i.Code is AreaErrorCode.UnsupportedPhysicalSelection or AreaErrorCode.UnsupportedArtClassOrState));
        }
        // ARC-DATA-036: bytes after the last frame are no part of the ART; the frame is read from the ART's own extent.
        var trailing = Art().Concat(new byte[] { 0 }).ToArray();
        using (var tailed = Make(0x40000000, art: trailing))
        {
            var snapshot = tailed.Owner.LoadStrict();
            var visual = snapshot.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
            Assert.AreEqual(("original-art-extent-v1", Art().Length), (visual.Policy, visual.ViewLength));
            // A lease reads the frame again from the same extent and gives the same pixels.
            using var lease = tailed.Owner.AcquireSelectedFrame(snapshot, visual.Key);
            CollectionAssert.AreEqual(new byte[] { 1,2,3,255,4,5,6,255 }, lease.CopyRgba());
        }
        // Both rules on one selection: rotation 1 of the tailed single-rotation ART reads group 0 from the ART's extent
        // (the policy names the group rule; the view shows the trim).
        using var both = Make(0x40000800, art: trailing);
        var aliased = both.Owner.LoadStrict().Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
        Assert.AreEqual(("original-single-group-rotation-v1", Art().Length, 1, 0), (aliased.Policy, aliased.ViewLength, aliased.SourceGroup, aliased.PhysicalGroup));
    }

    [TestMethod]
    public void An_object_whose_art_id_names_no_art_has_no_body()
    {
        // ARC-DATA-038: ART id 0xFFFFFFFF (type 15) names no ART in the original (ledger NA1). The object stays an object with
        // its disposition; it has no visual, and the load is complete (a window around Caladon held such scenery).
        using var f = Make(uint.MaxValue);
        var snapshot = f.Owner.LoadStrict();
        var placed = snapshot.Objects.Single(o => o.Key.Kind == AreaSourceKind.Static);
        Assert.AreEqual((AreaDisposition.BodyEligibleConditionally, true, true, true),
            (placed.Disposition, placed.SchemaReady, placed.PlacementReady, placed.RequiredLayersReady));
        Assert.IsFalse(snapshot.Visuals.Any(v => v.Key.Kind == AreaSourceKind.Static));
        // Any other ART type 15 id is still refused.
        using var other = Make(0xF0000000);
        Assert.IsTrue(other.Owner.Inspect().Issues.Any(i => i.Code == AreaErrorCode.UnsupportedArtClassOrState));
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
        // Packed facing 1 asks group 7 by the source rule (mirrored), not modulo; a single-rotation ART has only group 0
        // (ARC-DATA-036: the original's rule on every plan).
        using var rotated = Make(0x20000800, resourceName: cases[1].Path, allMappings: true);
        var alias = rotated.Owner.LoadStrict().Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
        Assert.AreEqual((7, 0, true, "original-single-group-rotation-v1"), (alias.SourceGroup, alias.PhysicalGroup, alias.Mirrored, alias.Policy));
    }

    [TestMethod]
    public void A_name_held_by_both_base_archives_is_ambiguous_and_pixels_keep_transparency_row_order()
    {
        var art = new byte[1188]; Art().CopyTo(art, 0);
        BinaryPrimitives.WriteInt32LittleEndian(art.AsSpan(1160), 2); // height2
        BinaryPrimitives.WriteInt32LittleEndian(art.AsSpan(1164), 4); // four raw indexed pixels
        BinaryPrimitives.WriteInt32LittleEndian(art.AsSpan(1176), -13);
        BinaryPrimitives.WriteInt32LittleEndian(art.AsSpan(1180), 17);
        art[1184] = 1; art[1185] = 0; art[1186] = 2; art[1187] = 1;
        // ARC-DATA-036: a mapping's archive does not choose the ART; without a stack order a name both base archives hold is ambiguous.
        using (var both = Make(0x40000000, art: art, secondResource: Dat(("art/scenery/fixture.art", Art()))))
            Reject(both, AreaErrorCode.AmbiguousSource);
        using var f = Make(0x40000000, art: art);
        var snapshot = f.Owner.LoadStrict(); var visual = snapshot.Visuals.Single(v => v.Key.Kind == AreaSourceKind.Static);
        Assert.AreEqual(AreaSourceRole.Resource1, visual.Entry.Source.Role);
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
            // A body the serialized state hides at T0 resolves no visual; an inventory-refused body keeps its reference.
            Assert.AreEqual(expected == AreaDisposition.BodySuppressedBySerializedState ? 1 : 2, s.Visuals.Count);
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
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows refuses the rename of an open file (see the Windows binding tests).");
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

    // ARC-DEV-049: the Windows binding uses managed calls only, so it runs on macOS and Linux too. On Windows the read-only share
    // mode refuses the rename and rewrite these tests do (asserted there instead); elsewhere they show the size/write-time
    // comparison catches them anyway.
    [TestMethod]
    public void Path_binding_is_unix_on_macos_and_linux_and_windows_on_windows()
    {
        var expected = OperatingSystem.IsWindows() ? AreaPathBinding.Windows : AreaPathBinding.Unix;
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        { Throws<PlatformNotSupportedException>(() => AreaPathStream.CurrentBinding()); return; }
        Assert.AreEqual(expected, AreaPathStream.CurrentBinding());
        Throws<ArgumentOutOfRangeException>(() => new AreaPathStream(Path.Combine(Path.GetTempPath(), "absent.dat"), (AreaPathBinding)7));
        if (OperatingSystem.IsWindows())
            Throws<PlatformNotSupportedException>(() => new AreaPathStream(Path.Combine(Path.GetTempPath(), "absent.dat"), AreaPathBinding.Unix));
    }

    [TestMethod]
    public void Windows_path_binding_reads_and_publishes_like_the_unix_one()
    {
        var directory = TemporaryDirectory("area-windows-path-"); var path = Path.Combine(directory,"module.dat");
        var bytes = Dat(("maps/test/67108865.sec",Sector())); File.WriteAllBytes(path,bytes);
        var key = new AreaInputKey(AreaSourceRole.Module);
        var plan = new AreaSourcePlan(1,1,new(1,1),2,"maps/test",[new(key,bytes.Length,Hash(bytes))],[]);
        try
        {
            using var memory = AreaDataOwner.Open(new([new AreaInput(key,new MemoryStream(bytes))]),plan,new());
            using var owner = AreaDataOwner.Open(new([new AreaInput(key,new AreaPathStream(path,AreaPathBinding.Windows))]),plan,new());
            // The adapter's buffer is admitted as for the Unix binding.
            Assert.AreEqual(65568L, owner.Usage.RetainedReserved - memory.Usage.RetainedReserved);
            var expected = memory.Inspect(); var report = owner.Inspect();
            Assert.AreEqual(expected.IsComplete, report.IsComplete);
            CollectionAssert.AreEqual(expected.Issues.Select(i => i.Code).ToArray(), report.Issues.Select(i => i.Code).ToArray());
            Assert.IsFalse(report.Issues.Any(i => i.Code == AreaErrorCode.InputChangedDuringRead));
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    [TestMethod]
    public void Windows_path_binding_refuses_a_replaced_rewritten_or_removed_file()
    {
        var directory = TemporaryDirectory("area-windows-change-"); var path = Path.Combine(directory,"module.dat");
        var bytes = Dat(("maps/test/67108865.sec",Sector()));
        if (OperatingSystem.IsWindows())
        {
            // The share mode itself: no rename, delete or write of the open file.
            try
            {
                File.WriteAllBytes(path,bytes);
                using var open = new AreaPathStream(path,AreaPathBinding.Windows);
                Throws<IOException>(() => File.Move(path,path+".old"));
                Throws<IOException>(() => File.Delete(path));
                Throws<IOException>(() => File.OpenWrite(path).Dispose());
                open.Check();
            }
            finally { File.Delete(path); File.Delete(path+".old"); Directory.Delete(directory); }
            return;
        }
        try
        {
            File.WriteAllBytes(path,bytes);
            var plan = new AreaSourcePlan(1,1,new(1,1),2,"maps/test",[new(new(AreaSourceRole.Module),bytes.Length,Hash(bytes))],[]);
            using (var owner = AreaDataOwner.Open(new([new AreaInput(new(AreaSourceRole.Module),new AreaPathStream(path,AreaPathBinding.Windows))]),plan,new()))
            {
                var written = File.GetLastWriteTimeUtc(path);
                File.Move(path,path+".old"); File.WriteAllBytes(path,bytes); File.SetLastWriteTimeUtc(path,written.AddMinutes(1)); // same bytes, later write
                var report = owner.Inspect(); Assert.IsFalse(report.IsComplete);
                Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.InputChangedDuringRead));
                Assert.IsNull(owner.PublishedSnapshot);
            }
            File.Delete(path); File.Move(path+".old",path);

            using (var grown = new AreaPathStream(path,AreaPathBinding.Windows))
            {
                var written = File.GetLastWriteTimeUtc(path);
                File.WriteAllBytes(path,[.. bytes, 0]); File.SetLastWriteTimeUtc(path,written); // longer file, same write time
                Assert.AreEqual(AreaErrorCode.InputChangedDuringRead, Throws<AreaException>(() => grown.Check()).Issue.Code);
            }
            File.WriteAllBytes(path,bytes);

            using (var removed = new AreaPathStream(path,AreaPathBinding.Windows))
            {
                File.Move(path,path+".old");
                Assert.AreEqual(AreaErrorCode.InputChangedDuringRead, Throws<AreaException>(() => removed.Check()).Issue.Code);
            }
            File.Move(path+".old",path);

            Assert.AreEqual(AreaErrorCode.MissingSource,
                Throws<AreaException>(() => new AreaPathStream(Path.Combine(directory,"absent.dat"),AreaPathBinding.Windows)).Issue.Code);
        }
        finally { File.Delete(path); File.Delete(path+".old"); Directory.Delete(directory); }
    }

    private static string TemporaryDirectory(string prefix)
    {
        var directory = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); return directory;
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
            ("tile.mes",Encoding.ASCII.GetBytes("{0}{stone/}{100}{stone/}{200}{stone/}{300}{stone/}{400}{end/}")),
            ("facade.mes",Encoding.ASCII.GetBytes("{0}{fixture}")), ("scenery.mes",Encoding.ASCII.GetBytes(mapping)) };
        if (allMappings) entries.AddRange(new (string,byte[])[] { ("item.mes",Encoding.ASCII.GetBytes("{0}{fixture.art}")),
            ("monster.mes",Encoding.ASCII.GetBytes("{0}{fixture}")), ("npc.mes",Encoding.ASCII.GetBytes("{0}{fixture}")) });
        entries.Add(("art/tile/stonebse0a.art",Art())); entries.Add((resourceName,art ?? Art()));
        if (duplicate) entries.Add((resourceName,Art()));
        return new([(67108865,[Record(Id(0))])],prototype:prototype,resourceEntries:entries.ToArray(),secondResource:secondResource,limits:limits,
            mappingRoles: allMappings ? [AreaMappingRole.Tile,AreaMappingRole.Facade,AreaMappingRole.Scenery,AreaMappingRole.ItemGround,AreaMappingRole.MonsterNames,AreaMappingRole.UniqueNpcNames] : null);
    }
}
