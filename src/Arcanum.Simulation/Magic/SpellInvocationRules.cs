namespace Arcanum.Simulation.Magic;

/// <summary>A tile location as the original packs it: x in the low 32 bits, y in the high 32 bits; (0, 0) is "no location".</summary>
public readonly record struct SpellLocation(uint X, uint Y)
{
    public static SpellLocation None => default;

    public bool IsNone => X == 0 && Y == 0;

    /// <summary>The original's tile distance (0x4B96F0, <see cref="Travel.WorldTravelGeometry.Distance"/>): the larger of the two
    /// coordinate differences, the unsigned coordinates taken in 64 bits.</summary>
    public static long Distance(SpellLocation a, SpellLocation b) => Travel.WorldTravelGeometry.Distance(new(a.X, a.Y), new(b.X, b.Y));
}

/// <summary>
/// One object an invocation names, as the check reads it: its id (identity: the parent and the target are compared by it), its
/// object type (15 PC, 16 NPC, 5..14 items), its location, its object flags (0x1000 in an inventory, 0x20 shoot-through), spell
/// flags, critter flags, effective magick/tech aptitude and effective intelligence.
/// </summary>
public sealed record SpellObjectFacts(string Id, int Type, SpellLocation Location, int ObjectFlags = 0, int SpellFlags = 0, int CritterFlags = 0,
    int Aptitude = 0, int Intelligence = 0)
{
    public const int PcType = 15, NpcType = 16, FirstItemType = 5, LastItemType = 14;
    public const int InInventory = 0x1000, ShootThrough = 0x20;
    /// <summary>Bonds of Magick (spell flag 0x1000): a critter under it cannot cast its own spells.</summary>
    public const int BondsOfMagick = 0x1000;

    public bool IsCritter => Type is PcType or NpcType;
    public bool IsItem => Type is >= FirstItemType and <= LastItemType;
    public bool InAnInventory => (ObjectFlags & InInventory) != 0;
}

/// <summary>
/// The record facts the check reads: technology (`Is_Tech:`, every record from 140), `Range:` (1 before the table loads), the
/// forbidden caster spell flags (`Disallowed_SF:`), target spell flags (`Disallowed_TSF:`) and target critter flags
/// (`Disallowed_TCF:`), and the intelligence a critter needs to cast its own spell (0x4502E0: 5).
/// </summary>
public readonly record struct SpellInvocationRecord(bool Technology, int Range, int DisallowedCasterSpellFlags, int DisallowedTargetSpellFlags,
    int DisallowedTargetCritterFlags, int MinimumIntelligence = SpellBookRules.MinimumIntelligence);

/// <summary>
/// One invocation: the spell, the parent (the caster; null for a sourceless cast), the source (the object whose spell it is: the
/// caster itself for its own spell, else an item), the target object and the target tile (either may be none), the power the
/// parent draws from a source item (0x4614A0, the equipment rules; null when not known, which the check refuses to guess when it
/// needs it), whether the parent may be teleported where it stands (0x4BDD30), whether the parent is the local player (for the
/// anti-teleport message), whether turn-based combat is running and the critter whose turn it is (null: none, which no critter
/// parent matches).
/// </summary>
public sealed record SpellInvocation(int Spell, SpellObjectFacts? Parent, SpellObjectFacts? Source, SpellObjectFacts? TargetObject,
    SpellLocation TargetTile, int? SourceItemPower = null, bool TeleportAllowed = true, bool ParentIsLocalPlayer = false,
    bool TurnBased = false, string? TurnBasedActor = null);

/// <summary>A line-of-fire answer (0x4ADE00): the score (100 or more: no line of fire) and the first blocker on the line, if any.</summary>
public readonly record struct SpellLineOfFire(int Score, SpellObjectFacts? Blocker);

