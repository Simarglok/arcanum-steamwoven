namespace Arcanum.Simulation.Party;

/// <summary>
/// The two follower-order rule settings of owner decision D8 (contract docs/world/rule-modes.md): <see cref="Steamwoven"/> (the
/// original with the recorded fixes, the default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour). Decisions D1–D4 of the
/// companion orders study are open with the owner; the Steamwoven setting carries the coordinator's recommended fixes until he
/// answers, each a separate switch so another answer changes one value. The profile holds no state.
/// </summary>
public sealed record CompanionOrderRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ledger OL6 (decision D1): the follow order's unwait never acts, because the order reaches only followers in the array, which
    /// never wait; the order changes no state (0x4C31A0). Steamwoven also sends it to the waiting followers within 10 tiles, which
    /// the follow check brings back.
    /// </summary>
    public bool FollowOrderNeverUnwaits { get; init; }

    /// <summary>
    /// Ledger OL14 (decision D2): preset 5 (F6) has no <c>#</c> and names no follower, so it addresses nobody (0x4C3BE0).
    /// Steamwoven sends F6 to every follower in reach as "follow".
    /// </summary>
    public bool PresetSixAddressesNobody { get; init; }

    /// <summary>
    /// Ledger OL13 (decision D3): the leave farewell takes the range the line files label friendly (1900–1999) for a reaction of 40
    /// or less and the hostile one (2000–2099) above (0x4133B0). Steamwoven picks the friendly range above 40.
    /// </summary>
    public bool FarewellRangesSwapped { get; init; }

    /// <summary>
    /// Ledger OL15 (decision D4): the portrait menu addresses its follower by name text, so the first follower within 10 tiles whose
    /// known name is a prefix of "name label" takes the order (0x56A9D0, 0x4C31A0). Steamwoven addresses the clicked follower itself,
    /// still within 10 tiles.
    /// </summary>
    public bool PortraitAddressesByNamePrefix { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly CompanionOrderRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly CompanionOrderRuleProfile Original = new()
    {
        Name = "original", FollowOrderNeverUnwaits = true, PresetSixAddressesNobody = true, FarewellRangesSwapped = true,
        PortraitAddressesByNamePrefix = true,
    };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<CompanionOrderRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static CompanionOrderRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
