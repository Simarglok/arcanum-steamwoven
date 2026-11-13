using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-COMBAT-1720 (ledger PI5, OH3): the hero's weapon wearing out wields the best weapon left. The original's weapon wear (0x4B5580) calls the re-wield
// (0x4B83E0) when the attacker's weapon drops below 1 hit point: for a PC, with the "auto switch" setting on (0x4B8330), it asks the best-item call
// (0x465170) for the weapon slot against the struck object. The wear itself is the session's (AttackCritter / AttackObject); this host reads the setting at
// each wear, as the taunts' host does, and asks the session for the re-wield with the target's distance.
public partial class TerrainViewerApp
{
    /// <summary>
    /// After an attack of the hero on <paramref name="struck"/>: when the setting is on and the attack's wear broke (or deleted) the hero's weapon, wields the best
    /// weapon the hero holds for that target (<see cref="GameSessionOwner.WieldBestWeapon"/>). The cell distance is the Chebyshev one the attack reads; without a
    /// hero cell the choice has no target. Returns the entity wielded, or null.
    /// </summary>
    private string? HeroWeaponWornOut(SessionAttackWearResult? wear, AreaObjectRecord struck, bool magneticInversion)
    {
        if (!_gameSettings.AutoSwitchWeapons || wear is null) return null;
        if (!wear.Steps.Any(step => step is { Kind: SessionWearStepKind.AttackerWeapon, Skip: SessionWearSkip.None, Item.Break: not Arcanum.Simulation.Items.ItemBreakStep.None })) return null;
        return HeroWieldBest("wear", struck, magneticInversion);
    }

    /// <summary>
    /// ARC-COMBAT-1738 (OH3 case a, 0x4B3170 → 0x4B83E0): the hero's attack the ammunition check refused (<see cref="AttackRefusal.OutOfAmmunition"/>, the test 0x4B65D0 answers
    /// 0) wields the best weapon for the struck object when the setting is on. Returns the entity wielded, or null.
    /// </summary>
    private string? HeroOutOfAmmunition(AttackRefusal? refusal, AreaObjectRecord struck, bool magneticInversion) =>
        _gameSettings.AutoSwitchWeapons && refusal == AttackRefusal.OutOfAmmunition ? HeroWieldBest("no-ammunition", struck, magneticInversion) : null;

    /// <summary>
    /// ARC-COMBAT-1738 (OH3 case c, the damage step 0x4B4390, messages 0x13, 0x16 and 0x17): an attack that struck the hero (an NPC's hit, an arrow, a throw) and took its
    /// wielded weapon (the "drop weapon" critical, the "destroy weapon" and "explode weapon" criticals, the "damage weapon" critical that broke it) wields the best weapon
    /// the hero still holds, with the attacker as the hint for the choice. Returns the entity wielded, or null.
    /// </summary>
    private string? HeroWeaponLostToStrike(SessionAttackResult result, AreaObjectRecord? attacker, bool magneticInversion) =>
        _gameSettings.AutoSwitchWeapons && AreaHeroWeaponLoss.TookStruckWeapon(result.Recipient, result.Criticals, result.Wear) ? HeroWieldBest("struck", attacker, magneticInversion) : null;

    private string? HeroWieldBest(string why, AreaObjectRecord? hint, bool magneticInversion)
    {
        if (_areaSession is not { } owner || _inventoryHost is not { } host) return null;
        SessionWieldTarget? target = hint is not null && _worldActor.Session is AreaGridSession session && session.Chart.ToWorld(session.State.CommittedCell) is var cell
            ? new SessionWieldTarget(checked((int)Chebyshev(hint, (cell.X, cell.Y))), magneticInversion) : null;
        var result = owner.WieldBestWeapon(owner.Version, entity => host.Wearable(owner, entity, _areaKitWear), (_, _) => true, host.InsertCheck, target);
        var wielded = result.Steps.FirstOrDefault(step => step.Wielded is not null)?.Wielded;
        GD.Print($"ARC-COMBAT-1720 auto-switch why={why} hint={hint?.Key.ToString() ?? "none"} target={(target is { } t ? $"{t.Distance}/{t.MagneticInversion}" : "none")} disposition={result.Disposition} failure={result.Failure}"
            + $" wielded={wielded} unsupported=[{string.Join(",", result.Steps.Where(step => step.Unsupported is not null).Select(step => step.Unsupported))}]");
        return wielded;
    }
}
