using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Fixes;
using Arcanum.Data.Scripts;
using Arcanum.Data.Tests.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Fixes;

/// <summary>ARC-DATA-022: DLG data fixes over invented synthetic files.</summary>
[TestClass]
public sealed class DataFixTests
{
    private static readonly byte[] SixFields = Encoding.Latin1.GetBytes("{1}{Six fields only.}{}{}{}{}\r\n");
    private static readonly byte[] EffectsInResponse = Encoding.Latin1.GetBytes(
        "{1}{Npc line.}{}{}{}{gf 7 1}{}\r\n{2}{Reply.}{}{1}{}{fl 3}{re -10}\r\n{3}{Last.}{}{}{}{}{}\r\n{4}{Plain.}{}{1}{}{0}{}\r\n{5}{Jump.}{}{1}{}{12 fl 3}{re 1}\r\n");

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [TestMethod]
    public void Profiles_enable_every_fix_in_Steamwoven_and_none_in_Original()
    {
        foreach (var id in new[] { "DF1", "DF2", "DF3", "MP7" })
        {
            Assert.IsTrue(DataFixProfile.Steamwoven.Applies(id), id);
            Assert.IsFalse(DataFixProfile.Original.Applies(id), id);
        }
        Assert.IsFalse(DataFixProfile.Steamwoven.Applies("DF9"));
        CollectionAssert.AreEquivalent(new[] { "DF1", "DF2", "DF3", "MP7" }, DataFixProfile.Rows.ToArray(), "every switch row, and only those");
        Assert.AreSame(DataFixProfile.Original, DataFixProfile.FromName("original"));
        Assert.IsNull(DataFixProfile.FromName("Original"));
        Assert.IsFalse((DataFixProfile.Steamwoven with { Name = "mix", NpcLineEffectsInResponseLost = true }).Applies("DF2"));
        Assert.IsFalse((DataFixProfile.Steamwoven with { Name = "mix", MapPatchScriptZeroIgnored = true }).Applies("MP7"));
        // MP7 is the map patch's switch: a DLG catalogue entry for it is refused.
        Assert.ThrowsExactly<ArgumentException>(() => new DlgDataFixCatalogue([new("MP7", "dlg/00001a.dlg", Sha(SixFields), DlgFixOperation.CompleteLastRecord)]));
    }

    [TestMethod]
    public void A_fix_applies_only_to_the_exact_copy_it_was_written_for()
    {
        var catalogue = new DlgDataFixCatalogue([new("DF1", "dlg/00001a.dlg", Sha(SixFields), DlgFixOperation.CompleteLastRecord)]);
        var result = catalogue.Apply("dlg/00001a.dlg", SixFields, DataFixProfile.Steamwoven);
        Assert.AreEqual(DlgFixOutcome.Applied, result.Attempts.Single().Outcome);
        var document = DlgReader.Read(result.Bytes, ScriptContent.OriginalDialogs);
        Assert.AreEqual((1, DlgStopReason.EndOfInput), (document.Records.Count, document.StopReason));
        Assert.AreEqual(0, document.Records[0].Action.Length);
        Assert.AreEqual(0, DlgReader.Read(SixFields, ScriptContent.OriginalDialogs).Records.Count, "the input is not changed");

        // Another copy (a patch or a mod), another path, or the Original mode: nothing is touched.
        var other = SixFields.Concat(" "u8.ToArray()).ToArray();
        Assert.AreSame(other, catalogue.Apply("dlg/00001a.dlg", other, DataFixProfile.Steamwoven).Bytes);
        Assert.AreEqual(0, catalogue.Apply("dlg/00001a.dlg", other, DataFixProfile.Steamwoven).Attempts.Count);
        Assert.AreSame(SixFields, catalogue.Apply("dlg/00002a.dlg", SixFields, DataFixProfile.Steamwoven).Bytes);
        Assert.AreSame(SixFields, catalogue.Apply("dlg/00001a.dlg", SixFields, DataFixProfile.Original).Bytes);
    }

    [TestMethod]
    public void A_response_holding_actions_moves_to_the_effects_and_keeps_every_record()
    {
        var sha = Sha(EffectsInResponse);
        var catalogue = new DlgDataFixCatalogue(
        [
            new("DF2", "dlg/00001a.dlg", sha, DlgFixOperation.MoveResponseToActions, 1),
            new("DF3", "dlg/00001a.dlg", sha, DlgFixOperation.MoveResponseToActions, 2),
            new("DF3", "dlg/00001a.dlg", sha, DlgFixOperation.MoveResponseToActions, 4),  // a plain number: no defect
            new("DF3", "dlg/00001a.dlg", sha, DlgFixOperation.MoveResponseToActions, 9),  // no such record
            new("DF3", "dlg/00001a.dlg", sha, DlgFixOperation.MoveResponseToActions, 5),  // leading digits: a real jump
        ]);
        var result = catalogue.Apply("dlg/00001a.dlg", EffectsInResponse, DataFixProfile.Steamwoven);
        CollectionAssert.AreEqual(new[] { DlgFixOutcome.Applied, DlgFixOutcome.Applied, DlgFixOutcome.Refused, DlgFixOutcome.Refused, DlgFixOutcome.Refused },
            result.Attempts.Select(a => a.Outcome).ToArray());
        var document = DlgReader.Read(result.Bytes, ScriptContent.OriginalDialogs);
        string Field(int id, DlgFieldKind kind) => Encoding.Latin1.GetString(document.Records.Single(r => r.Id == id).Fields[(int)kind].Bytes.ToArray());
        Assert.AreEqual(5, document.Records.Count);
        Assert.AreEqual(("12 fl 3", "re 1"), (Field(5, DlgFieldKind.Response), Field(5, DlgFieldKind.Action)));
        Assert.AreEqual(("", "gf 7 1"), (Field(1, DlgFieldKind.Response), Field(1, DlgFieldKind.Action)), "an empty effects field takes the list as it is");
        Assert.AreEqual(("", "re -10, fl 3"), (Field(2, DlgFieldKind.Response), Field(2, DlgFieldKind.Action)), "a non-empty one gets it after a comma");
        Assert.AreEqual(("0", ""), (Field(4, DlgFieldKind.Response), Field(4, DlgFieldKind.Action)));
        Assert.AreEqual("Last.", Field(3, DlgFieldKind.Text));

        // Only the switches the profile turns on.
        var mixed = catalogue.Apply("dlg/00001a.dlg", EffectsInResponse, DataFixProfile.Steamwoven with { Name = "mix", ReplyFinalLineInResponseLost = true });
        Assert.AreEqual("DF2", mixed.Applied.Single().LedgerId);
    }

