using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Data.Tests.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Scripts;

[TestClass]
public sealed class ScriptContentTests
{
    private static readonly byte[] Dialog = Encoding.Latin1.GetBytes("{1}{Hello.}{}{}{}{}{}{2}{Bye.}{}{1}{}{0}{}");
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-script-content-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void Number_reads_leading_digits_as_atoi_does()
    {
        Assert.AreEqual(1324, ScriptNameIndex.Number("01324virgil.scr"));
        Assert.AreEqual(11, ScriptNameIndex.Number("  11a.scr"));
        Assert.AreEqual(12, ScriptNameIndex.Number("+12.scr"));
        Assert.AreEqual(1, ScriptNameIndex.Number("0000000001x.scr"), "leading zeros are not significant digits");
        Assert.AreEqual(999999999, ScriptNameIndex.Number("999999999.scr"));
        foreach (var file in new[] { "-5.scr", "0.scr", "00000a.scr", "x.scr", "", "+.scr", "1234567890.scr" })
            Assert.IsNull(ScriptNameIndex.Number(file), file);
    }

    [TestMethod]
    public void Names_follow_the_listing_order_and_the_mount_time_of_each_number_range()
    {
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), AreaIdentityTests.Dat());
        File.WriteAllBytes(Path.Combine(_root, "arcanum1.dat"), AreaIdentityTests.Dat(
            ("dlg\\00007seven.dlg", Dialog), ("scr\\00007seven.scr", Script(1)), ("scr\\01000base.scr", Script(2))));
        Loose("modules/Mod/scr/01000base.scr", Script(3));     // the module copy of a base name wins the read
        Loose("modules/Mod/scr/02000modonly.scr", Script(4));  // listed only after the module mount: never named
        Loose("modules/Mod/scr/01000zmod.scr", Script(4));     // nor does it replace the base name of its number
        Loose("scr/.scr", Script(8));                          // listed; atoi reads 0
        Loose("modules/Mod/scr/00008mod.scr", Script(5));      // 1..999 are listed with the module
        Loose("data/scr/00009a.scr", Script(6));
        Loose("data/scr/00009b.scr", Script(7));               // a later name replaces an earlier one
        Loose("scr/x.scr", Script(8)); Loose("scr/00000zero.scr", Script(8)); Loose("scr/-0005neg.scr", Script(8));
        Loose("scr/sub/00013nested.scr", Script(8)); Loose("scr/00014text.txt", Script(8)); Loose("scr/ 00011space.scr", Script(9));
        Loose("dlg/00008other.dlg", Dialog);                   // a DLG is found by the script's name, not by its number

        using var content = Open();
        var names = content.Names;
        Assert.AreEqual(12, names.ListedNames);
        Assert.AreEqual(4, names.UnnumberedNames);
        Assert.AreEqual(2, names.ModuleOnlySystemNames);
        Assert.AreEqual(new ScriptName(1000, "scr/01000base.scr", "dlg/01000base.dlg", 0), names.Find(1000));
        Assert.AreEqual(1, names.ReplacedNames);
        CollectionAssert.AreEqual(new[] { 7, 8, 9, 11, 1000 }, names.Names.Select(n => n.Number).ToArray());
        Assert.AreEqual(new ScriptName(9, "scr/00009b.scr", "dlg/00009b.dlg", 1), names.Find(9));
        Assert.AreEqual("scr/ 00011space.scr", names.Find(11)!.ScriptPath);
        Assert.IsNull(names.Find(2000));

        Assert.AreEqual(3u, content.Script(1000).Program!.InitialLocalFlags, "module copy");
        Assert.AreEqual(7u, content.Script(9).Program!.InitialLocalFlags);
        Assert.AreEqual(ScriptContentStatus.NoName, content.Script(2000).Status);
        Assert.AreEqual(ScriptContentStatus.NoName, content.Dialog(2000).Status);
        var dialog = content.Dialog(7);
        Assert.AreEqual(ScriptContentStatus.Read, dialog.Status, dialog.Detail);
        Assert.IsTrue(dialog.Document!.TryGetRecord(1, out _));
        Assert.AreEqual(ScriptContentStatus.NotFound, content.Dialog(8).Status);
        Assert.AreEqual("dlg/00008mod.dlg: not in the content stack", content.Dialog(8).Detail);
    }

    [TestMethod]
    public void Bad_bytes_are_typed_and_a_changed_file_is_not_mixed_in()
    {
        Base();
        Loose("scr/00001short.scr", new byte[10]);
        Loose("scr/00002ok.scr", Script(1));
        Loose("dlg/00002ok.dlg", Encoding.Latin1.GetBytes("{1}{unterminated"));
        Loose("scr/00003ok.scr", Script(1));
        Loose("dlg/00003ok.dlg", Dialog);
        using var content = Open();
        Assert.AreEqual(ScriptContentStatus.Invalid, content.Script(1).Status);
        Assert.AreEqual("scr/00001short.scr: Truncated", content.Script(1).Detail);
        Assert.AreEqual(ScriptContentStatus.Invalid, content.Dialog(2).Status);
        Assert.AreEqual(ScriptContentStatus.Read, content.Script(3).Status);
        Assert.AreEqual(ScriptContentStatus.Read, content.Dialog(3).Status);
        Assert.IsFalse(content.Changed);

        Loose("scr/00003ok.scr", Script(2));
        Assert.AreEqual(ScriptContentStatus.Changed, content.Script(3).Status);
        Assert.IsTrue(content.Changed);
        Assert.AreEqual(ScriptContentStatus.Read, content.Dialog(3).Status, "another path keeps its first bytes");
        Loose("scr/00003ok.scr", Script(1));
        Assert.AreEqual(ScriptContentStatus.Read, content.Script(3).Status, "the first bytes again");
        Assert.IsTrue(content.Changed, "a change is remembered");
    }

    [TestMethod]
    public void VerifySeen_catches_a_change_of_a_file_read_earlier_without_reading_it_again()
    {
        Base();
        Loose("scr/00001one.scr", Script(1));
        Loose("dlg/00001one.dlg", Dialog);
        Loose("scr/00002two.scr", Script(1));
        using var content = Open();
        Assert.IsTrue(content.VerifySeen(), "nothing read yet");
        Assert.AreEqual(ScriptContentStatus.Read, content.Script(1).Status);
        Assert.AreEqual(ScriptContentStatus.Read, content.Dialog(1).Status);
        Assert.IsTrue(content.VerifySeen());
        Loose("scr/00002two.scr", Script(2));
        Assert.IsTrue(content.VerifySeen(), "a file never read is not compared");
        Loose("dlg/00001one.dlg", Encoding.Latin1.GetBytes("{1}{Changed.}{}{}{}{}{}"));
        Assert.IsFalse(content.VerifySeen());
        Assert.IsTrue(content.Changed);
        Assert.IsFalse(content.VerifySeen(), "for good");
    }

    [TestMethod]
    public void Over_borrows_the_stack_and_Open_owns_it()
    {
        Base();
        Loose("scr/00001one.scr", Script(1));
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        using (var stack = ContentStack.Open(discovery.Plan!).Stack!)
        {
            using (var borrowed = ScriptContent.Over(stack)) Assert.AreEqual(ScriptContentStatus.Read, borrowed.Script(1).Status);
            Assert.AreEqual(ContentResolutionStatus.Resolved, stack.Resolve("scr/00001one.scr").Status, "still open");
        }
        var owned = Open();
        owned.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => owned.Script(1));
        Assert.ThrowsExactly<ArgumentNullException>(() => ScriptContent.Over(null!));
    }

    private void Base()
    {
        File.WriteAllBytes(Path.Combine(_root, "tig.dat"), AreaIdentityTests.Dat());
        Directory.CreateDirectory(Path.Combine(_root, "modules", "Mod"));
    }

    private ScriptContent Open()
    {
        var discovery = ContentMountPlan.Discover(_root, "Mod");
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ScriptContent.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return opened.Content!;
    }

    private void Loose(string relative, byte[] bytes)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>An empty script whose initial local flags tell copies apart.</summary>
    private static byte[] Script(uint flags)
    {
        var bytes = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, flags);
        return bytes;
    }
}
