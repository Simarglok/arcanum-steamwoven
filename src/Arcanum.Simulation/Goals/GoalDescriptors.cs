using System.Collections.Immutable;

namespace Arcanum.Simulation.Goals;

/// <summary>
/// One goal type's descriptor, read from the retail 1.0.7.4 executable's table 0x5B03D0 (ARC-DEV-131, ledger GS8, GS9): the priority 1..5,
/// the no-interrupt flag (+0x8), the carry flag (+0xC), the "not counted" flag (+0x10 bit 0) and up to three alias goal types (+0x14..+0x1C).
/// The node count (+0x0, read by no code) and the node table (+0x20..) are not modelled; the stepper's nodes are the executor's.
/// </summary>
/// <param name="Type">The goal type the descriptor belongs to.</param>
/// <param name="Priority">1..5: 1 idles and loops, 2 movement, 3 ordinary actions, 4 spell attempts and unconceal, 5 dying, projectiles, knockback, eye candy.</param>
/// <param name="NoInterrupt">The goal starts beside the main run and survives every interrupt below level 6 (an overlay).</param>
/// <param name="Carried">The goal's run travels with its object across a teleport of the hero, and its run is "not busy" for the please-move starters (GS10).</param>
/// <param name="NotCounted">The goal is left out of the round's significant-goal count and of the round-start sweep (TL39).</param>
/// <param name="Aliases">The goal types that count as "the same activity" in <see cref="GoalDescriptors.IsSameActivity"/> (not symmetric).</param>
public sealed record GoalDescriptor(GoalType Type, int Priority, bool NoInterrupt, bool Carried, bool NotCounted, ImmutableArray<GoalType> Aliases);

/// <summary>
/// The static descriptor table of the 87 goal types (ARC-DEV-131, ledger GS8, GS9): 85 have a descriptor, 13 [ag_unlock_door] and 22
/// [ag_pick_weapon] have none. Priority 1: 1, 2, 57, 77; priority 2: the moves 3..8, 24, 52, 53, 55, 56, 61, 68, 69, 79, 80; priority 4: 27, 54,
/// 74, 83; priority 5: 31, 32, 36, 43..48, 58, 59, 75, 76, 84; priority 3 the rest. No-interrupt (15): 12, 44..48, 50, 58, 59, 69, 73..76, 84.
/// Not counted (5): 1, 44, 75, 76, 77. Carried (26): 0, 1, 6, 11, 12, 29..31, 44..49, 51, 57..59, 62, 70..72, 75..77, 84.
/// </summary>
public static class GoalDescriptors
{
    /// <summary>The number of goal types that have a descriptor.</summary>
    public const int DescribedCount = 85;

    private static readonly ImmutableArray<GoalDescriptor?> Table = Build();

    /// <summary>Every descriptor, by goal number.</summary>
    public static ImmutableArray<GoalDescriptor> All { get; } = [.. Table.Where(d => d is not null).Select(d => d!)];

    /// <summary>The descriptor of <paramref name="type"/>, or null for a goal without one (13, 22) or a number outside 0..86.</summary>
    public static GoalDescriptor? Of(GoalType type) => GoalTypes.IsDefined((int)type) ? Table[(int)type] : null;

    /// <summary>Whether <paramref name="type"/> has a descriptor.</summary>
    public static bool Has(GoalType type) => Of(type) is not null;

    /// <summary>
    /// Whether a run whose bottom goal is <paramref name="bottom"/> does the same activity as a query for <paramref name="queried"/>
    /// (0x44E830 and 0x44E710, which read the <b>queried</b> goal's descriptor): the type itself, or one of the queried type's aliases.
    /// The alias lists are not symmetric (move to tile 3 lists 7 and 4, move near tile 7 lists 4, 3 and 55, run near tile 55 lists 7 and 4).
    /// A queried goal without a descriptor matches only itself.
    /// </summary>
    public static bool IsSameActivity(GoalType queried, GoalType bottom) =>
        queried == bottom || (Of(queried) is { } descriptor && !descriptor.Aliases.IsDefaultOrEmpty && descriptor.Aliases.Contains(bottom));

