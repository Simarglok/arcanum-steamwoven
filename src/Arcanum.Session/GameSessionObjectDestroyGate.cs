using Arcanum.Simulation.Combat;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    private Func<SessionObjectTarget, bool>? _objectDestroyGate;

    /// <summary>
    /// ARC-SCRIPT-2644 (ledger TD2, OD7–OD11, BS2/BS3c): attaches the host's answer to the break's object destroy. The original's break
    /// (0x43F1C0) calls the object destroy (0x43CCA0) for a door, a container (after its drop), a trap with no destroyed ART and a scenery that is already busted;
    /// that destroy first runs the object's destroy point (2), and a 0 answer leaves the object whole (TD2). The session writes such a break's
    /// destroyed condition only when <paramref name="gate"/> answers true for the target (it runs the destroy point; a host that has no script for the object,
    /// or the object not on its map, answers true). On false the object keeps its condition (the hit's damage and a container's spill are written as the
    /// break does before the destroy), and the result's host steps leave out <see cref="SessionObjectHostSteps.ObjectDestroyed"/> (the sound and the
    /// spill eye candy still play: the original plays them after the destroy returns). Not state: it is never saved and is the host's to attach again after a restore; null
    /// detaches it, and without one every such break destroys, as before. The gate is called once per break, after a container's generation and the spill walk's budgets have admitted the break and before the break's own writes (the hit's damage, the condition, the spill),
    /// so a destroy point's effects are applied first and it sees the object as it was before the hit's write (a recorded difference from the original, which writes the damage and drops the items before the destroy).
    /// </summary>
    public void AttachObjectDestroyGate(Func<SessionObjectTarget, bool>? gate)
    {
        ThrowIfDisposed();
        _objectDestroyGate = gate;
    }

    /// <summary>Whether the break's destroy branch may destroy: false when the attached gate refused (the object's destroy point answered 0, TD2).</summary>
    private bool BreakMayDestroy(SessionObjectTarget target, ObjectHitOutcome hit) =>
        !(hit.Break is ObjectBreak.Destroyed or ObjectBreak.ContainerSpilled) || _objectDestroyGate is not { } gate || gate(target);

    private Func<string, bool>? _itemDestroyGate;

    /// <summary>
    /// ARC-SCRIPT-2722 (ledger OD12, TD2): attaches the host's answer to the break's item deletion. An item that is hit again after its first break is deleted through the object destroy, which runs the item's
    /// destroy point (2) first; a 0 answer keeps it (<see cref="ItemDurabilityResult.DestroyKept"/>: the item stays at 0 hit points, still broken). Asked for the break of an item after a hit (<c>AttackObject</c> on an item,
    /// <see cref="DamageItemUnattacked"/>) and, since ARC-SCRIPT-2749, for an item the wear paths delete (<see cref="ApplyAttackWear"/>, <see cref="ApplyArmorWear"/>), once per deleting break and before the deletion is written; <paramref name="gate"/> gets the item's entity and answers true to delete (an item the host holds no script for answers true).
    /// Not state: never saved, the host attaches it again after a restore; null detaches it, and without one every deleting break deletes.
    /// </summary>
    public void AttachItemDestroyGate(Func<string, bool>? gate)
    {
        ThrowIfDisposed();
        _itemDestroyGate = gate;
    }
}
