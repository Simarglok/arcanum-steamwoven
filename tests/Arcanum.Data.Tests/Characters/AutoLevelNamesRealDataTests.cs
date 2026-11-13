using Arcanum.Data.Characters;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Characters;

/// <summary>
/// ARC-UI-1742: opt-in check of the installed original's scheme names (<c>mes\userlevelname.mes</c>, <c>mes\gamelevelname.mes</c>); DAT_ROOT
/// is the game folder. Only numbers are compared; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class AutoLevelNamesRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Installed_scheme_name_files_list_the_schemes_the_page_shows()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        MesFile Table(string path)
        {
            var resolution = stack.Resolve(path);
            Assert.AreEqual(ContentResolutionStatus.Resolved, resolution.Status, path);
            var bytes = stack.Read(resolution.Candidates[0]);
            Assert.AreEqual(ContentReadStatus.Read, bytes.Status, path);
            var parsed = MesFile.Parse(bytes.Bytes);
            Assert.AreEqual(MesParseStatus.Parsed, parsed.Status, $"{path} line {parsed.Line}");
            return parsed.File!;
        }
        var game = Table(AutoLevelTables.GameNamesPath);
        var user = Table(AutoLevelTables.UserNamesPath);
        var listed = AutoLevelTables.NamedSchemes(game, user);
        var rules = new[] { Table(AutoLevelTables.UserRulesPath), Table(AutoLevelTables.GameRulesPath) };
        Assert.AreEqual(0, listed[0], "scheme 0 is the first name");
        // AL14's 58 rules (one user rule, 57 game rules) plus scheme 0: 59 names, so more than the page's fifteen rows (a scroll bar of 44 steps).
        Assert.AreEqual((59, 1, 57), (listed.Count, listed.Count(n => n is >= 1 and < 50), listed.Count(n => n >= 50)));
        Assert.IsTrue(rules.SelectMany(file => file.Messages.Select(message => message.Number)).All(number => listed.Contains(number)), "every scheme with a rule has a name");
        // The page lists the schemes in ascending order.
        CollectionAssert.AreEqual(listed.Order().ToArray(), listed.ToArray());
    }
}
