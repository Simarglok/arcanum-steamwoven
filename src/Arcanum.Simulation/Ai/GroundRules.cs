using System.Collections.Immutable;
using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Ai;

/// <summary>The kinds the item search looks for: the query's type mask (0x40 ammunition, 0x20 weapon, 0x80 armour) and, in a junk
/// pile, the item type (6, 5, 7).</summary>
public enum AiGroundKind
{
    Ammunition,
    Weapon,
    Armor,
}

/// <summary>
/// An item the item search may pick up: its distance to the NPC in tiles (0x441AE0), item flags <see cref="NoDisplay"/> and
/// <see cref="NoNpcPickup"/> (mask 0x80010), <see cref="Worthless"/> (0x461F60 answers non-zero), <see cref="CannotTake"/> (0x466510
/// refuses the NPC) and, for an item in a junk pile, object flag <see cref="Off"/> (0x2).
/// </summary>
public sealed record AiGroundItem(string Id, long Distance, bool NoDisplay = false, bool NoNpcPickup = false, bool Worthless = false,
    bool CannotTake = false, bool Off = false);

/// <summary>
/// What the item search asks of the world. <see cref="LooseItems"/> and <see cref="JunkPiles"/> are the square query (0x4AE4E0)
/// around the NPC in the query's order: loose items of the kind, and the containers named 3023 (junk piles). <see cref="WriteDangerBits"/>
/// is the session's write of the look-for bits; <see cref="StartPickup"/> is the pickup starter (0x4356C0, goal 15, slice I8b).
/// </summary>
public interface IAiItemSearchWorld : IHostilityWorld
{
    DangerFlags DangerBits(string npc);
    void WriteDangerBits(string npc, DangerFlags set, DangerFlags clear);
    /// <summary>Critter flag 0x80 (blinded).</summary>
    bool Blinded(string npc);
    /// <summary>The interface callback (slot 0x12C, 0x460C20) reports an object (inference: a window holds one).</summary>
    bool InterfaceHoldsObject { get; }
    IReadOnlyList<AiGroundItem> LooseItems(string npc, AiGroundKind kind, int halfSize);
    IReadOnlyList<string> JunkPiles(string npc, int halfSize);
    /// <summary>The pile's first item of the kind (0x4626B0), or null.</summary>
    AiGroundItem? FirstInPile(string pile, AiGroundKind kind);
    bool StartPickup(string npc, string item);
}

/// <summary>Where the item search (0x4A8E70) stopped.</summary>
public enum AiItemSearchStop
{
    Asleep,
    Blinded,
    InterfaceBusy,
    /// <summary>No look-for bit was set, or no search started a pickup.</summary>
    NothingStarted,
    /// <summary>A pickup started (<see cref="AiItemSearchResult.Item"/>; <see cref="AiItemSearchResult.Pile"/> when it lies in a junk pile).</summary>
    PickupStarted,
}

/// <summary>The item search's answer: the stop, the bits it cleared in order, and the item a pickup started on.</summary>
public sealed record AiItemSearchResult(AiItemSearchStop Stop, DangerFlags Cleared, AiGroundKind? Kind = null, string? Item = null, string? Pile = null)
{
    /// <summary>The pipeline's item-search stage chose an action.</summary>
    public bool Chose => Stop == AiItemSearchStop.PickupStarted;
}

/// <summary>
/// What the grenade step asks of the world: the thrower's stored throwing skill points (basic skill 3), its first generic item
/// with the grenade flag (0x462760), the grenade's area radius (0x466230), the line of fire's first blocker to the target's tile and
/// the critters in a square around the target (0x4AE4E0), NPCs only or PCs and NPCs, in the query's order.
/// </summary>
public interface IAiGrenadeWorld : IHostilityWorld
{
    DangerFlags DangerBits(string npc);
    void WriteDangerBits(string npc, DangerFlags set, DangerFlags clear);
    bool HasThrowingPoints(string npc);
    string? Grenade(string npc);
    int BlastRadius(string grenade);
    string? LineOfFireBlocker(string npc, string target);
    IReadOnlyList<string> SquareAround(string center, int halfSize, bool withPcs);
}

/// <summary>Why the grenade step threw or did not (0x4AC910, in the original's order).</summary>
public enum AiGrenadeStop
{
    NoCheck,
    BackingOff,
    NoThrowingSkill,
    NoGrenade,
    TooClose,
    LineBlocked,
    /// <summary>A living critter the NPC protects stands in the blast (<see cref="AiGrenadeResult.Protected"/>).</summary>
    FriendInBlast,
    /// <summary>The throw goal (37) starts at the target's tile with <see cref="AiGrenadeResult.Grenade"/>.</summary>
    Throw,
}

