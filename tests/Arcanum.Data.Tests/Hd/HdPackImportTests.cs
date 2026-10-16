using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

[TestClass]
public sealed class HdPackImportTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-hd-import-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private (SyntheticHdPack Pack, SyntheticHdPack.Frame Frame) Built(string name = "built-elsewhere", string id = "synthetic-pack")
    {
        var pack = new SyntheticHdPack(Path.Combine(_root, name), id: id);
        var frame = SyntheticHdPack.Legacy("critter", 6, 4, seed: 2, palette: 2);
        pack.Add(frame, file: "frames/critter/g0-f0-p2.png");
        pack.Add(SyntheticHdPack.Legacy("item", 3, 3, seed: 4));
        pack.Write();
        return (pack, frame);
    }

    [TestMethod]
    public void A_pack_built_elsewhere_imports_with_the_same_identity_and_binds_from_its_new_place()
    {
        var (source, frame) = Built();
        File.WriteAllText(Path.Combine(source.Directory, "notes.txt"), "not part of the pack");
        var collection = Path.Combine(_root, "device", "hd-packs");
        var before = Directory.GetFiles(source.Directory, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);

        var result = HdPackImport.Import(source.Directory, collection);
        Assert.AreEqual(HdImportStatus.Imported, result.Status, result.Detail);
        var imported = result.Pack!;
        Assert.AreEqual(Path.Combine(Path.GetFullPath(collection), "synthetic-pack"), imported.Directory);
        Assert.AreEqual(HdPack.Open(source.Directory).Pack!.Identity, imported.Identity, "copying is not rebuilding");
        Assert.IsTrue(result.Report!.Intact);
        Assert.AreEqual(HdFrameStatus.Hd, imported.ResolveSprite(frame.Query, frame.Rgba).Status);

        CollectionAssert.AreEquivalent(new[] { "frames/0001.png", "frames/critter/g0-f0-p2.png", "manifest.json", "recipe.json" },
            Directory.GetFiles(imported.Directory, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(imported.Directory, f).Replace(Path.DirectorySeparatorChar, '/')).ToArray(),
            "only the manifest, the recipe and entry files are copied");
        CollectionAssert.AreEqual(new[] { "synthetic-pack" }, Directory.GetDirectories(collection).Select(Path.GetFileName).ToArray(), "no staging left");
        foreach (var (path, bytes) in before) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path), "the source is never modified");

        // Moving the whole directory anywhere keeps it working: every path inside is relative.
        var moved = Path.Combine(_root, "elsewhere", "renamed-folder");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(imported.Directory, moved);
        var reopened = HdPack.Open(moved);
        Assert.AreEqual(imported.Identity, reopened.Pack!.Identity);
        Assert.AreEqual(HdFrameStatus.Hd, reopened.Pack.ResolveSprite(frame.Query, frame.Rgba).Status);
    }

    [TestMethod]
    public void A_damaged_copy_or_an_existing_pack_is_refused_and_replace_is_explicit()
    {
        var (source, frame) = Built();
        var collection = Path.Combine(_root, "packs");
        Assert.AreEqual(HdImportStatus.Imported, HdPackImport.Import(source.Directory, collection).Status);
        Assert.AreEqual(HdImportStatus.AlreadyExists, HdPackImport.Import(source.Directory, collection).Status);
        Assert.AreEqual(HdImportStatus.AlreadyExists, HdPackImport.Import(Path.Combine(collection, "synthetic-pack"), collection, replace: true).Status,
            "a pack cannot replace itself");
        Assert.AreEqual(HdImportStatus.AlreadyExists, HdPackImport.Import(source.Directory, collection, replace: true).Status,
            "an identical pack is not replaced by a copy of itself");
        if (!OperatingSystem.IsWindows())
        {
            var alias = Path.Combine(_root, "alias");
            Directory.CreateSymbolicLink(alias, collection);
            var installed = Path.Combine(collection, "synthetic-pack", "manifest.json");
            var before = File.GetLastWriteTimeUtc(installed);
            Assert.AreEqual(HdImportStatus.AlreadyExists, HdPackImport.Import(Path.Combine(alias, "synthetic-pack"), collection, replace: true).Status,
                "nor through a link to itself");
            Assert.AreEqual(before, File.GetLastWriteTimeUtc(installed));
        }

        // A rebuilt pack (other pixels) with the same id replaces the old one only on request.
        var rebuilt = new SyntheticHdPack(Path.Combine(_root, "rebuilt"));
        rebuilt.Add(frame, SyntheticHdPack.Hd(frame, 2, tint: 77), file: "frames/critter/g0-f0-p2.png");
        rebuilt.Write();
        var replaced = HdPackImport.Import(rebuilt.Directory, collection, replace: true);
        Assert.AreEqual(HdImportStatus.Imported, replaced.Status, replaced.Detail);
        Assert.AreEqual(HdPack.Open(rebuilt.Directory).Pack!.Identity, replaced.Pack!.Identity);
        Assert.AreNotEqual(HdPack.Open(source.Directory).Pack!.Identity, replaced.Pack.Identity, "a rebuild is a new identity");
        Assert.AreEqual(1, Directory.GetDirectories(collection).Length, "no staging or replaced copy left");
        Assert.IsFalse(File.Exists(Path.Combine(collection, "synthetic-pack", "frames", "0001.png")), "nothing of the old pack survives");

        // A damaged install of the same identity is repaired by an explicit replace.
        var installedFrame = Path.Combine(collection, "synthetic-pack", "frames", "critter", "g0-f0-p2.png");
        var good = File.ReadAllBytes(installedFrame);
        var broken = (byte[])good.Clone(); broken[^16] ^= 1; File.WriteAllBytes(installedFrame, broken);
        Assert.AreEqual(HdImportStatus.AlreadyExists, HdPackImport.Import(rebuilt.Directory, collection).Status, "replace stays explicit");
        var repaired = HdPackImport.Import(rebuilt.Directory, collection, replace: true);
        Assert.AreEqual(HdImportStatus.Imported, repaired.Status, repaired.Detail);
        CollectionAssert.AreEqual(good, File.ReadAllBytes(installedFrame));
        Assert.IsTrue(repaired.Pack!.VerifyIntegrity().Intact);

        // A source inside the pack it would replace is refused before anything is deleted.
        var inner = new SyntheticHdPack(Path.Combine(collection, "synthetic-pack", "inner"));
        inner.Add(frame, SyntheticHdPack.Hd(frame, 2, tint: 9), file: "frames/critter/g0-f0-p2.png");
        inner.Write();
        Assert.AreEqual(HdImportStatus.SourceInvalid, HdPackImport.Import(inner.Directory, collection, replace: true).Status);
        Assert.IsTrue(HdPack.Open(inner.Directory).Pack!.VerifyIntegrity().Intact, "the source survives");
        Directory.Delete(inner.Directory, recursive: true);

        var damaged = Built("damaged", "damaged-pack").Pack;
        var file = Path.Combine(damaged.Directory, "frames", "0001.png");
        var bytes = File.ReadAllBytes(file); bytes[^16] ^= 1; File.WriteAllBytes(file, bytes);
        var refused = HdPackImport.Import(damaged.Directory, collection);
        Assert.AreEqual(HdImportStatus.IntegrityFailed, refused.Status);
        Assert.AreEqual(1, refused.Report!.ProblemCounts[HdInvalidReason.HashMismatch]);
        Assert.IsFalse(Directory.Exists(Path.Combine(collection, "damaged-pack")));

        Assert.AreEqual(HdImportStatus.SourceInvalid, HdPackImport.Import(Path.Combine(_root, "absent"), collection).Status);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.AreEqual(HdImportStatus.Cancelled, HdPackImport.Import(Built("c", "cancelled-pack").Pack.Directory, collection, token: cancelled.Token).Status);
        Assert.IsFalse(Directory.EnumerateDirectories(collection).Any(d => Path.GetFileName(d).StartsWith('.')));
    }

    [TestMethod]
    public void Discovery_lists_collection_and_chosen_packs_and_one_broken_pack_hides_nothing()
    {
        var collection = Path.Combine(_root, "game-packs");
        Assert.AreEqual(HdImportStatus.Imported, HdPackImport.Import(Built("a", "beta").Pack.Directory, collection).Status);
        Assert.AreEqual(HdImportStatus.Imported, HdPackImport.Import(Built("b", "alpha").Pack.Directory, collection).Status);
        Directory.CreateDirectory(Path.Combine(collection, "broken"));
        File.WriteAllText(Path.Combine(collection, "broken", "manifest.json"), "{");
        Directory.CreateDirectory(Path.Combine(collection, ".import-leftover"));
        File.WriteAllText(Path.Combine(collection, ".import-leftover", "manifest.json"), "{");
        Directory.CreateDirectory(Path.Combine(collection, "no-manifest"));
        var external = Built("usb-stick/my-pack", "external").Pack.Directory;

        var found = HdPack.Discover([
            new(HdPackLocationKind.Collection, collection),
            new(HdPackLocationKind.Collection, Path.Combine(_root, "missing")),
            new(HdPackLocationKind.Pack, external),
        ]);
        CollectionAssert.AreEqual(new[] { "alpha", "beta", "broken", "my-pack" }, found.Select(f => Path.GetFileName(f.Directory)).ToArray());
        CollectionAssert.AreEqual(new[] { HdPackOpenStatus.Opened, HdPackOpenStatus.Opened, HdPackOpenStatus.Malformed, HdPackOpenStatus.Opened },
            found.Select(f => f.Result.Status).ToArray());
        Assert.AreEqual("external", found[3].Result.Pack!.Id);
        Assert.AreEqual(2, HdPack.Discover([new(HdPackLocationKind.Collection, collection)], new HdPackOptions { MaxPacksPerCollection = 2 }).Count);

        // A manifest with text that is not valid UTF-8 is one malformed pack, not a failed listing.
        var badText = Built("c", "corrupt-text").Pack;
        var manifest = Path.Combine(badText.Directory, "manifest.json");
        var bytes = File.ReadAllBytes(manifest);
        var at = bytes.AsSpan().IndexOf("\"gpu\":\"none\""u8);
        bytes[at + 8] = 0xFF;
        File.WriteAllBytes(manifest, bytes);
        Directory.Move(badText.Directory, Path.Combine(collection, "corrupt-text"));
        var listed = HdPack.Discover([new(HdPackLocationKind.Collection, collection)]);
        Assert.AreEqual(4, listed.Count);
        Assert.AreEqual(HdPackOpenStatus.Malformed, listed.Single(f => Path.GetFileName(f.Directory) == "corrupt-text").Result.Status);
        Assert.AreEqual(HdImportStatus.SourceInvalid, HdPackImport.Import(Path.Combine(collection, "corrupt-text"), Path.Combine(_root, "other")).Status);

        // An empty manifest (or anything that is not a non-empty regular file) is unreadable, never opened as a stream.
        var empty = Path.Combine(_root, "empty-pack");
        Directory.CreateDirectory(empty);
        File.WriteAllBytes(Path.Combine(empty, "manifest.json"), []);
        Assert.AreEqual(HdPackOpenStatus.Unreadable, HdPack.Open(empty).Status);
    }
}
