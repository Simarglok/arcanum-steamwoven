namespace Arcanum.Simulation.Combat;

/// <summary>The object types an attack damages besides critters (the original's types 1, 2, 3, 5..14 and 17; a wall never takes
/// damage and a projectile is never a target here).</summary>
public enum DamageableObjectKind { Portal, Container, Scenery, Item, Trap }

/// <summary>
/// What breaking a non-critter object does to it (the type branches of 0x43F1C0, ledger "Object damage").
/// </summary>
public enum ObjectBreak
{
    /// <summary>The object still has hit points.</summary>
    None,
    /// <summary>A junk pile (container prototype 3023): its damage is reset to 0 and nothing breaks.</summary>
    Spared,
    /// <summary>The object is destroyed (removed): a door, a scenery already busted, a trap without a destroyed ART or already
    /// busted. The destroy sound plays.</summary>
    Destroyed,
    /// <summary>A window gets its busted ART (bit 0x200, frame 0) and the busted bit (0x80), and its BUST script runs.</summary>
    WindowBusted,
    /// <summary>A container drops its inventory at its location and is destroyed; an eye candy plays on what lies there.</summary>
    ContainerSpilled,
    /// <summary>A respawning scenery (scenery flag 0x20): its BUST script runs, it is switched off with its damage at 0 and comes
    /// back when its respawn event fires.</summary>
    SceneryRespawns,
    /// <summary>A scenery takes its destroyed ART when it has one, its damage becomes its maximum, it is marked busted and becomes
    /// invulnerable; the bust sound plays and its BUST script runs.</summary>
    SceneryBusted,
    /// <summary>A trap takes its destroyed ART with its damage at 0 and is marked busted; the bust sound plays and its BUST script
    /// runs.</summary>
    TrapBusted,
    /// <summary>An item breaks (<see cref="Items.ItemDurabilityRules.Break"/>).</summary>
    ItemBreak,
}

/// <summary>
/// The facts of a struck object that its damage and break read: its kind, its maximum hit points (hp_pts + hp_adj), whether a portal
/// is a window (a door has no busted ART), a container a junk pile, a scenery respawning, and whether a scenery or trap has a
/// destroyed ART (field 25 not −1) and is already busted.
/// </summary>
public sealed record DamageableObject
{
    public required DamageableObjectKind Kind { get; init; }
    public int MaxHitPoints { get; init; }
    public bool Window { get; init; }
    public bool JunkPile { get; init; }
    public bool Respawning { get; init; }
    public bool HasDestroyedArt { get; init; }
    public bool Busted { get; init; }
}

/// <summary>
/// One hit on an object: its hit-point damage before and after the hit (<see cref="Damage"/> clamped as the setter stores it), whether
/// its current hit points fell to 0 or less, and the break that followed with the damage it leaves (<see cref="DamageAfterBreak"/>).
/// </summary>
public sealed record ObjectHitOutcome(int DamageBefore, int Damage, bool Broken, ObjectBreak Break, int DamageAfterBreak);

/// <summary>
/// ARC-COMBAT-008: an attack's hit-point damage on a non-critter object and its break, as the retail 1.0.7.4 executable applies them
/// (the object branch of 0x4B4390 and the type branches of 0x43F1C0; contract docs/world/object-damage-runtime.md, ledger "Object
/// damage"). The amount is the resolver's <see cref="AttackDamage.HitPointDamage"/>, which already holds the resistance, the game
/// difficulty and a portal's or container's material reduction (the critical procedure does nothing for an object). Pure; the
/// session stores the damage and the break, the host its scripts, sounds and eye candy. An item's break is
/// <see cref="Items.ItemDurabilityRules"/>' (the session's <c>DamageItem</c>).
/// </summary>
public static class ObjectDamageRules
{
    /// <summary>The container prototype that never breaks (a junk pile, description 3023).</summary>
    public const int JunkPilePrototype = 3023;

    /// <summary>The eye candy a container's break plays on the first container or item at its location.</summary>
    public const int SpillEyeCandy = 1203;