/// <summary>The grenade step's answer.</summary>
public sealed record AiGrenadeResult(AiGrenadeStop Stop, string? Grenade = null, string? Protected = null)
{
    public bool Throws => Stop == AiGrenadeStop.Throw;
}

/// <summary>A spell's AI redirect component (0x451700): the critter-flag mask (0: any) and <c>MinIQ:</c> (−1 when absent).</summary>
public readonly record struct AiRedirectComponent(uint CritterMask, int MinimumIntelligence);

/// <summary>What the redirect asks of the world: the PCs and NPCs in the target's vicinity (an iso-window-sized rectangle,
/// 0x440FC0) in the query's order, and an object's critter flags (field 0xDA).</summary>
public interface IAiRedirectWorld : IHostilityWorld
{
    IReadOnlyList<string> Vicinity(string target);
    uint CritterFlags(string id);
}

/// <summary>
/// The item search, the grenade and the AI redirect of the NPC AI (stage A5 and A3, research ARC-RES-059 and ARC-RES-057; slice I8,
/// ARC-WORLD-088; the item search re-read in the retail 1.0.7.4 executable).
/// </summary>
public static class AiGroundRules
{
    /// <summary>The search range is the perception distance of <c>Perception − this</c>.</summary>
    public const int SearchPerceptionOffset = 5;

    /// <summary>
    /// The item search (0x4A8E70), the pipeline step after heal: nothing while the NPC sleeps, is blinded or the interface holds an
    /// object. Then for LOOK_FOR_AMMO, LOOK_FOR_WEAPON and LOOK_FOR_ARMOR in that order, each set bit is cleared just before its search
    /// whatever the result, and the first search that starts a pickup ends the step; later bits stay set.
    /// </summary>
    [OriginalFunction(0x4A8E70, OriginalFunctionScope.Full)]
    public static AiItemSearchResult SearchItems(IAiItemSearchWorld world, string npc)
    {
        ArgumentNullException.ThrowIfNull(world);
        var n = Get(world, npc);
        if (n.Asleep) return new(AiItemSearchStop.Asleep, DangerFlags.None);
        if (world.Blinded(npc)) return new(AiItemSearchStop.Blinded, DangerFlags.None);
        if (world.InterfaceHoldsObject) return new(AiItemSearchStop.InterfaceBusy, DangerFlags.None);
        var cleared = DangerFlags.None;
        foreach (var (bit, kind) in new[] { (DangerFlags.LookForAmmo, AiGroundKind.Ammunition), (DangerFlags.LookForWeapon, AiGroundKind.Weapon),
                     (DangerFlags.LookForArmor, AiGroundKind.Armor) })
        {
            if (!world.DangerBits(npc).HasFlag(bit)) continue;
            world.WriteDangerBits(npc, DangerFlags.None, bit);
            cleared |= bit;
            if (Search(world, n, kind) is { } found) return new(AiItemSearchStop.PickupStarted, cleared, kind, found.Item, found.Pile);
        }
        return new(AiItemSearchStop.NothingStarted, cleared);
    }

    /// <summary>
    /// One search (0x4A8F90): nothing for a monstrous NPC. In the square of half-size <c>clamp(Perception − 5, 1, 10)</c>, the
    /// nearest loose item of the kind that is displayed, NPC-pickable, worth something and takable (the first on a tie, P1) gets the
    /// pickup; when there is none or the pickup does not start, the first junk pile whose first item of the kind passes the same
    /// tests (and is not off) and starts a pickup ends the search.
    /// </summary>
    [OriginalFunction(0x4A8F90, OriginalFunctionScope.Full)]
    private static (string Item, string? Pile)? Search(IAiItemSearchWorld world, HostilityCritter n, AiGroundKind kind)
    {
        if (n.Monstrous) return null;
        var range = AwarenessRules.PerceptionDistance(unchecked(n.Perception - SearchPerceptionOffset));
        AiGroundItem? nearest = null;
        foreach (var item in world.LooseItems(n.Id, kind, range))
            if (Takable(item) && (nearest is null || item.Distance < nearest.Distance)) nearest = item;
        if (nearest is not null && world.StartPickup(n.Id, nearest.Id)) return (nearest.Id, null);
        foreach (var pile in world.JunkPiles(n.Id, range))
            if (world.FirstInPile(pile, kind) is { Off: false } item && Takable(item) && world.StartPickup(n.Id, item.Id)) return (item.Id, pile);
        return null;
    }

