using Arcanum.Data.Area;
using Arcanum.Session;
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
        if (!_gameSettings.AutoSwitchWeapons || wear is null || _areaSession is not { } owner || _inventoryHost is not { } host) return null;
        if (!wear.Steps.Any(step => step is { Kind: SessionWearStepKind.AttackerWeapon, Skip: SessionWearSkip.None, Item.Break: not Arcanum.Simulation.Items.ItemBreakStep.None })) return null;
        SessionWieldTarget? target = _worldActor.Session is AreaGridSession session && session.Chart.ToWorld(session.State.CommittedCell) is var cell
            ? new SessionWieldTarget(checked((int)Chebyshev(struck, (cell.X, cell.Y))), magneticInversion) : null;
        var result = owner.WieldBestWeapon(owner.Version, entity => host.Wearable(owner, entity, _areaKitWear), (_, _) => true, host.InsertCheck, target);
        var wielded = result.Steps.FirstOrDefault(step => step.Wielded is not null)?.Wielded;
        GD.Print($"ARC-COMBAT-1720 auto-switch struck={struck.Key} target={(target is { } t ? $"{t.Distance}/{t.MagneticInversion}" : "none")} disposition={result.Disposition} failure={result.Failure}"
            + $" wielded={wielded} unsupported=[{string.Join(",", result.Steps.Where(step => step.Unsupported is not null).Select(step => step.Unsupported))}]");
        return wielded;
    }
}