    /// <summary>
    /// One hit: the damage grows by <paramref name="amount"/> (a negative amount adds nothing: the original clamps it to 0 before
    /// writing), and at current hit points 0 or less the object breaks (<see cref="Break"/>).
    /// </summary>
    public static ObjectHitOutcome Hit(DamageableObject target, int damage, int amount)
    {
        ArgumentNullException.ThrowIfNull(target);
        Check(target, damage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, Items.ItemDurabilityLimits.MaxDamageStep);
        var after = (int)Math.Min((long)damage + Math.Max(0, amount), Items.ItemDurabilityLimits.MaxDamage);
        if (target.MaxHitPoints - after > 0) return new(damage, after, false, ObjectBreak.None, after);
        var (kind, left) = Break(target, after);
        return new(damage, after, true, kind, left);
    }

    /// <summary>
    /// The break of a non-critter object (0x43F1C0) with the damage it leaves: a portal (0x4F06E0) destroys a door and busts a window
    /// (damage kept); a junk pile resets its damage to 0; another container spills and is destroyed; a respawning scenery is switched
    /// off with damage 0; a scenery busts the first time (damage set to its maximum) and is destroyed when already busted; a trap
    /// without a destroyed ART or already busted is destroyed, else busts with damage 0; an item breaks through the durability rules.
    /// </summary>
    public static (ObjectBreak Break, int Damage) Break(DamageableObject target, int damage)
    {
        ArgumentNullException.ThrowIfNull(target);
        Check(target, damage);
        return target.Kind switch
        {
            DamageableObjectKind.Portal => (PortalBust(target.Window), damage),
            DamageableObjectKind.Container => target.JunkPile ? (ObjectBreak.Spared, 0) : (ObjectBreak.ContainerSpilled, damage),
            DamageableObjectKind.Scenery => target.Respawning ? (ObjectBreak.SceneryRespawns, 0)
                : target.Busted ? (ObjectBreak.Destroyed, damage)
                : (ObjectBreak.SceneryBusted, Math.Max(0, target.MaxHitPoints)),
            DamageableObjectKind.Trap => !target.HasDestroyedArt || target.Busted ? (ObjectBreak.Destroyed, damage) : (ObjectBreak.TrapBusted, 0),
            DamageableObjectKind.Item => (ObjectBreak.ItemBreak, damage),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    /// <summary>
    /// The delay of a respawning scenery's respawn event from its stored respawn field (field 71): a positive value counts seconds at
    /// the game rate (× 8,000 game ms), a negative one game minutes (× 60,000 ms), and 0 is one day and 1 ms. Exact; the original
    /// multiplies in 32 bits, which wraps only beyond ±35,791 minutes or 268,435 seconds.
    /// </summary>
    public static long RespawnDelayMs(int respawn) => respawn switch
    {
        0 => 86_400_000L + 1,
        > 0 => respawn * 8_000L,
        _ => -(long)respawn * 60_000L,
    };

    /// <summary>The portal bust (0x4F06E0, ledger LK12): a door has no busted ART and is destroyed; a window takes its busted ART on
    /// frame 0 and the busted bit, and its BUST script runs. The ART, the script and the redraw are the host's.</summary>
    [OriginalFunction(0x4F06E0, OriginalFunctionScope.Partial)]
    public static ObjectBreak PortalBust(bool window) => window ? ObjectBreak.WindowBusted : ObjectBreak.Destroyed;

    /// <summary>Whether the original gives the object an end state that stays (destroyed, or a container spilled).</summary>
    public static bool Removes(ObjectBreak kind) => kind is ObjectBreak.Destroyed or ObjectBreak.ContainerSpilled;

    private static void Check(DamageableObject target, int damage)
    {
        if (!Enum.IsDefined(target.Kind)) throw new ArgumentOutOfRangeException(nameof(target), "Kind");
        if (Math.Abs((long)target.MaxHitPoints) > Items.ItemDurabilityLimits.MaxDamage) throw new ArgumentOutOfRangeException(nameof(target), "MaxHitPoints");
        if (damage is < 0 or > Items.ItemDurabilityLimits.MaxDamage) throw new ArgumentOutOfRangeException(nameof(damage));
        if (target.Window && target.Kind != DamageableObjectKind.Portal || target.JunkPile && target.Kind != DamageableObjectKind.Container ||
            target.Respawning && target.Kind != DamageableObjectKind.Scenery ||
            (target.HasDestroyedArt || target.Busted) && target.Kind is not (DamageableObjectKind.Scenery or DamageableObjectKind.Trap))
            throw new ArgumentException("A fact that does not belong to the object's kind.", nameof(target));
    }
}
