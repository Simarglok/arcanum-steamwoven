using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Scripts;

/// <summary>Opt-in check of the installed original (DAT_ROOT = the game folder) against the
/// ARC-RES-045 census (docs/research/scr-census.md).</summary>
[TestClass]
public sealed class ScrProgramRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_installed_script_frames_with_the_census_counts()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in SCR framing check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        var programs = new List<ScrProgram>();
        foreach (var key in stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal) && k.Count(c => c == '/') == 1))
        {
            var read = stack.Read(stack.Resolve(key).Winner!);
            Assert.AreEqual(ContentReadStatus.Read, read.Status, key);
            var result = ScrProgram.Read(read.Bytes!);
            Assert.AreEqual(ScrReadStatus.Read, result.Status, key);
            programs.Add(result.Program!);
        }
        Assert.AreEqual(2399, programs.Count);
        Assert.AreEqual(23872, programs.Sum(p => p.Count));
        Assert.AreEqual(221, programs.Max(p => p.Count));
        Assert.AreEqual(32, programs.Count(p => p.Count == 0));
        Assert.AreEqual(7, programs.Count(p => (p.FileFlags & (ScrCatalogue.FileFlag.NonmagicalTrap | ScrCatalogue.FileFlag.MagicalTrap)) != 0));
        // Every type and operand kind a signature reads is inside the catalogue.
        foreach (var line in programs.SelectMany(p => p.Lines))
            foreach (var (side, clause) in new[] { (ScrClauseSide.Condition, line.Condition), (ScrClauseSide.OnTrue, line.OnTrue), (ScrClauseSide.OnFalse, line.OnFalse) })
            {
                var signature = ScrCatalogue.Signature(side, clause.Type);
                Assert.IsNotNull(signature);
                for (var slot = 0; slot < ScrClause.Operands; slot++)
                {
                    if (signature[slot] == ScrOperandRole.Number) Assert.IsTrue(ScrCatalogue.IsKnownValueKind(clause.Kind(slot)));
                    if (signature[slot] == ScrOperandRole.Object) Assert.IsTrue(ScrCatalogue.IsKnownFocusKind(clause.Kind(slot)));
                }
            }
    }
}