    [TestMethod]
    public void Completing_a_record_refuses_a_file_without_that_defect()
    {
        foreach (var text in new[] { "{1}{Complete.}{}{}{}{}{}", "{1}{Five.}{}{}{}", "{1}{Open seventh.}{}{}{}{}{", "" })
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            var catalogue = new DlgDataFixCatalogue([new("DF1", "dlg/00001a.dlg", Sha(bytes), DlgFixOperation.CompleteLastRecord)]);
            var result = catalogue.Apply("dlg/00001a.dlg", bytes, DataFixProfile.Steamwoven);
            Assert.AreEqual(DlgFixOutcome.Refused, result.Attempts.Single().Outcome, text);
            Assert.AreSame(bytes, result.Bytes, text);
        }
    }

    [TestMethod]
    public void The_catalogue_refuses_malformed_entries()
    {
        var sha = new string('a', 64);
        foreach (var bad in new DlgDataFix[]
        {
            new("DF1", "DLG/00001a.dlg", sha, DlgFixOperation.CompleteLastRecord),
            new("DF1", "dlg\\00001a.dlg", sha, DlgFixOperation.CompleteLastRecord),
            new("DF1", "dlg/00001a.dlg", sha.ToUpperInvariant(), DlgFixOperation.CompleteLastRecord),
            new("DF1", "dlg/00001a.dlg", sha[1..], DlgFixOperation.CompleteLastRecord),
            new("DF1", "dlg/00001a.dlg", sha, DlgFixOperation.CompleteLastRecord, 3),
            new("DF2", "dlg/00001a.dlg", sha, DlgFixOperation.MoveResponseToActions),
            new("DF2", "dlg/00001a.dlg", sha, (DlgFixOperation)9, 1),
            new("", "dlg/00001a.dlg", sha, DlgFixOperation.CompleteLastRecord),
            new("DF4", "dlg/00001a.dlg", sha, DlgFixOperation.CompleteLastRecord),
            new("DF1", "dlg/00001a.dlg", null!, DlgFixOperation.CompleteLastRecord),
            new("DF1", null!, sha, DlgFixOperation.CompleteLastRecord),
        })
            Assert.ThrowsExactly<ArgumentException>(() => new DlgDataFixCatalogue([bad]), bad.ToString());
        var fix = new DlgDataFix("DF1", "dlg/00001a.dlg", sha, DlgFixOperation.CompleteLastRecord);
        Assert.ThrowsExactly<ArgumentException>(() => new DlgDataFixCatalogue([fix, fix]));
        Assert.AreEqual(6, DlgDataFixCatalogue.Installed.Fixes.Count);
    }

    [TestMethod]
    public void Script_content_applies_the_fixes_of_the_rules_it_is_given()
    {
        var root = Directory.CreateTempSubdirectory("arc-data-022-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "tig.dat"), AreaIdentityTests.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "modules", "Mod"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "scr"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "dlg"));
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00001a.scr"), new byte[64]);
            File.WriteAllBytes(Path.Combine(root.FullName, "dlg", "00001a.dlg"), SixFields);
            var nul = Encoding.Latin1.GetBytes("{1}{nul\0}{}{}{}{}");
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00002b.scr"), new byte[64]);
            File.WriteAllBytes(Path.Combine(root.FullName, "dlg", "00002b.dlg"), nul);
            var catalogue = new DlgDataFixCatalogue([new("DF1", "dlg/00001a.dlg", Sha(SixFields), DlgFixOperation.CompleteLastRecord),
                new("DF1", "dlg/00002b.dlg", Sha(nul), DlgFixOperation.CompleteLastRecord)]);
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Mod").Plan!, fixes: catalogue).Content!;
            var raw = content.Dialog(1);
            Assert.AreEqual((ScriptContentStatus.Empty, 0), (raw.Status, raw.Fixes.Count), "no rules: the retail read");
            Assert.AreEqual(ScriptContentStatus.Empty, content.Dialog(1, DataFixProfile.Original).Status);
            var fixedRead = content.Dialog(1, DataFixProfile.Steamwoven);
            Assert.AreEqual(ScriptContentStatus.Read, fixedRead.Status, fixedRead.Detail);
            Assert.AreEqual("DF1", fixedRead.Fixes.Single().Fix.LedgerId);
            Assert.IsTrue(content.VerifySeen(), "change detection is on the stored file");
            // A read the reader refuses still reports the fixes that matched.
            var refused = content.Dialog(2, DataFixProfile.Steamwoven);
            Assert.AreEqual((ScriptContentStatus.Invalid, DlgFixOutcome.Refused), (refused.Status, refused.Fixes.Single().Outcome));
            Assert.IsFalse(content.Changed);
        }
        finally { root.Delete(true); }
    }
}
