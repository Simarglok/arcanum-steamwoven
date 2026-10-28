namespace Arcanum.Simulation.Magic;

/// <summary>What the spell UI's activation of a spell does (0x57BC70).</summary>
public enum SpellActivationKind
{
    /// <summary>Nothing: no caster, a caster that is not the player's PC or is switched off, dead or unconscious, or a disabled
    /// spell (<c>Disabled:</c>).</summary>
    Nothing,
    /// <summary>The spell is cast at once at the caster's tile: its BEGIN area is 0, exactly the radius bit, or has the self bit
    /// without the tile bit.</summary>
    CastAtCaster,
    /// <summary>The targeting cursor opens with <see cref="SpellActivation.Mask"/>.</summary>
    Target,
    /// <summary>Shift was held for an object spell that may target its caster: the targeting cursor opens and the spell is cast
    /// at once at the local PC as the target object.</summary>
    CastAtPlayer,
    /// <summary>The cast's action points refuse (only in a turn-based round, in the caster's subturn): no targeting cursor.</summary>
    NoActionPoints,
}

/// <summary>The activation's answer and the targeting cursor's mask.</summary>
public readonly record struct SpellActivation(SpellActivationKind Kind, ulong Mask = 0);

/// <summary>
/// ARC-MAGIC-015 (magic plan MT20a-2): the spell UI's rules as the 1.0.7.4 executable has them (ledger "Spell UI", SU rows): the
/// activation of a spell from the spell pane (0x57BC70), the targeting cursor's mask and its Alt toggle (0x57C0B0, 0x57C0E0), and
/// the message a click with no valid target shows (0x54DE50, 0x57BC30). The cast at the picked target (0x57C110) is the cast host's
/// (<c>SessionSpellCaster.CastAt</c>). Pure functions; the host asks the session for the caster's state and the action points.
/// </summary>
public static class SpellUiRules
{
    /// <summary>Target-mask bits the UI tests: <c>Tgt_Self</c>, the object bit (<c>Tgt_Object</c>), <c>Tgt_Obj_Radius</c>, the
    /// <c>Tgt_Obj_Damaged</c> test's own bit, the inventory bit of <c>Tgt_Obj_Inven</c>, <c>Tgt_Obj_No_Self</c>'s own bit, the tile
    /// bit (<c>Tgt_Tile</c>) and <c>Tgt_Non_Party_Critters</c>'s own bit.</summary>
    public const ulong SelfBit = 0x1, ObjectBit = 0x4, RadiusBit = 0x10, DamagedBit = 0x100000, InventoryBit = 0x2000000, NoSelfBit = 0x10000000,
        TileBit = 0x8000UL << 32, NonPartyBit = 0x10000000UL << 32;

    /// <summary>spell.mes messages of the UI: the caster cannot afford the spell (600), no line of fire (604), a target that is not
    /// damaged (606).</summary>
    public const int CannotAffordMessage = 600, NoLineOfFireMessage = 604, NotDamagedMessage = 606;

    /// <summary>
    /// The activation (0x57BC70) for the player's PC: nothing when the caster cannot act (<paramref name="casterActive"/>: present,
    /// the local player's PC, not switched off, not dead, not unconscious) or the spell is disabled. The record's BEGIN area 0,
    /// exactly the radius bit, or with the self bit and without the tile bit casts at once at the caster's tile. Otherwise the cast's
    /// action points are checked (<paramref name="actionPointsAllow"/>: 4 in a turn-based round, in the caster's subturn; outside a
    /// round always) and the targeting cursor opens with <see cref="TargetingMask"/>; with Shift held and the record's object bit, a
    /// record without the no-self bit is then cast at once at the local PC. An inventory spell's opening of the inventory is the
    /// host's (the original opens it before these tests).
    /// </summary>
    [OriginalFunction(0x57BC70, OriginalFunctionScope.Partial)]
    public static SpellActivation Activate(bool casterActive, bool enabled, ulong begin, bool aggressive, bool altHeld, bool shiftHeld, bool actionPointsAllow)
    {
        if (!casterActive || !enabled) return new(SpellActivationKind.Nothing);
        var mask = TargetingMask(begin, aggressive, altHeld);
        if (CastsAtOnce(begin)) return new(SpellActivationKind.CastAtCaster, mask);
        if (!actionPointsAllow) return new(SpellActivationKind.NoActionPoints, mask);
        return shiftHeld && (begin & ObjectBit) != 0 && (begin & NoSelfBit) == 0 ? new(SpellActivationKind.CastAtPlayer, mask)
            : new(SpellActivationKind.Target, mask);
    }

    /// <summary>Whether a BEGIN area needs no target (the activation's and the release re-check's test, 0x57BC70, 0x456A90): 0,
    /// exactly the radius bit, or the self bit without the tile bit.</summary>
    public static bool CastsAtOnce(ulong begin) => begin == 0 || begin == RadiusBit || (begin & SelfBit) != 0 && (begin & TileBit) == 0;

    /// <summary>Whether the inventory opens for the spell's target (the inventory bit of the BEGIN area, 0x57BC70).</summary>
    public static bool TargetsInventory(ulong begin) => (begin & InventoryBit) != 0;

    /// <summary>The targeting cursor's mask (0x57BC70): the record's BEGIN area, plus the non-party bit for an aggressive spell
    /// while no Alt key is held, so the cursor spares the caster's party.</summary>
    public static ulong TargetingMask(ulong begin, bool aggressive, bool altHeld) => aggressive && !altHeld ? begin | NonPartyBit : begin;

    /// <summary>An Alt key pressed while the cursor is open (0x57C0B0): an aggressive spell's mask loses the non-party bit.</summary>
    [OriginalFunction(0x57C0B0, OriginalFunctionScope.Full)]
    public static ulong AltPressed(ulong mask, bool aggressive) => aggressive ? mask & ~NonPartyBit : mask;

    /// <summary>The last Alt key released while the cursor is open (0x57C0E0): an aggressive spell's mask gains the non-party bit.</summary>
    [OriginalFunction(0x57C0E0, OriginalFunctionScope.Full)]
    public static ulong AltReleased(ulong mask, bool aggressive) => aggressive ? mask | NonPartyBit : mask;

    /// <summary>A click with no valid target (0x54DE50, 0x57BC30): the area test's reason "not damaged" shows spell.mes 606; any other
    /// shows nothing, and the cursor stays.</summary>
    [OriginalFunction(0x57BC30, OriginalFunctionScope.Partial)]
    public static int? NoTargetMessage(ulong reason) => reason == DamagedBit ? NotDamagedMessage : null;

    /// <summary>ARC-MAGIC-019 (SU13): a cast while the caster's casting goal (26 or 27) plays is queued beneath it unless the playing
    /// goal's stack index (the goals beneath it, the queued casts, plus its own subgoals) is this or more (0x455C30 through 0x4373A0).
    /// The insertion's own limit (0x44D9B0: index 7 of an 8-goal stack) is never reached from here.</summary>
    public const int QueueRefusedFromDepth = 4;

    /// <summary>
    /// ARC-MAGIC-019 (SU13, 0x455C30's queue branch): whether a cast whose run started while the caster's casting goal plays is
    /// refused rather than queued: at a stack index of <see cref="QueueRefusedFromDepth"/> or more (<paramref name="depth"/>), or
    /// while turn-based combat is on (0x4B6D70: any round, whoever's turn). A refusal frees the run (0x4558D0) and shows nothing.
    /// </summary>
    public static bool QueueRefused(int depth, bool turnBased) => depth >= QueueRefusedFromDepth || turnBased;
}