    /// <summary>
    /// Whether a goal of this descriptor is a "significant" goal of the round (TL39): priority 2 or more and not a no-interrupt goal. The
    /// original counts a goal when it is pushed and uncounts it when its run is freed (0x44C840, 0x44C8F0), each time unless the run's current
    /// goal is "not counted" (<see cref="GoalRun.CurrentGoalNotCounted"/>); Steamwoven derives the count from the table instead
    /// (<see cref="GoalRunTable.SignificantGoalCount"/>), which agrees with the counter while no run holds counted goals under a not-counted one (open question Q3).
    /// </summary>
    [OriginalFunction(0x44C840, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x44C8F0, OriginalFunctionScope.Partial)]
    public static bool IsSignificant(GoalDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.Priority >= 2 && !descriptor.NoInterrupt;
    }

    private static GoalDescriptor D(GoalType type, int priority, bool noInterrupt = false, bool carried = false, bool notCounted = false, GoalType[]? aliases = null) =>
        new(type, priority, noInterrupt, carried, notCounted, aliases is null ? [] : [.. aliases]);

    private static ImmutableArray<GoalDescriptor?> Build()
    {
        var table = new GoalDescriptor?[GoalTypes.Count];
        foreach (var d in Rows())
        {
            if (table[(int)d.Type] is not null) throw new InvalidOperationException($"descriptor of goal {(int)d.Type} listed twice");
            table[(int)d.Type] = d;
        }
        return [.. table];
    }

