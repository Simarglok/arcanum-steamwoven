using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Content;

/// <summary>Opt-in check of the installed original (DAT_ROOT = the game folder) against the
/// facts in docs/research/content-precedence.md.</summary>
[TestClass]
public sealed class ContentStackRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_original_mounts_in_the_documented_order_and_crash_site_inputs_are_winners()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in content precedence check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var plan = discovery.Plan!;
        CollectionAssert.AreEqual(new[]
        {
            "ModuleDirectory:modules/arcanum", "ModulePatch:modules/arcanum.patch0", "ModuleArchive:modules/arcanum.dat", "DataDirectory:data",
            "BaseArchive:arcanum4.dat", "BaseArchive:arcanum3.dat", "BaseArchive:arcanum2.dat", "BaseArchive:arcanum1.dat", "GameRoot:.", "Tig:tig.dat"
        }, plan.SearchOrder.Select(s => $"{s.Role}:{Relative(plan, s)}").ToArray());

        var opened = ContentStack.Open(plan);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var overrides = stack.Overrides();
        var chains = overrides.GroupBy(o => string.Join(" > ", o.Candidates.Select(c => Relative(plan, c.Source))))
            .ToDictionary(g => g.Key, g => g.Count());
        CollectionAssert.AreEquivalent(new Dictionary<string, int>
        {
            ["arcanum4.dat > arcanum3.dat"] = 63,
            ["arcanum4.dat > arcanum2.dat"] = 43,
            ["modules/arcanum.patch0 > modules/arcanum.dat"] = 2,
            ["modules/arcanum.patch0 > arcanum4.dat > arcanum3.dat"] = 1,
        }, chains);
        Assert.IsTrue(overrides.All(o => o.Candidates.All(c => c.Entry is not null)), "No loose file overrides anything.");

        // The Crash Site exterior and cave read arcanum1/arcanum2/module entries by ordinal.
        var shadowed = overrides.SelectMany(o => o.Shadowed.Select(c => (Source: Relative(plan, c.Source), o.Key))).ToArray();
        Assert.IsFalse(shadowed.Any(s => s.Source == "arcanum1.dat"));
        Assert.IsTrue(shadowed.Where(s => s.Source == "arcanum2.dat").All(s => s.Key.StartsWith("scr/", StringComparison.Ordinal) || s.Key.StartsWith("sound/", StringComparison.Ordinal)));
        CollectionAssert.AreEquivalent(new[] { "mes/gamequestlog.mes", "rules/wmap_rnd.mes" },
            shadowed.Where(s => s.Source == "modules/arcanum.dat").Select(s => s.Key).ToArray());
        var archives = new Dictionary<AreaSourceRole, string> { [AreaSourceRole.Module] = "modules/arcanum.dat", [AreaSourceRole.Resource1] = "arcanum1.dat", [AreaSourceRole.Resource2] = "arcanum2.dat" };
        var bindings = OriginalProfile.Plan().Mappings.Concat(CaveProfile.Plan().Mappings).ToArray();
        Assert.AreEqual(18, bindings.Length);
        foreach (var binding in bindings)
        {
            var source = plan.SearchOrder.Single(s => Relative(plan, s) == archives[binding.Entry.Source.Role]);
            var name = EntryName(source, binding.Entry.Ordinal);
            Assert.AreEqual(binding.Entry.NameSha256, Sha(Encoding.ASCII.GetBytes(name + "\0")), name);
            var winner = stack.Resolve(name).Winner!;
            Assert.AreSame(source, winner.Source, name);
            Assert.AreEqual(binding.Entry.Ordinal, winner.Entry!.Ordinal, name);
        }
        Assert.AreEqual(1, stack.Resolve(@"maps\Arcanum1-024-fixed\86570436012.sec").Candidates.Count);
        // Prototypes and the game-generated mobile files are loose files that nothing shadows.
        var prototypeFolder = plan.SearchOrder.Single(s => s.Role == ContentSourceRole.DataDirectory).Path;
        foreach (var number in OriginalProfile.Prototypes.Keys.Concat(CaveProfile.Prototypes.Keys).Distinct())
        {
            var file = Path.GetFileName(Directory.EnumerateFiles(Path.Combine(prototypeFolder, "proto"), $"{number:D6} - *.pro").Single());
            var prototype = stack.Resolve("proto/" + file);
            Assert.AreEqual(1, prototype.Candidates.Count, file);
            Assert.AreEqual(ContentSourceRole.DataDirectory, prototype.Winner!.Source.Role, file);
        }
        foreach (var mobile in new[] { "maps/qrmvs-420-1zjcnpgn", "maps/rknp rivh uhngp" })
        {
            var resolution = stack.Resolve(mobile);
            Assert.AreEqual(1, resolution.Candidates.Count, mobile);
            Assert.AreEqual(ContentSourceRole.ModuleDirectory, resolution.Winner!.Source.Role, mobile);
        }

        // ARC-SCRIPT-005 reads Virgil's script from arcanum2.dat; the original opens Arcanum4.dat's.
        var script = stack.Resolve(@"scr\01324Virgil.scr");
        CollectionAssert.AreEqual(new[] { "arcanum4.dat#86", "arcanum2.dat#8973" }, script.Candidates.Select(c => $"{Relative(plan, c.Source)}#{c.Entry!.Ordinal}").ToArray());
        Assert.AreEqual("8c6a4e5c9c42d4be8f35e711cfbde32db860315fd76e87c052fa13aa1176684b", Sha(stack.Read(script.Candidates[0]).Bytes!));
        Assert.AreEqual("f726b47ca8a960b9d876583320fb9592bd79009ff1880c71650bceecfa43e4fe", Sha(stack.Read(script.Candidates[1]).Bytes!));
        var dialog = stack.Resolve(@"dlg\01324Virgil.dlg");
        CollectionAssert.AreEqual(new[] { "arcanum3.dat#160" }, dialog.Candidates.Select(c => $"{Relative(plan, c.Source)}#{c.Entry!.Ordinal}").ToArray());
        var mapPatch = stack.Resolve(@"Rules\map.patch");
        CollectionAssert.AreEqual(new[] { "modules/arcanum.patch0#3" }, mapPatch.Candidates.Select(c => $"{Relative(plan, c.Source)}#{c.Entry!.Ordinal}").ToArray());
    }

    private static string EntryName(ContentSource source, int ordinal)
    {
        using var archive = DatArchive.Open(source.Path);
        var raw = archive.Entries[ordinal].RawNameBytes.ToArray();
        return Encoding.ASCII.GetString(raw, 0, raw.Length - 1);
    }

    private static string Relative(ContentMountPlan plan, ContentSource source)
        => Path.GetRelativePath(plan.GameRoot, source.Path).Replace('\\', '/').ToLowerInvariant();

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