/// <summary>
/// What the check asks of the world: the line of fire from the parent to a tile (0x4ADE00; filled by the NPC AI awareness stage I2,
/// which owns the line of fire, or a later line-of-sight slice; tests use a double), and the NPC fallback's area questions — whether
/// the target matches the spell's BEGIN area and the first matching tile in radius 2 around the target object (filled by the target
/// functions, MT8).
/// </summary>
public interface ISpellInvocationWorld
{
    SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile);

    bool TargetMatchesBeginArea(SpellInvocation invocation);

    /// <summary>Whether the BEGIN area takes tiles (the original's area flag 0x8000 of the second word).</summary>
    bool BeginAreaTakesTiles(int spell);

    /// <summary>The first tile in radius <paramref name="radius"/> around the target object that matches the BEGIN area, or none.</summary>
    SpellLocation FirstTileInBeginArea(SpellInvocation invocation, int radius);
}

/// <summary>Why an invocation is refused, step by step in the original's order, or <see cref="None"/>.</summary>
public enum SpellInvocationRefusal
{
    None,
    /// <summary>In turn-based combat a PC or NPC parent whose turn it is not.</summary>
    NotTheirTurn,
    /// <summary>A spell number outside 0..222 (the original logs it).</summary>
    InvalidSpell,
    /// <summary>The target is beyond the record's <c>Range:</c> (Chebyshev), or has no location.</summary>
    OutOfRange,
    /// <summary>The line of fire scores 100 or more, or its blocker is neither shoot-through nor the target.</summary>
    NoLineOfFire,
    /// <summary>A forbidden caster spell flag, target spell flag or target critter flag.</summary>
    ForbiddenFlags,
    /// <summary>The parent is under Bonds of Magick and casts its own spell.</summary>
    BondsOfMagick,
    /// <summary>An NPC's target fails the BEGIN area and no tile in radius 2 replaces it.</summary>
    NoValidTarget,
    /// <summary>A technologist parent draws no power from the magic item it uses (the "resisted" effect plays).</summary>
    ItemAptitude,
    /// <summary>MG5: a technologist target nullifies the spell by chance — never in the original (the d100 is compared with a
    /// negative value), in both modes.</summary>
    TargetAptitude,
    /// <summary>A critter casting its own spell has less intelligence than 5.</summary>
    Intelligence,
    /// <summary>Teleportation (spell 4) where the parent may not be teleported (the player sees a message).</summary>
    AntiTeleport,
}

/// <summary>
/// The answer of an invocation check: the refusal (or none); the tile that replaced an NPC's target (the target object is then
/// cleared), or none; whether the "resisted" effect plays (the item and target aptitude refusals); whether the player is told the
/// place forbids teleportation.
/// </summary>
public sealed record SpellInvocationResult(SpellInvocationRefusal Refusal, SpellLocation ReplacedTile, bool PlaysResisted, bool ShowsTeleportMessage)
{
    public bool Allowed => Refusal == SpellInvocationRefusal.None;
}

/// <summary>
/// ARC-MAGIC-006 (magic plan MT7): the original's invocation check (0x4564E0), which runs before any run slot or payment, with its
/// range (0x454700), line of fire (0x456BC0), forbidden flags (0x456430) and item gate (0x456A10), as the 1.0.7.4 executable has them
/// (contract docs/world/spell-invocation-runtime.md, ledger "Spell invocation", IV1–IV12). Pure functions over explicit facts; the
/// line of fire and the NPC fallback's area questions come through <see cref="ISpellInvocationWorld"/>, the MG5 d100 from the
/// caller's <see cref="ISpellRollSource"/>. MG5 follows the original in both modes (owner decision 2026-10-20).
/// </summary>
public static class SpellInvocationRules
{
    public const int Spells = 223, Teleportation = 4, FallbackRadius = 2, NoLineOfFire = 100;