    private static bool Takable(AiGroundItem item) => !item.NoDisplay && !item.NoNpcPickup && !item.Worthless && !item.CannotTake;

    /// <summary>
    /// The grenade step (0x4AC910) of combat perform at distance <c>d</c> to the target: no CHECK_GRENADE → none; BACKING_OFF →
    /// none, the bit kept; the bit cleared; then no throwing skill points, no grenade, <c>d ≤</c> the blast radius R, any blocker on
    /// the line of fire, or a living critter the NPC protects in the square of half-size R around the target → none; otherwise the
    /// throw at the target's tile. The Original setting scans NPCs only, so a PC in the blast never stops a throw (SP15,
    /// <see cref="CombatAnomalyProfile.GrenadeIgnoresPcsInBlast"/>); Steamwoven scans PCs and NPCs.
    /// </summary>
    [OriginalFunction(0x4AC910, OriginalFunctionScope.Full)]
    public static AiGrenadeResult Grenade(IAiGrenadeWorld world, string npc, string target, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(profile);
        var bits = world.DangerBits(npc);
        if (!bits.HasFlag(DangerFlags.CheckGrenade)) return new(AiGrenadeStop.NoCheck);
        if (bits.HasFlag(DangerFlags.BackingOff)) return new(AiGrenadeStop.BackingOff);
        world.WriteDangerBits(npc, DangerFlags.None, DangerFlags.CheckGrenade);
        if (!world.HasThrowingPoints(npc)) return new(AiGrenadeStop.NoThrowingSkill);
        if (world.Grenade(npc) is not { } grenade) return new(AiGrenadeStop.NoGrenade);
        var radius = world.BlastRadius(grenade);
        if (world.Distance(npc, target) <= radius) return new(AiGrenadeStop.TooClose, grenade);
        if (world.LineOfFireBlocker(npc, target) is not null) return new(AiGrenadeStop.LineBlocked, grenade);
        foreach (var id in world.SquareAround(target, radius, !profile.GrenadeIgnoresPcsInBlast))
        {
            if (world.Find(id) is not { } c || c.Dead) continue;
            if (profile.GrenadeIgnoresPcsInBlast && c.Kind != HostilityKind.Npc) continue;
            if (c.Kind is HostilityKind.Pc or HostilityKind.Npc && HostilityRules.Protect(world, npc, id) != Protection.No)
                return new(AiGrenadeStop.FriendInBlast, grenade, id);
        }
        return new(AiGrenadeStop.Throw, grenade);
    }

    /// <summary>
    /// The AI redirect (0x4A9560 with its record 0x4A9530) of a spell's redirect component on <paramref name="target"/>: over the
    /// target's vicinity, every living candidate other than the target, skipped when the threshold is above 0 and the intelligence
    /// tested exceeds it, or when the mask is not 0 and the candidate has none of its critter flags; each NPC left is redirected
    /// (0x4A94C0: the host runs the attack event with the target as attacker, loudness 0, no flags, then writes the target into the
    /// candidate's focus and who-hit-me-last, the session's <c>RedirectDanger</c>). The Original setting tests the target's own
    /// intelligence for every candidate, so it redirects all of them or none (P3,
    /// <see cref="CombatAnomalyProfile.RedirectTestsTargetIntelligence"/>); Steamwoven tests each candidate's.
    /// </summary>
    [OriginalFunction(0x4A9560, OriginalFunctionScope.Full)]
    public static ImmutableArray<string> Redirect(IAiRedirectWorld world, string target, AiRedirectComponent component, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        var t = Get(world, target);
        var redirected = ImmutableArray.CreateBuilder<string>();
        foreach (var id in world.Vicinity(target))
        {
            if (id == target || world.Find(id) is not { } c || c.Dead) continue;
            var tested = profile.RedirectTestsTargetIntelligence ? t.Intelligence : c.Intelligence;
            if (component.MinimumIntelligence > 0 && tested > component.MinimumIntelligence) continue;
            if (component.CritterMask != 0 && (world.CritterFlags(id) & component.CritterMask) == 0) continue;
            if (c.Kind == HostilityKind.Npc) redirected.Add(id);
        }
        return redirected.ToImmutable();
    }

    private static HostilityCritter Get(IHostilityWorld world, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return world.Find(id) ?? throw new ArgumentException($"The world does not know '{id}'.", nameof(id));
    }
}
