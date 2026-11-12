namespace Arcanum.Simulation.Combat;

/// <summary>What the Decay Dead Bodies event does when it fires for a critter (0x45EB50, ledger PO10).</summary>
public enum BodyDecayStep
{
    /// <summary>The critter is dead: its whole inventory leaves it (each item is placed by the drop rule, or destroyed when it is marked "not droppable"), then the critter is destroyed.</summary>
    DropAndDestroy,
    /// <summary>The critter is not dead (it was healed back to life since): the original answers an error line and the handler returns 0; the event is consumed, the critter stays.</summary>
    NotDead,
}

/// <summary>
/// ARC-WORLD-1158 (E2-b): the original's body decay of a dead critter (read in the retail 1.0.7.4 executable, ledger
/// "Generators, encounter creatures and body decay" PO10). The death routine queues the Decay Dead Bodies event (kind 24, game
/// clock) for a PC or NPC one game day on (<see cref="BloodPoolRules.CritterDecayDelayMs"/>) unless the critter's critter flags 2
/// carry the "no decay" bit <see cref="NoDecayCritterFlag2"/> (and unless the object is a prototype). When it fires, a dead critter
/// drops its inventory and is destroyed. The delay and the pool's own deletion are <see cref="BloodPoolRules"/>'s.
/// </summary>
public static class BodyDecayRules
{
    /// <summary>The critter flag 2 bit (field 0xDB, 0x40; the rules-text parser's name table 0x5BA3C8) that keeps a dead critter from decaying.</summary>
    public const uint NoDecayCritterFlag2 = 0x40;

    /// <summary>Whether the death queues the event: not for a critter with <see cref="NoDecayCritterFlag2"/>. The delay is one game day.</summary>
    public static bool Schedules(uint critterFlags2) => (critterFlags2 & NoDecayCritterFlag2) == 0;

    /// <summary>The firing for a critter (the Decay Dead Bodies handler, ledger PO10): a dead one is dropped and destroyed, one that is alive again is left as it is.</summary>
    public static BodyDecayStep Fire(bool dead) => dead ? BodyDecayStep.DropAndDestroy : BodyDecayStep.NotDead;
}
