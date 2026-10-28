using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Combat;

/// <summary>Opt-in ARC-COMBAT-017 census (DAT_ROOT = the game folder): the weapon prototypes with the "default throws" flag (0x200,
/// thrown at the attack start, ledger RH2) or the boomerang flag (0x40), with their skill, range, weight, projectile art, no-drop
/// flag and whether a throw script (point 6) is attached.</summary>
[TestClass]
public sealed class ThrownWeaponCensusTests
{
    private const int ThrowScriptPoint = 6;
    private const uint NoDropFlag = 0x20;

    [TestMethod, TestCategory("RealData")]
    public void Thrown_weapons_are_listed_with_their_throw_facts()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in census."); return; }
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var prototypes = ContentPrototypes.Open(stack);
        var weapons = prototypes.Numbers.Select(number => (number, fields: CombatFieldReader.ReadPrototype(prototypes.Read, number).Fields))
            .Where(p => p.fields?.Weapon is not null).ToArray();
        var thrown = weapons.Where(p => (p.fields!.Weapon!.Flags & (CombatWeaponFlags.DefaultThrows | CombatWeaponFlags.Boomerangs)) != 0).ToArray();
        foreach (var (number, fields) in thrown)
        {
            var weapon = fields!.Weapon!;
            var points = fields.Object.ScriptPoints;
            Console.WriteLine($"ARC-COMBAT-017 thrown prototype={number} flags={weapon.Flags} skill={weapon.Skill} range={weapon.Range}"
                + $" weight={fields.Item!.Weight} art={weapon.MissileArt} nodrop={(fields.Item.Flags & NoDropFlag) != 0}"
                + $" throw-script={points.Contains(ThrowScriptPoint)} points={string.Join(",", points)} ammo={weapon.AmmoType}");
        }
        Console.WriteLine($"ARC-COMBAT-017 weapons={weapons.Length} default-throws={thrown.Count(p => p.fields!.Weapon!.Flags.HasFlag(CombatWeaponFlags.DefaultThrows))}"
            + $" boomerangs={thrown.Count(p => p.fields!.Weapon!.Flags.HasFlag(CombatWeaponFlags.Boomerangs))}"
            + $" throwing-skill={weapons.Count(p => p.fields!.Weapon!.Skill == CombatWeaponSkill.Throwing)}");
        Assert.IsTrue(weapons.Length > 0);
    }
}