    /// <summary>
    /// The check, in the original's order: the turn; the spell number; with a parent, the range; the line of fire; the forbidden
    /// flags; Bonds of Magick on the parent's own spell; a non-PC parent's BEGIN-area fallback (a failed target is replaced by the
    /// first matching tile in radius 2 when the area takes tiles); for a magic record the item aptitude gate and the target aptitude
    /// test (MG5: its d100 is rolled for a technologist target although it never refuses); intelligence 5 for a critter's own spell;
    /// Teleportation where the parent may not be teleported.
    /// </summary>
    [OriginalFunction(0x4564E0, OriginalFunctionScope.Partial)]
    public static SpellInvocationResult Check(SpellInvocation invocation, SpellInvocationRecord record, ISpellInvocationWorld world, ISpellRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rolls);
        var parent = invocation.Parent;
        if (parent is not null && invocation.TurnBased && invocation.TurnBasedActor != parent.Id && parent.IsCritter)
            return Refused(SpellInvocationRefusal.NotTheirTurn);
        if (invocation.Spell is < 0 or >= Spells) return Refused(SpellInvocationRefusal.InvalidSpell);
        if (parent is not null && !InRange(parent.Location, invocation.TargetTile, invocation.TargetObject, record.Range))
            return Refused(SpellInvocationRefusal.OutOfRange);
        if (!HasLineOfFire(invocation, world)) return Refused(SpellInvocationRefusal.NoLineOfFire);
        if (!FlagsAllow(parent, invocation.TargetObject, record)) return Refused(SpellInvocationRefusal.ForbiddenFlags);
        if (parent is not null && (parent.SpellFlags & SpellObjectFacts.BondsOfMagick) != 0 && Same(parent, invocation.Source))
            return Refused(SpellInvocationRefusal.BondsOfMagick);
        var replaced = SpellLocation.None;
        if (parent is null || parent.Type != SpellObjectFacts.PcType)
        {
            if (!world.TargetMatchesBeginArea(invocation))
            {
                if (invocation.TargetObject is null || !world.BeginAreaTakesTiles(invocation.Spell)) return Refused(SpellInvocationRefusal.NoValidTarget);
                replaced = world.FirstTileInBeginArea(invocation, FallbackRadius);
                if (replaced.IsNone) return Refused(SpellInvocationRefusal.NoValidTarget);
                invocation = invocation with { TargetObject = null, TargetTile = replaced };
            }
        }
        if (!record.Technology)
        {
            var parentAptitude = parent?.Aptitude ?? 0;
            var targetAptitude = invocation.TargetObject?.Aptitude ?? 0;
            if (invocation.Source is not null && parentAptitude < 0 && !Same(parent, invocation.Source)
                && (invocation.SourceItemPower ?? throw new ArgumentException("A technologist's item spell needs the item power.", nameof(invocation))) <= 0)
                return new(SpellInvocationRefusal.ItemAptitude, replaced, true, false);
            if (targetAptitude < 0)
            {
                var chance = parentAptitude >= 0 ? unchecked((100 - parentAptitude) * targetAptitude / 100) : targetAptitude;
                if (rolls.Roll(SpellRollPurpose.InvocationAptitudeD100, 1, 100) <= chance)
                    return new(SpellInvocationRefusal.TargetAptitude, replaced, true, false);
            }
        }
        if (parent is not null && parent.IsCritter && Same(parent, invocation.Source) && parent.Intelligence < record.MinimumIntelligence)
            return new(SpellInvocationRefusal.Intelligence, replaced, false, false);
        if (parent is not null && invocation.Spell == Teleportation && !invocation.TeleportAllowed)
            return new(SpellInvocationRefusal.AntiTeleport, replaced, false, invocation.ParentIsLocalPlayer);
        return new(SpellInvocationRefusal.None, replaced, false, false);
    }

    /// <summary>
    /// The range (0x454700): no origin location is in range; a target object in an inventory is in range, otherwise its location
    /// replaces the tile; no location is out of range; otherwise the Chebyshev distance (<see cref="SpellLocation.Distance"/>) must be
    /// at most <paramref name="range"/> (compared in 64 bits, so a negative range admits nothing).
    /// </summary>
    [OriginalFunction(0x454700, OriginalFunctionScope.Full)]
    public static bool InRange(SpellLocation origin, SpellLocation tile, SpellObjectFacts? target, int range)
    {
        if (origin.IsNone) return true;
        if (target is not null)
        {
            if (target.InAnInventory) return true;
            tile = target.Location;
        }
        return !tile.IsNone && SpellLocation.Distance(origin, tile) <= range;
    }

    /// <summary>
    /// The line of fire (0x456BC0): a self-cast or a parentless cast passes; the target tile, or else the target object's location
    /// (an item in an inventory passes) is tested; no tile passes; a score of 100 or more refuses; a blocker that is shoot-through or
    /// the target itself passes, any other refuses. With neither a target tile nor a target object the original reads an
    /// uninitialised stack slot as the tile; called from the check that slot still holds the zero tile and target words the range
    /// call left, so the case passes, as it does here (it is reached only for a parent without a location, which the range admits).
    /// </summary>
    [OriginalFunction(0x456BC0, OriginalFunctionScope.Partial)]
    public static bool HasLineOfFire(SpellInvocation invocation, ISpellInvocationWorld world)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(world);
        var parent = invocation.Parent;
        var target = invocation.TargetObject;
        if (parent is null || Same(parent, target)) return true;
        var tile = invocation.TargetTile;
        if (tile.IsNone && target is not null)
        {
            if (target.IsItem && target.InAnInventory) return true;
            tile = target.Location;
        }
        if (tile.IsNone) return true;
        var line = world.LineOfFire(parent, tile);
        if (line.Score >= NoLineOfFire) return false;
        return line.Blocker is null || (line.Blocker.ObjectFlags & SpellObjectFacts.ShootThrough) != 0 || Same(line.Blocker, target);
    }

    /// <summary>
    /// The forbidden flags (0x456430): no parent passes; the parent's spell flags against <c>Disallowed_SF:</c>; then, with a target
    /// object, its spell flags against <c>Disallowed_TSF:</c> and, for a PC or NPC, its critter flags against <c>Disallowed_TCF:</c>.
    /// </summary>
    [OriginalFunction(0x456430, OriginalFunctionScope.Full)]
    public static bool FlagsAllow(SpellObjectFacts? parent, SpellObjectFacts? target, SpellInvocationRecord record)
    {
        if (parent is null) return true;
        if ((parent.SpellFlags & record.DisallowedCasterSpellFlags) != 0) return false;
        if (target is null) return true;
        if ((target.SpellFlags & record.DisallowedTargetSpellFlags) != 0) return false;
        return !target.IsCritter || (target.CritterFlags & record.DisallowedTargetCritterFlags) == 0;
    }

    /// <summary>
    /// Whether a holder may use an item's magic (0x456A10; the item's triggered spells ask it with the trigger event's first object,
    /// usually the item's holder, as the holder): no holder, an item without
    /// magic-tech complexity, or a holder whose aptitude is not negative, may; a technologist may only when it draws power from the
    /// item (<paramref name="itemPower"/>, 0x4614A0, above 0; asked only then). The original also reads a second object's aptitude
    /// and ignores it.
    /// </summary>
    [OriginalFunction(0x456A10, OriginalFunctionScope.Full)]
    public static bool HolderCanUseItem(int? holderAptitude, int complexity, Func<int> itemPower)
    {
        ArgumentNullException.ThrowIfNull(itemPower);
        return holderAptitude is not { } aptitude || complexity <= 0 || aptitude >= 0 || itemPower() > 0;
    }

    private static bool Same(SpellObjectFacts? a, SpellObjectFacts? b) => a is not null && b is not null && a.Id == b.Id;

    private static SpellInvocationResult Refused(SpellInvocationRefusal refusal) => new(refusal, SpellLocation.None, false, false);
}
