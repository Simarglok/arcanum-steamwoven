using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-SCRIPT-1735 (E5-a tail, ledger NS5 and the change art number rows): the art id a script's change art number wrote is the object state of its directory entry (the field <c>obj.art</c>, no schema step: the objects section
/// holds routed object fields by property name). A static object's record is on its map and is one per map and entity; a mobile object's is one whatever map it names. It is saved with the session and an older save, which has none, loads as
/// it was. Ids, maps and art ids are invented.
/// </summary>
[TestClass]
public sealed class ObjectArtStateTests
{
    private const string Statue = "sector:7:3", Other = "sector:7:4", Npc = "oid:statue-npc";

    [TestMethod]
    public void A_static_objects_art_is_a_record_of_its_map_and_the_baseline_clears_it()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryObjectArt(Exterior, Statue));
        var changed = owner.SetObjectArt(owner.Version, Exterior, Statue, 0x4090_0040, baseline: 0x4120_0040);
        Assert.AreEqual(GameSessionDisposition.Changed, changed.Disposition);
        Assert.AreEqual(0x4090_0040u, owner.QueryObjectArt(Exterior, Statue));
        Assert.IsNull(owner.QueryObjectArt(Cave, Statue), "the same sector and ordinal of another map is another object");
        CollectionAssert.AreEqual(new[] { (Statue, 0x4090_0040u) }, owner.QueryObjectArts(Exterior).ToArray());
        Assert.AreEqual(0, owner.QueryObjectArts(Cave).Count);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetObjectArt(owner.Version, Exterior, Statue, 0x4090_0040, baseline: 0x4120_0040).Disposition);
        Assert.AreEqual(0, owner.Capture().Maps.Count, "an object field, not a per-map change");
        // The id the map data gives is the baseline: the record goes, a second one is nothing.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetObjectArt(owner.Version, Exterior, Statue, 0x4120_0040, baseline: 0x4120_0040).Disposition);
        Assert.IsNull(owner.QueryObjectArt(Exterior, Statue));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetObjectArt(owner.Version, Exterior, Statue, 0x4120_0040, baseline: 0x4120_0040).Disposition);
    }

    [TestMethod]
    public void The_arts_of_a_map_follow_every_art_write_and_clear_between_reads()
    {
        using var owner = Create();
        void Same(SessionMapId map) => CollectionAssert.AreEqual(owner.QueryMapChanges(map)
            .Where(change => change.Key.Property == SessionObjectArt.Property).Select(change => (change.Key.Entity!, (uint)change.Value)).ToArray(), owner.QueryObjectArts(map).ToArray());
        Same(Exterior);
        owner.SetObjectArt(owner.Version, Exterior, Other, 0x40A0_0040, baseline: 1); Same(Exterior);
        owner.SetObjectArt(owner.Version, Exterior, Npc, 0xD050_0000, baseline: 1); Same(Exterior); Same(Cave); Same(Exterior);
        owner.SetObjectArt(owner.Version, Exterior, Statue, 0x4090_0040, baseline: 1); Same(Exterior);
        // A mobile record moves with a write that names another map, and an unrelated record changes nothing here.
        owner.SetObjectArt(owner.Version, Cave, Npc, 0xD060_0000, baseline: 1); Same(Exterior); Same(Cave);
        owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Statue, "obj.off"), 1); Same(Exterior);
        owner.SetObjectArt(owner.Version, Exterior, Statue, 0x4090_0041, baseline: 1); Same(Exterior);
        owner.SetObjectArt(owner.Version, Exterior, Other, 1, baseline: 1); Same(Exterior);
        CollectionAssert.AreEqual(new[] { (Statue, 0x4090_0041u) }, owner.QueryObjectArts(Exterior).ToArray());
    }

    [TestMethod]
    public void The_art_is_saved_with_the_session_and_a_mobile_objects_record_is_one_whatever_map_it_names()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetObjectArt(owner.Version, Exterior, Statue, 0x4090_0040, baseline: 1).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetObjectArt(owner.Version, Exterior, Other, 0x40A0_0040, baseline: 1).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetObjectArt(owner.Version, Cave, Statue, 0x40B0_0040, baseline: 1).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetObjectArt(owner.Version, Exterior, Npc, 0xD050_0000, baseline: 1).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetObjectArt(owner.Version, Cave, Npc, 0xD060_0000, baseline: 1).Disposition, "a write that names another map moves the record");
        Assert.AreEqual(0xD060_0000u, owner.QueryObjectArt(Exterior, Npc), "found by identity, whatever map is asked");
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(0x4090_0040u, restored.QueryObjectArt(Exterior, Statue));
        Assert.AreEqual(0x40A0_0040u, restored.QueryObjectArt(Exterior, Other));
        Assert.AreEqual(0x40B0_0040u, restored.QueryObjectArt(Cave, Statue));
        Assert.AreEqual(0xD060_0000u, restored.QueryObjectArt(Cave, Npc));
        CollectionAssert.AreEqual(new[] { (Statue, 0x4090_0040u), (Other, 0x40A0_0040u) }, restored.QueryObjectArts(Exterior).ToArray());
    }

    [TestMethod]
    public void A_mobile_items_art_record_is_cleared_by_the_baseline_from_any_map_it_stands_on()
    {
        // ARC-SCRIPT-1779 (AN10): an item is a mobile object; its record travels with it, so the run that returns it to the data's art may name the map the item stands on now.
        using var owner = Create();
        const string Item = "oid:item:lamp";
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetObjectArt(owner.Version, Exterior, Item, 0x6040_0040, baseline: 0x6020_0040).Disposition);
        Assert.AreEqual(0x6040_0040u, owner.QueryObjectArt(Cave, Item), "found by identity from the map it stands on now");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetObjectArt(owner.Version, Cave, Item, 0x6020_0040, baseline: 0x6020_0040).Disposition);
        Assert.IsNull(owner.QueryObjectArt(Exterior, Item));
        Assert.IsNull(owner.QueryObjectArt(Cave, Item));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetObjectArt(owner.Version, Cave, Item, 0x6020_0040, baseline: 0x6020_0040).Disposition);
    }

    [TestMethod]
    public void A_made_items_art_record_follows_it_from_map_to_map_is_saved_and_is_the_only_field_a_made_item_keeps()
    {
        // ARC-SCRIPT-1826 (AN18): a made or script-created item (made:n) is a mobile object for this field: one record whatever map it names, saved in the objects section, cleared by the baseline from any map.
        using var owner = Create();
        const string Made = "made:3";
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetObjectArt(owner.Version, Exterior, Made, 0x6040_0040, baseline: 0x6020_0040).Disposition);
        Assert.AreEqual(0x6040_0040u, owner.QueryObjectArt(Cave, Made), "found by identity from the map it stands on now");
        Assert.AreEqual(0, owner.Capture().Maps.Count, "an object field, not a per-map change");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetObjectArt(owner.Version, Cave, Made, 0x6060_0040, baseline: 0x6020_0040).Disposition, "a write that names another map moves the record");
        Assert.IsNull(owner.QueryMapChange(Exterior, SessionObjectArt.Key(Made)));
        CollectionAssert.AreEqual(new[] { (Made, 0x6060_0040u) }, owner.QueryObjectArts(Cave).ToArray());
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(0x6060_0040u, restored.QueryObjectArt(Exterior, Made));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.SetObjectArt(restored.Version, Exterior, Made, 0x6020_0040, baseline: 0x6020_0040).Disposition);
        Assert.IsNull(restored.QueryObjectArt(Cave, Made));
        // No other field is a made item's own: it is a per-map change, as before (ARC-SCRIPT-2346: but the off flag of the item, which a script's toggle state writes).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(Made, "obj.destroyed"), 1).Disposition);
        Assert.AreEqual(1, owner.Capture().Maps.Count);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetMapChange(owner.Version, Cave, SessionItemOff.Key(Made), 1).Disposition);
        Assert.AreEqual(1, owner.Capture().Maps.Count, "the off flag is an object field of the made item: no per-map change");
    }

    [TestMethod]
    public void A_save_without_the_field_loads_as_it_was_and_a_refused_write_changes_nothing()
    {
        using var plain = Create();
        using var loaded = Restore(Decode(GameSessionSnapshotCodec.Encode(plain.Capture())));
        Assert.IsNull(loaded.QueryObjectArt(Exterior, Statue));
        Assert.AreEqual(0, loaded.QueryObjectArts(Exterior).Count);
        var version = plain.Version;
        Assert.AreEqual(GameSessionFailure.InvalidChangeKey, plain.SetObjectArt(plain.Version, Exterior, "bad id", 5, baseline: 1).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidMap, plain.SetObjectArt(plain.Version, new SessionMapId(0), Statue, 5, baseline: 1).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, plain.SetObjectArt(new GameSessionVersion(plain.Version.Owner, plain.Version.Revision + 1), Exterior, Statue, 5, baseline: 1).Failure);
        Assert.AreEqual(version, plain.Version);
        // The budget counts the field like any change.
        using var tight = Create(new GameSessionLimits(maxChangesPerMap: 1));
        Assert.AreEqual(GameSessionDisposition.Changed, tight.SetObjectArt(tight.Version, Exterior, Statue, 5, baseline: 1).Disposition);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, tight.SetObjectArt(tight.Version, Exterior, Other, 5, baseline: 1).Failure);
    }
}
