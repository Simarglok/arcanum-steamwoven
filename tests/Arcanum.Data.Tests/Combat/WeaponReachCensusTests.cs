using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Combat;

/// <summary>Opt-in ARC-COMBAT-012 census (DAT_ROOT = the game folder): the weapon prototypes whose range is above 1, by skill (a
/// melee-skill one takes the original's ranged path, ledger RA rows).</summary>
[TestClass]
public sealed class WeaponReachCensusTests
{
    [TestMethod, TestCategory("RealData")]
    public void Weapons_with_reach_above_one_are_counted_by_skill()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census."); return; }
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var prototypes = ContentPrototypes.Open(stack);
        var weapons = prototypes.Numbers.Select(number => (number, fields: CombatFieldReader.ReadPrototype(prototypes.Read, number).Fields))
            .Where(p => p.fields?.Weapon is not null).ToArray();
        var reach = weapons.Where(p => p.fields!.Weapon!.Range > 1).ToArray();
        foreach (var group in reach.GroupBy(p => p.fields!.Weapon!.Skill).OrderBy(g => g.Key))
            Console.WriteLine($"ARC-COMBAT-012 reach skill={group.Key} weapons={group.Count()} ammo-none={group.Count(p => p.fields!.Weapon!.AmmoType == CombatFieldReader.NoAmmo || p.fields.Weapon.AmmoType < 0)}"
                + $" ranges={string.Join(",", group.Select(p => p.fields!.Weapon!.Range).Distinct().Order())} prototypes={string.Join(",", group.Take(12).Select(p => p.number))}");
        Console.WriteLine($"ARC-COMBAT-012 weapons={weapons.Length} reach>1={reach.Length}");
        // The melee ones by their description (OBJ_F_DESCRIPTION through mes/description.mes).
        var descriptions = stack.Resolve("mes/description.mes").Winner is { } winner && stack.Read(winner).Bytes is { } bytes
            ? Arcanum.Data.Messages.MesFile.Parse(bytes).File : null;
        foreach (var (number, fields) in reach.Where(p => p.fields!.Weapon!.Skill == CombatWeaponSkill.Melee))
        {
            var description = fields!.Object.Description;
            var text = descriptions?.Messages.FirstOrDefault(m => m.Number == description) is { } message ? System.Text.Encoding.Latin1.GetString(message.Bytes.Span) : "?";
            Console.WriteLine($"ARC-COMBAT-012 melee-reach prototype={number} description={description} text={text} flags={fields.Weapon!.Flags} speed={fields.Weapon.SpeedFactor}");
        }
        Assert.IsTrue(weapons.Length > 0);
    }
}
