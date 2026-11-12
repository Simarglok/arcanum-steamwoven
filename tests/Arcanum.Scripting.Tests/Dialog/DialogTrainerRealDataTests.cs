using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1150 opt-in check (<c>DAT_ROOT</c>, the game folder): every installed Player reply that starts with <c>t:</c> reads as a trainer reply
/// (its numbers are skills and its menu fits), and the generated files hold every line the trainer talk reads. Only counts are compared, never text.
/// </summary>
[TestClass]
public sealed class DialogTrainerRealDataTests
{
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_trainer_reply_is_admitted_and_the_files_hold_the_talks_lines()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in trainer check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;

        int replies = 0, files = 0, skills = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            var any = false;
            foreach (var record in document.Records.Where(r => r.Role == DlgRecordRole.Player))
            {
                var text = Encoding.Latin1.GetString(record.Text.Bytes.ToArray());
                if (text.Length < 2 || char.ToLowerInvariant(text[0]) != 't' || text[1] != ':') continue;
                var list = DialogConversation.ParseTrainerSkills(text);
                Assert.IsNotNull(list, $"{key} {record.Id}: a trainer reply outside the subset");
                replies++;
                skills += list.Length;
                any = true;
                seen.Add(string.Join(",", list));
            }
            if (any) files++;
        }
        Console.WriteLine($"TRAINER|replies {replies}|files {files}|skills listed {skills}|distinct lists {seen.Count}");
        Assert.AreEqual((315, 58), (replies, files));

        var generated = GeneratedDialogFiles.Load(stack).Files!;
        foreach (var file in Enum.GetValues<GeneratedDialogFile>().Where(f => GeneratedDialogFiles.Names[(int)f].StartsWith("gd_cls_m", StringComparison.Ordinal)
                     || GeneratedDialogFiles.Names[(int)f].StartsWith("gd_cls_f", StringComparison.Ordinal)))
        {
            // The NPC class files: groups 1000 to 6000 for every social class block read by the talk (class 0 at least).
            foreach (var group in new[] { 1000, 2000, 3000, 4000, 5000, 6000 })
                Assert.IsTrue(generated.RunLength(file, group, group + 49) > 0, $"{file} {group}");
            var quote = generated.Line(file, 1000).Message!;
            Assert.IsTrue(Encoding.Latin1.GetString(quote.Bytes.ToArray()).Contains("%d", StringComparison.Ordinal), $"{file}: the quote's line holds the price");
        }
        foreach (var file in new[] { GeneratedDialogFile.PcToMale, GeneratedDialogFile.PcToFemale })
            foreach (var (first, last) in new[] { (1, 99), (100, 199), (500, 599), (600, 699), (800, 899) })
                Assert.IsTrue(generated.RunLength(file, first, last) > 0, $"{file} {first}");
    }
}
