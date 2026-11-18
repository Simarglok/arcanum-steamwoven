using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-COMBAT-1932 opt-in (<c>DAT_ROOT</c>, the game folder): the census of the installed critter, monster and unique NPC body ARTs that have
/// no attack animation file. An NPC's attack goal (node 0x42B9C0) wants the attacker's art with animation 14, 20 or 21; a missing low attack
/// (21) is repaired to the attack (20) for bare hands and weapons 1..8, 10 and 13 only (MA1), and a file that is still missing ends the goal
/// with no swing. Names only (no original content): a body is a (folder, name stem) group of ART files named
/// <c>{stem}{x|s}{weapon letter}{animation letter}.art</c>.
/// </summary>
[TestClass]
public sealed class AttackArtCensusRealDataTests
{
    private static readonly string[] Folders = ["art/critter/", "art/monster/", "art/unique_npc/"];

    [TestMethod, TestCategory("RealData")]
    public void Installed_bodies_by_the_attack_animations_their_ARTs_hold()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var lines = new List<string>();
        var counts = new Dictionary<string, (int Bodies, int WithBare, int NoBareAttack, int NoThrow, string WeaponGaps)>();
        foreach (var folder in Folders)
        {
            var bodies = new Dictionary<string, HashSet<(char Shield, char Weapon, char Animation)>>();
            foreach (var key in stack.Keys(folder).Where(k => k.EndsWith(".art", StringComparison.Ordinal) && k.Length >= folder.Length + 8))
            {
                var stem = key[..^7];
                var (shield, weapon, animation) = (key[^7], key[^6], key[^5]);
                if (shield is not ('x' or 's')) continue;
                if (!bodies.TryGetValue(stem, out var set)) bodies[stem] = set = [];
                set.Add((shield, weapon, animation));
            }
            // Weapon letter a = none (stand and walk out of combat mode), b = bare hands, c.. = weapons; animation letter a = stand, o = throw (14),
            // u = attack (20), v = low attack (21). A body's weapon slot is "present" when any ART of it names that weapon letter.
            var withBare = bodies.Where(b => b.Value.Contains(('x', 'b', 'a'))).ToArray();
            var noBareAttack = withBare.Where(b => !b.Value.Contains(('x', 'b', 'u'))).ToArray();
            var noBareAnyAttack = noBareAttack.Where(b => !b.Value.Contains(('x', 'b', 'v'))).ToArray();
            var noThrow = bodies.Where(b => !b.Value.Contains(('x', 'a', 'o'))).ToArray();
            var weaponGaps = new SortedDictionary<char, int>();
            var lowGaps = new SortedDictionary<char, int>();
            foreach (var (_, set) in bodies)
                foreach (var weapon in set.Where(t => t.Shield == 'x' && t.Weapon != 'a').Select(t => t.Weapon).Distinct())
                {
                    if (!set.Contains(('x', weapon, 'u'))) weaponGaps[weapon] = weaponGaps.GetValueOrDefault(weapon) + 1;
                    if (set.Contains(('x', weapon, 'v')) && !set.Contains(('x', weapon, 'u'))) lowGaps[weapon] = lowGaps.GetValueOrDefault(weapon) + 1;
                }
            var withStand = withBare;
            counts[folder] = (bodies.Count, withBare.Length, noBareAttack.Length, noThrow.Length, string.Join(",", weaponGaps.Select(g => $"{g.Key}{g.Value}")));
            lines.Add($"{folder} bodies={bodies.Count} with bare-hand stand={withStand.Length} but no bare-hand attack(20)={noBareAttack.Length}"
                + $" (and no low attack(21) either={noBareAnyAttack.Length}); no throw ART(14)={noThrow.Length} | weapon slots present without attack by weapon: {string.Join(", ", weaponGaps.Select(g => $"{g.Key}={g.Value}"))} | low attack only: {string.Join(", ", lowGaps.Select(g => $"{g.Key}={g.Value}"))}");
            if (noBareAttack.Length > 0)
                lines.Add($"  e.g. {string.Join(", ", noBareAttack.Take(12).Select(b => b.Key))}");
        }
        foreach (var line in lines) Console.WriteLine("ARC-COMBAT-1932 " + line);
        // First observation 2026-11-18 (installed 1.0.7.4 data). Every critter and monster body has the bare-hand attack; one unique NPC body (hopuw)
        // has no attack ART of any kind, so its attack goal fails at node 5 (ledger VC38, VCG11); a few weapon slots have no attack (see the lines).
        Assert.AreEqual((45, 44, 0, 1, ""), counts["art/critter/"], "the installed critter bodies changed: update ledger VCG11");
        Assert.AreEqual((34, 34, 0, 27, "x4,y1"), counts["art/monster/"], "the installed monster bodies changed: update ledger VCG11");
        Assert.AreEqual((40, 35, 1, 24, "b1,x1,y1"), counts["art/unique_npc/"], "the installed unique NPC bodies changed: update ledger VCG11");
    }
}
