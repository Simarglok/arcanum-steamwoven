using Arcanum.Simulation.Reaction;

namespace Arcanum.Simulation.Party;

/// <summary>
/// The fourteen order types of the original's broadcast rules (<c>Rules\BroadCastRules.mes</c>, ledger OL3), in their numbering.
/// <see cref="FollowOther"/> is never reached (its keyword repeats "follow"); <see cref="Join"/> and <see cref="Disband"/> act only
/// in the multiplayer phase.
/// </summary>
public enum CompanionOrderType { None, Leave, Wait, Follow, Move, StayClose, SpreadOut, Curse, Join, Disband, Attack, WalkHere, BackOff, FollowOther }

/// <summary>The line ranges of an order (OL12): acknowledgement, "cannot", and the two leave farewells as the line files label them.</summary>
public enum CompanionOrderLine { Acknowledge, Cannot, FriendlyFarewell, HostileFarewell }

/// <summary>The portrait menu's items (<c>mes\follower_ui.mes</c> 0–7, ledger OL1).</summary>
public enum FollowerMenuItem { Walk, Attack, StayClose, SpreadOut, BackOff, Wait, Inventory, CharacterSheet }

/// <summary>A preset's order (F1–F6): its type and whether it addresses every follower in reach (its text starts with <c>#</c>).</summary>
public readonly record struct CompanionOrderPreset(CompanionOrderType Type, bool All);

/// <summary>A follower an order may reach: its entity, its name as the PC knows it, and its tile distance to the PC (the original's
/// Chebyshev tile distance, 0x4B96F0).</summary>
public readonly record struct CompanionOrderCandidate(string Entity, string KnownName, long Distance);

/// <summary>
/// The original's out-of-combat follower orders (ARC-RES-064 rows OL1–OL16, ARC-WORLD-046) as pure functions: the presets, the portrait
/// menu, who an order reaches and the line ranges. The session applies the effects (<c>GameSessionOwner.IssueCompanionOrder</c>); the
/// host shows the lines by range and runs the goals.
/// </summary>
public static class CompanionOrderRules
{
    /// <summary>An order reaches followers at most this many tiles from the PC (OL2: farther than 10 is skipped).</summary>
    public const int Reach = 10;

    /// <summary>The number of presets (F1–F6).</summary>
    public const int Presets = 6;

    /// <summary>Whether the order belongs to the NPC AI's combat stage (A7): attack, back off (stop attacking) and curse.</summary>
    public static bool IsCombat(CompanionOrderType type) => type is CompanionOrderType.Attack or CompanionOrderType.BackOff or CompanionOrderType.Curse;

