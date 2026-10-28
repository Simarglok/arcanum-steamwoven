using Arcanum.Data.Content;
using Arcanum.Data.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Magic;

/// <summary>ARC-MAGIC-017 opt-in check of the installed original (DAT_ROOT = the game folder): the spells' eye-candy table against
/// the census in docs/formats/spell-eye-candy.md. Counts only; no game text.</summary>
[TestClass]
public sealed class SpellEyeCandyRealDataTests
{
    [TestMethod, TestCategory("RealData")]
    public void The_installed_eye_candy_table_matches_the_census()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in eye-candy check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var read = SpellEyeCandyTable.Open(stack);
        Assert.AreEqual(SpellDataStatus.Read, read.Status, $"{read.Refusal}");
        var entries = read.Table!.Entries;
        Assert.AreEqual((SpellRecord.Count * SpellEyeCandyTable.PerSpell, 364), (entries.Count, entries.Count(e => e.Art is not null)));
        Assert.AreEqual("103/12/176/64/4/5", string.Join('/', Enumerable.Range(0, 6).Select(k => entries.Where((e, i) => i % 6 == k).Count(e => e.Art is not null))));
        Assert.AreEqual(80, Enumerable.Range(0, SpellRecord.CollegeSpells).Count(s => read.Table.Entry(s, SpellEyeCandyTable.Cast).Art is not null),
            "every college spell has a cast eye candy");
        Assert.AreEqual(0, Enumerable.Range(0, SpellRecord.CollegeSpells).Count(s => read.Table.Entry(s, SpellEyeCandyTable.SecondaryCast).Art is not null));
        Assert.AreEqual((356, 55, 249), (entries.Count(e => e.Art is { } a && EyeCandyArt.Translucent(a)),
            entries.Count(e => e.Art is { } a && EyeCandyArt.Scale(a) != EyeCandyArt.UnscaledIndex), entries.Count(e => e.Light is not null)));
        var withArt = entries.Where(e => e.Art is not null).ToArray();
        Assert.AreEqual(0, withArt.Count(e => (e.Flags & SpellEyeCandyFlags.Underlay) != 0 && (e.Flags & (SpellEyeCandyFlags.Foreground | SpellEyeCandyFlags.Background)) != 0),
            "no installed entry has an underlay beside an overlay");
        Assert.AreEqual(2, withArt.Count(e => (e.Flags & (SpellEyeCandyFlags.Foreground | SpellEyeCandyFlags.Background | SpellEyeCandyFlags.Underlay)) == 0),
            "two entries name an art without a part (nothing is placed)");
        Assert.AreEqual(64, withArt.Count(e => (e.Flags & SpellEyeCandyFlags.Forever) != 0));
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_missiles_match_the_census()
    {
        // ARC-MAGIC-018: the projectile entries (6·spell + 1) the casting goal launches as missiles (0x429C40); counts only.
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in eye-candy check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var table = SpellEyeCandyTable.Open(stack).Table!;
        var missiles = Enumerable.Range(0, SpellRecord.Count).Select(s => (Spell: s, Entry: table.Entry(s, SpellEyeCandyTable.Projectile)))
            .Where(m => m.Entry.Art is not null).ToArray();
        Assert.AreEqual((12, 6), (missiles.Length, missiles.Count(m => m.Spell < SpellRecord.CollegeSpells)), "12 missiles, 6 of college spells");
        Assert.AreEqual("0,2,16,22,33,34", string.Join(',', missiles.Where(m => m.Spell < SpellRecord.CollegeSpells).Select(m => m.Spell)));
        Assert.AreEqual((11, 1, 0), (missiles.Count(m => m.Entry.ProjectileSpeed == 0), missiles.Count(m => m.Entry.ProjectileSpeed == 4),
            missiles.Count(m => m.Entry.ProjectileSpeed < 0)), "one speed of 4 steps, the rest take the default (4); none negative");
        Assert.IsTrue(missiles.All(m => EyeCandyArt.IsEyeCandy(m.Entry.Art!.Value) && EyeCandyArt.Rotation(m.Entry.Art.Value) == 0
            && EyeCandyArt.Number(m.Entry.Art.Value) + 3 < 0x200), "eye-candy arts whose four faced numbers fit");
    }
}
