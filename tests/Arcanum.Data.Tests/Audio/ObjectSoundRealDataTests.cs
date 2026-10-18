using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Audio;

/// <summary>
/// ARC-WORLD-041 opt-in check of the installed original (DAT_ROOT = the game folder, module Arcanum): every prototype's sound
/// fields read, the ids the event rules reach are in the catalogue, and every ground tile has a footstep class (census in
/// docs/research/audio-evidence.md). Only counts are compared, never original text or audio.
/// </summary>
[TestClass]
public sealed class ObjectSoundRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_prototype_has_sound_fields_and_every_tile_a_sound_class()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var catalog = SoundCatalog.Open(stack).Catalog!;
        var prototypes = ContentPrototypes.Open(stack);

        var reads = prototypes.Numbers.Select(n => ObjectSoundReader.ReadPrototype(prototypes.Read, n)).ToArray();
        Assert.AreEqual(1423, reads.Length);
        Assert.IsTrue(reads.All(r => r.Failure == ObjectSoundFailure.None), "every prototype reads");
        var fields = reads.Select(r => r.Fields!).ToArray();
        int Based(int type) => fields.Count(f => f.Type == type && f.SoundEffect != 0);
        CollectionAssert.AreEqual(new[] { 0, 15, 38, 3, 0, 150, 13, 496 },
            new[] { Based(0), Based(1), Based(2), Based(3), Based(4), Based(5), Based(15), Based(16) }, "walls, portals, containers, scenery, projectiles, weapons, PCs, NPCs");
        Assert.AreEqual(0, fields.Count(f => f.Type is >= 6 and <= 14 or 17 && f.SoundEffect != 0), "no other item or trap has a base");
        Assert.AreEqual(1, fields.Count(f => f.Item?.UnknownDescription == 9056), "one gold prototype");
        Assert.AreEqual(8, fields.Single(f => f.Item?.UnknownDescription == 9056).Type);

        SfxStatus Status(int id) => catalog.ResolveSfx(id).Status;
        var weapons = fields.Where(f => f.Type == 5).ToArray();
        Assert.AreEqual(22, weapons.Count(w => Status(w.SoundEffect) == SfxStatus.UnknownId), "weapons whose base is in no table: hits use the material sounds");
        Assert.IsTrue(weapons.Where(w => Status(w.SoundEffect) != SfxStatus.UnknownId)
            .All(w => Enumerable.Range(4, 3).All(o => Status(w.SoundEffect + o) == SfxStatus.EmptyRecord)), "every other weapon's hit and critical ids are empty records");
        Assert.IsTrue(fields.Where(f => f.Type is 1 or 2 && f.SoundEffect != 0).All(f => Enumerable.Range(0, 3).All(o => Status(f.SoundEffect + o) != SfxStatus.UnknownId)));

        bool Playable(int id) => catalog.ResolveSfx(id).Playable;
        Assert.IsTrue(Enumerable.Range(2900, 40).All(Playable), "plate and every class's footsteps, chain sets included");
        Assert.IsTrue(Enumerable.Range(5950, 9).Concat(Enumerable.Range(5960, 9)).All(Playable), "pickup and drop by class and gold");
        Assert.IsTrue(Playable(4010));
        Assert.IsTrue(Enumerable.Range(0, 5).SelectMany(w => Enumerable.Range(0, 5).SelectMany(t => new[] { 0, 1 }.Select(v => 7000 + 20 * w + 3 * t + v))).All(Playable),
            "every material hit");

        var tiles = TileSoundTable.Open(stack);
        Assert.AreEqual(TileSoundTableStatus.Ready, tiles.Status);
        var table = tiles.Table!;
        CollectionAssert.AreEqual(new[] { 31, 6, 2, 13 }, Enumerable.Range(0, 4).Select(c => table.Category(c).Count).ToArray());
        var classes = Enumerable.Range(0, 4).SelectMany(table.Category).ToArray();
        Assert.IsTrue(classes.All(c => c is >= 0 and <= 5), "every class has footsteps");
        var records = stack.Read(ContentPhase.Candidate(stack, TileSoundTable.Path, ContentLoadPhase.GameStart)!).Bytes!;
        Assert.AreEqual(Arcanum.Data.Messages.MesParseStatus.Parsed, Arcanum.Data.Messages.MesTable.Read(records, new(), out var names));
        var bare = names.Where(n => n.Key is >= 0 and < 400 && !n.Value.Contains('/')).ToArray();
        Assert.AreEqual(21, bare.Length, "records without flags");
        CollectionAssert.AreEqual(new[] { 4, 11, 6 }, new[] { 0, 3, 5 }.Select(c => bare.Count(b => SoundClassOf(table, names, b.Key) == c)).ToArray(),
            "17 of them are stone or wood, read after the third character");
        CollectionAssert.AreEqual(new[] { 21, 3, 0, 14, 8, 6 }, Enumerable.Range(0, 6).Select(c => classes.Count(x => x == c)).ToArray(), "dirt, sand, snow, stone, water, wood");
    }

    // The class the table gives a record: its position among the records of its category.
    private static int SoundClassOf(TileSoundTable table, IReadOnlyDictionary<int, string> names, int record) =>
        table.Category(record / 100)[names.Keys.Where(k => k / 100 == record / 100 && k >= 0 && k < record).Count()];
}