    /// <summary>
    /// The order of preset <paramref name="index"/> (F1 = 0 … F6 = 5, OL14): walk here, attack, stay close, spread out and back off
    /// for every follower in reach; preset 5 names nobody in the Original setting and is "follow" for every follower in reach in
    /// Steamwoven (D2). The installed presets' meaning, not their text, which the original copies from <c>mes\broadcast.mes</c>.
    /// </summary>
    [OriginalFunction(0x4C3BE0, OriginalFunctionScope.Partial)]
    public static CompanionOrderPreset Preset(int index, CompanionOrderRuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return index switch
        {
            0 => new(CompanionOrderType.WalkHere, true),
            1 => new(CompanionOrderType.Attack, true),
            2 => new(CompanionOrderType.StayClose, true),
            3 => new(CompanionOrderType.SpreadOut, true),
            4 => new(CompanionOrderType.BackOff, true),
            5 => profile.PresetSixAddressesNobody ? new(CompanionOrderType.None, false) : new(CompanionOrderType.Follow, true),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    /// <summary>
    /// The order a portrait menu item sends (OL1): walk (with a picked tile), attack, stay close, spread out, back off and wait; wait is
    /// not sent for a mind-controlled follower, and the inventory and character sheet items open windows instead (null).
    /// </summary>
    [OriginalFunction(0x56A9D0, OriginalFunctionScope.Partial)]
    public static CompanionOrderType? PortraitOrder(FollowerMenuItem item, bool mindControlled) => item switch
    {
        FollowerMenuItem.Walk => CompanionOrderType.WalkHere,
        FollowerMenuItem.Attack => CompanionOrderType.Attack,
        FollowerMenuItem.StayClose => CompanionOrderType.StayClose,
        FollowerMenuItem.SpreadOut => CompanionOrderType.SpreadOut,
        FollowerMenuItem.BackOff => CompanionOrderType.BackOff,
        FollowerMenuItem.Wait => mindControlled ? null : CompanionOrderType.Wait,
        FollowerMenuItem.Inventory or FollowerMenuItem.CharacterSheet => null,
        _ => throw new ArgumentOutOfRangeException(nameof(item)),
    };

    /// <summary>The followers an order to all reaches (OL2): those within <see cref="Reach"/> tiles, in the given (array) order.</summary>
    public static IReadOnlyList<string> AllInReach(IEnumerable<CompanionOrderCandidate> followers)
    {
        ArgumentNullException.ThrowIfNull(followers);
        return followers.Where(InReach).Select(f => f.Entity).ToArray();
    }

    /// <summary>
    /// Who a portrait order for <paramref name="clicked"/> reaches, and with which type (OL15). In the Original setting the menu sends
    /// the text "name label" (its format is two strings with a space) and the walk gives it to the first follower in reach, in array
    /// order, whose known name is a case-insensitive prefix of the text; that follower reads the rest of the text after its own name,
    /// leading spaces skipped, as the order word, so a follower with a shorter name sharing the prefix takes the order as no order
    /// (<see cref="CompanionOrderType.None"/>). In Steamwoven (D4) the clicked follower itself takes it when it is in reach. Null when
    /// nobody does.
    /// </summary>
    public static (string Entity, CompanionOrderType Type)? PortraitReceiver(IReadOnlyList<CompanionOrderCandidate> followers, string clicked,
        CompanionOrderType type, string label, CompanionOrderRuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(followers); ArgumentNullException.ThrowIfNull(clicked); ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        var target = followers.FirstOrDefault(f => f.Entity == clicked);
        if (target.Entity is null) return null;
        if (!profile.PortraitAddressesByNamePrefix) return InReach(target) ? (target.Entity, type) : null;
        var text = target.KnownName + " " + label;
        foreach (var follower in followers.Where(InReach))
        {
            if (!text.StartsWith(follower.KnownName, StringComparison.OrdinalIgnoreCase)) continue;
            var word = text[follower.KnownName.Length..].TrimStart(' ');
            return (follower.Entity, string.Equals(word, label, StringComparison.OrdinalIgnoreCase) ? type : CompanionOrderType.None);
        }
        return null;
    }

    /// <summary>
    /// The leave order's farewell (OL13): the original takes the range labelled friendly for a reaction level of suspicious or worse
    /// (40 or less) and the hostile one above; Steamwoven (D3) the other way round. A follower that cannot speak says nothing (the
    /// host's test).
    /// </summary>
    [OriginalFunction(0x4133B0, OriginalFunctionScope.Partial)]
    public static CompanionOrderLine Farewell(int reaction, CompanionOrderRuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var low = ReactionRules.Category(reaction) >= ReactionCategory.Suspicious;
        return low == profile.FarewellRangesSwapped ? CompanionOrderLine.FriendlyFarewell : CompanionOrderLine.HostileFarewell;
    }

    /// <summary>The message range of a line (OL12): acknowledgement 2100, "cannot" 2200, friendly farewell 1900, hostile farewell
    /// 2000, 100 lines each, in the generated NPC line files chosen by both genders.</summary>
    public static LoyaltyLines Lines(CompanionOrderLine line) => line switch
    {
        CompanionOrderLine.Acknowledge => new(2100, 2199),
        CompanionOrderLine.Cannot => new(2200, 2299),
        CompanionOrderLine.FriendlyFarewell => new(1900, 1999),
        CompanionOrderLine.HostileFarewell => new(2000, 2099),
        _ => throw new ArgumentOutOfRangeException(nameof(line)),
    };

    private static bool InReach(CompanionOrderCandidate follower) => follower.Distance is >= 0 and <= Reach;
}