    private static GoalDescriptor[] Rows() =>
    [
        D(GoalType.Animate, 3, carried: true),
        D(GoalType.AnimateLoop, 1, carried: true, notCounted: true),
        D(GoalType.Fidget, 1),
        D(GoalType.MoveToTile, 2, aliases: [GoalType.MoveNearTile, GoalType.RunToTile]),
        D(GoalType.RunToTile, 2, aliases: [GoalType.MoveNearTile, GoalType.MoveToTile, GoalType.RunNearTile]),
        D(GoalType.AttemptMove, 2),
        D(GoalType.MoveToPause, 2, carried: true),
        D(GoalType.MoveNearTile, 2, aliases: [GoalType.RunToTile, GoalType.MoveToTile, GoalType.RunNearTile]),
        D(GoalType.MoveNear, 2),
        D(GoalType.MoveStraight, 3),
        D(GoalType.AttemptMoveStraight, 3),
        D(GoalType.OpenDoor, 3, carried: true),
        D(GoalType.AttemptOpenDoor, 3, noInterrupt: true, carried: true),
        // 13 [ag_unlock_door]: no descriptor.
        D(GoalType.JumpWindow, 3),
        D(GoalType.PickupItem, 3),
        D(GoalType.AttemptPickup, 3),
        D(GoalType.Pickpocket, 3),
        D(GoalType.Attack, 3, aliases: [GoalType.AttemptAttack]),
        D(GoalType.AttemptAttack, 3, aliases: [GoalType.Attack, GoalType.Kill]),
        D(GoalType.Kill, 3, aliases: [GoalType.AttemptAttack]),
        D(GoalType.Talk, 3),
        // 22 [ag_pick_weapon]: no descriptor.
        D(GoalType.Chase, 3),
        D(GoalType.Follow, 2, aliases: [GoalType.RunNear, GoalType.MoveNear]),
        D(GoalType.Flee, 3),
        D(GoalType.CastSpell, 3, aliases: [GoalType.AttemptSpell]),
        D(GoalType.AttemptSpell, 4, aliases: [GoalType.CastSpell]),
        D(GoalType.ShootSpell, 3),
        D(GoalType.HitBySpell, 3, carried: true),
        D(GoalType.HitByWeapon, 3, carried: true),
        D(GoalType.Dying, 5, carried: true),
        D(GoalType.DestroyObject, 5),
        D(GoalType.UseSkill, 3),
        D(GoalType.AttemptSkill, 3),
        D(GoalType.SkillConceal, 3),
        D(GoalType.Projectile, 5),
        D(GoalType.ThrowItem, 3),
        D(GoalType.UseObject, 3),
        D(GoalType.UseItem, 3),
        D(GoalType.UseItemWithSkill, 3),
        D(GoalType.UseItemOnTile, 3),
        D(GoalType.UseItemOnTileWithSkill, 3),
        D(GoalType.Knockback, 5),
        D(GoalType.Floating, 5, noInterrupt: true, carried: true, notCounted: true),
        D(GoalType.EyeCandy, 5, noInterrupt: true, carried: true),
        D(GoalType.EyeCandyReverse, 5, noInterrupt: true, carried: true),
        D(GoalType.EyeCandyCallback, 5, noInterrupt: true, carried: true),
        D(GoalType.EyeCandyReverseCallback, 5, noInterrupt: true, carried: true),
        D(GoalType.CloseDoor, 3, carried: true),
        D(GoalType.AttemptCloseDoor, 3, noInterrupt: true),
        D(GoalType.AnimateReverse, 3, carried: true),
        D(GoalType.MoveAway, 2),
        D(GoalType.Rotate, 2),
        D(GoalType.Unconceal, 4),
        D(GoalType.RunNearTile, 2, aliases: [GoalType.MoveNearTile, GoalType.RunToTile]),
        D(GoalType.RunNear, 2),
        D(GoalType.AnimateStunned, 1, carried: true),
        D(GoalType.EyeCandyEndCallback, 5, noInterrupt: true, carried: true),
        D(GoalType.EyeCandyReverseEndCallback, 5, noInterrupt: true, carried: true),
        D(GoalType.AnimateKneelMagicHands, 3),
        D(GoalType.MeleeApproach, 2),
        D(GoalType.KnockDown, 3, carried: true),
        D(GoalType.GetUp, 3),
        D(GoalType.AttemptMoveStraightKnockback, 3),
        D(GoalType.Wander, 3, aliases: [GoalType.MoveNearTile]),
        D(GoalType.WanderSeekDarkness, 3, aliases: [GoalType.MoveNearTile]),
        D(GoalType.AttemptPickLocks, 3),
        D(GoalType.PleaseMove, 2, aliases: [GoalType.MoveNearTile, GoalType.RunToTile, GoalType.MoveToTile]),
        D(GoalType.SpreadOut, 2, noInterrupt: true, aliases: [GoalType.RunNear, GoalType.MoveNear]),
        D(GoalType.AnimateDoorOpen, 3, carried: true),
        D(GoalType.AnimateDoorClosed, 3, carried: true),
        D(GoalType.PendClosingDoor, 3, carried: true),
        D(GoalType.CastSpellFriendly, 3, noInterrupt: true, aliases: [GoalType.AttemptSpell]),
        D(GoalType.AttemptSpellFriendly, 4, noInterrupt: true, aliases: [GoalType.CastSpell]),
        D(GoalType.EyeCandyFireDamage, 5, noInterrupt: true, carried: true, notCounted: true),
        D(GoalType.EyeCandyReverseFireDamage, 5, noInterrupt: true, carried: true, notCounted: true),
        D(GoalType.AnimateLoopFireDamage, 1, carried: true, notCounted: true),
        D(GoalType.AttemptMoveStraightSpell, 3),
        D(GoalType.CombatApproach, 2),
        D(GoalType.RangedApproach, 2),
        D(GoalType.UseContainer, 3),
        D(GoalType.CastSpellWithAnimation, 3, aliases: [GoalType.AttemptSpell]),
        D(GoalType.AttemptSpellWithAnimation, 4, aliases: [GoalType.CastSpell]),
        D(GoalType.CastSpellWithAnimationSecondary, 5, noInterrupt: true, carried: true),
        D(GoalType.BackOffFrom, 3),
        D(GoalType.AttemptPickpocket, 3),
    ];
}
