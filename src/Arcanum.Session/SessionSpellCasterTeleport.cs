using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-038 (MA-5 second half; ledger TP1–TP5): what a teleport asks of the shown map's world. <see cref="Allowed"/> is the anti-teleport test (0x4BDD30) for an object, with an optional target
/// location (the second measure of that test); <see cref="Portal"/> is the area choice of the area-portal helper (0x452650): the area the object stands in (on the start map the area whose radius covers
/// its tile, 0x4CB4D0; on any other map the map's own area from the map list, 0x40FFE0), whether the object may teleport, and the area's location on the start map (0x4CAED0).
/// </summary>
public interface ISpellTeleportHost
{
    bool Allowed(string subject, SpellLocation? target);

    SpellPortalPlan Portal(string subject);
}

/// <summary>The area helper's plan: the area (0 for none: the original shows message 10000 to the local player and stops), whether the anti-teleport test allows the object, and the area's location.</summary>
public readonly record struct SpellPortalPlan(int Area, bool Allowed, SpellLocation Destination);

/// <summary>
/// ARC-MAGIC-038 (MA-5 second half, owner decision D3b; the executable read ledger TP1–TP5, 0x4524C0 with its helper 0x452650 and the world map's teleport mode 0x560790 / 0x5618B0): the Movement
/// component's types 2 and 3. Both need the current target object and a run whose resistance margin is 0, like the other types.
/// <list type="bullet">
/// <item><b>Teleport_Tile</b> (type 2, Teleportation, spell 4): the anti-teleport test of the run's **parent**; a refusal shows message 10000 (to the local player's screen, whoever the parent is). Otherwise,
/// in a single-player game, the component moves nothing itself: it calls the world map's teleport mode (0x4608B0 → 0x560790) with the run's source and spell. The host logs <see cref="SpellCastEventKind.TeleportMap"/>
/// (the source as the object, the spell in the value); the player picks a known area there, the second anti-teleport measure of that area's location follows, and the cost the run's BEGIN did not charge
/// (the record's <c>ChargeBeginCost: 0</c>) is paid by <see cref="PayTeleportCost"/> (0x4507D0) when the source is the local player.</item>
/// <item><b>Teleport_Area_Portal</b> (type 3, technology record 218, helper 0x452650): the area of the object (see <see cref="ISpellTeleportHost.Portal"/>); no area, or a refusal by the test, shows message 10000 when
/// the object is the local player and does nothing; otherwise the object is sent to the start map at the area's location with the flags 0x120 (no wait orders, blank screen), the cache emptied first:
/// the host logs <see cref="SpellCastEventKind.TeleportPortal"/> (the object, the location's x in the value and y in the detail) and the viewer makes the change of place.</item>
/// </list>
/// The host branch of the original (a multiplayer host: the request goes to the current map) is not built; the game is single-player.
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The magic message file's line the anti-teleport refusals show (0x4524C0, 0x452650, 0x4564E0).</summary>
    public const int TeleportRefusedMessage = 10000;

    /// <summary>The teleport host of the shown map (<see cref="ISpellTeleportHost"/>); null when there is none: types 2 and 3 are not ready then and the invocation check allows every teleport.</summary>
    public ISpellTeleportHost? Teleport { get; set; }

    /// <summary>The invocation check's anti-teleport answer (0x4564E0): only spell 4 asks, for its parent, and only with a teleport host.</summary>
    private bool TeleportAllowedFor(int spell, string? parent) =>
        spell != SpellInvocationRules.Teleportation || Teleport is null || parent is null || Teleport.Allowed(parent, null);

    private SpellComponentOutcome? TeleportAreaPortalComponent(SpellRunObject target)
    {
        if (Teleport is not { } host) return null;
        var plan = host.Portal(target.Id);
        if (plan.Area < 1 || !plan.Allowed)
        {
            if (target.Id == Hero) _log.Add(new(SpellCastEventKind.Message, -1, TeleportRefusedMessage));
            return SpellComponentOutcome.None;
        }
        _log.Add(new(SpellCastEventKind.TeleportPortal, -1, unchecked((int)plan.Destination.X), target.Id, unchecked((int)plan.Destination.Y)));
        return SpellComponentOutcome.None;
    }

    private SpellComponentOutcome? TeleportTileComponent(SpellRun run)
    {
        if (Teleport is not { } host) return null;
        // 0x4BDD30 on the run's parent handle (zero refuses: no object).
        if (run.Parent is not { } parent || !host.Allowed(parent.Id, null))
        {
            _log.Add(new(SpellCastEventKind.Message, run.Id, TeleportRefusedMessage));
            return SpellComponentOutcome.None;
        }
        _log.Add(new(SpellCastEventKind.TeleportMap, run.Id, run.Spell, run.Source?.Id));
        return SpellComponentOutcome.None;
    }

    /// <summary>
    /// The world map's teleport mode asks the host to pay the spell once an area is chosen (0x5618B0 → 0x4507D0 → 0x450420): the run's cost of <paramref name="spell"/> (<see cref="SpellCostRules.RunCost"/>, applied)
    /// for <paramref name="source"/>, only when it is the local player (the original tests the object it was handed with 0x40D9F0). False when the payer cannot pay: nothing is taken then; the original ignores the answer (0x5618B0 calls 0x4507B0, the thunk of 0x4507D0) and lands the hero anyway, so the viewer does too.
    /// </summary>
    public bool PayTeleportCost(string source, int spell)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source != Hero) return true;
        if (RunObject(source) is not { } who || Payer(who) is not { } payer) return false;
        var record = Record(spell);
        var cost = SpellCostRules.RunCost(record.Cost, spell, Caster(who), false);
        var payment = SpellPaymentRules.Pay(payer, cost, true, spell, record.Payment, Cells);
        Pay(-1, source, payment);
        return payment.Succeeded;
    }
}
