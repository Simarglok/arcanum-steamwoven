using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Tests.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Content;

/// <summary>ARC-DATA-026: the stack-backed prototype source, on synthetic installations (no original data).</summary>
[TestClass]
public sealed class ContentPrototypesTests
{
    private const int Weapon = 5;
    private string _root = null!;

    [TestInitialize]
    public void Create()
    {
        Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-protos-" + Guid.NewGuid().ToString("N")));
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), Dat());
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
    }

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    private static byte[] P(int number, int name) => PremadeHeroTests.Prototype(number, Weapon, new Dictionary<int, object> { [21] = name });

    [TestMethod]
    public void The_first_base_source_wins_and_module_copies_are_never_read()
    {
        // arcanum1 < Arcanum4 < data/ < module archive < module patch < module folder.
        File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), Dat(("proto/007001 - a.pro", P(7001, 1)), ("proto/007002 - b.pro", P(7002, 1))));
        File.WriteAllBytes(Path.Combine(_root, "Arcanum4.dat"), Dat(("proto/007001 - a.pro", P(7001, 2))));
        Write("data/proto/007002 - b.pro", P(7002, 3));
        Write("data/proto/007003 - c.pro", P(7003, 3));
        File.WriteAllBytes(Path.Combine(_root, "modules", "Mod.dat"), Dat(("proto/007005 - e.pro", P(7005, 4))));
        File.WriteAllBytes(Path.Combine(_root, "modules", "Mod.patch0"), Dat(("proto/007003 - c.pro", P(7003, 5))));
        Write("modules/Mod/proto/007001 - a.pro", P(7001, 6));
        Write("modules/Mod/proto/007004 - d.pro", P(7004, 6));
        using var stack = Stack();

        var prototypes = ContentPrototypes.Open(stack);
        CollectionAssert.AreEqual(new[] { 7001, 7002, 7003 }, prototypes.Numbers.ToArray());
        CollectionAssert.AreEqual(P(7001, 2), prototypes.Read(7001).Bytes, "Arcanum4.dat over arcanum1.dat, the module folder ignored");
        CollectionAssert.AreEqual(P(7002, 3), prototypes.Read(7002).Bytes, "data/ over the archives");
        CollectionAssert.AreEqual(P(7003, 3), prototypes.Read(7003).Bytes, "the module patch ignored");
        Assert.AreEqual(PrototypeFileStatus.Missing, prototypes.Read(7004).Status, "module folder only");
        Assert.AreEqual(PrototypeFileStatus.Missing, prototypes.Read(7005).Status, "module archive only");
        CollectionAssert.AreEqual(new[] { "proto/007001 - a.pro", "proto/007003 - c.pro", "proto/007004 - d.pro", "proto/007005 - e.pro" },
            prototypes.IgnoredModuleKeys.ToArray());
        Assert.AreEqual(ContentSourceRole.BaseArchive, prototypes.Candidate(7001)!.Source.Role);
        Assert.AreEqual(ContentSourceRole.DataDirectory, prototypes.Candidate(7002)!.Source.Role);
        Assert.AreEqual("proto/007001 - a.pro", prototypes.Path(7001));
        Assert.IsNull(prototypes.Candidate(7004));
        Assert.IsNull(prototypes.Path(7005));
    }

    [TestMethod]
    public void Names_duplicates_size_and_range_are_reported()
    {
        Write("data/proto/007001 - a.pro", P(7001, 1));
        Write("data/proto/007001 - b.pro", P(7001, 2));
        Write("data/proto/7002.pro", P(7002, 1));
        Write("data/proto/sub/007003 - c.pro", P(7003, 1));
        Write("data/proto/007004 - d.txt", P(7004, 1));
        Write("data/proto/007005 - big.pro", new byte[ContentPrototypes.MaxPrototypeBytes + 1]);
        Write("data/proto/007006 - f.PRO", P(7006, 1));
        using var stack = Stack();

        var prototypes = ContentPrototypes.Open(stack);
        CollectionAssert.AreEqual(new[] { 7005, 7006 }, prototypes.Numbers.ToArray());
        CollectionAssert.AreEqual(new[] { 7001 }, prototypes.AmbiguousNumbers.ToArray());
        CollectionAssert.AreEqual(new[] { "proto/007001 - a.pro", "proto/007001 - b.pro" }, prototypes.AmbiguousKeys.ToArray());
        CollectionAssert.AreEqual(new[] { "proto/7002.pro" }, prototypes.UnrecognisedKeys.ToArray());
        Assert.AreEqual(PrototypeFileStatus.Ambiguous, prototypes.Read(7001).Status);
        Assert.IsNull(prototypes.Candidate(7001));
        Assert.AreEqual(PrototypeFileStatus.Missing, prototypes.Read(7002).Status);
        Assert.AreEqual(PrototypeFileStatus.Missing, prototypes.Read(7003).Status, "only files directly under proto/");
        Assert.AreEqual(PrototypeFileStatus.Missing, prototypes.Read(7004).Status);
        Assert.AreEqual(PrototypeFileStatus.TooLarge, prototypes.Read(7005).Status);
        Assert.AreEqual(PrototypeFileStatus.Found, prototypes.Read(7006).Status, "the extension compares ASCII case-insensitively");
        Assert.AreEqual(PrototypeFileStatus.Missing, prototypes.Read(-1).Status);
        Assert.AreEqual(PrototypeFileStatus.Missing, prototypes.Read(1_000_000).Status);
        Assert.ThrowsExactly<ArgumentNullException>(() => ContentPrototypes.Open(null!));
    }

    [TestMethod]
    public void The_combat_field_reader_reads_a_prototype_held_only_by_an_archive()
    {
        File.WriteAllBytes(Path.Combine(_root, "Arcanum4.dat"), Dat(("proto/006218 - sword.pro", P(6218, 2010))));
        using var stack = Stack();
        var prototypes = ContentPrototypes.Open(stack);

        var read = CombatFieldReader.ReadPrototype(prototypes.Read, 6218);
        Assert.AreEqual(CombatFieldFailure.None, read.Failure, $"refused at {read.Detail}");
        Assert.AreEqual((Weapon, 6218, 2010), (read.Fields!.Object.Type, read.Fields.Object.Prototype, read.Fields.Object.Name));
        Assert.AreEqual(CombatFieldFailure.PrototypeMissing, CombatFieldReader.ReadPrototype(prototypes.Read, 6219).Failure);
    }

    private ContentStack Stack()
    {
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Stack!;
    }

    private void Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
