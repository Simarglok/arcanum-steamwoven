namespace Arcanum.Simulation.Items;

/// <summary>
/// The two item rule settings of owner decision D8 (contract docs/world/rule-modes.md): <see cref="Steamwoven"/> (the
/// original with the recorded fixes, the default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour). Each switch is
/// a behaviour of the original game confirmed in its executable (verification ledger) that the owner decided to change. The
/// profile holds no state, so it may change between any two item decisions.
/// </summary>
public sealed record ItemRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ledger TC7: the repair quote starts from the PC's sale price, which already scales with <c>current / maximum</c>, and
    /// then multiplies by <c>damage / maximum</c>, so damage counts twice: a badly damaged item is nearly free to repair and the
    /// dearest is half damaged, as the retail executable does (0x578760). Steamwoven quotes from the undamaged sale price, so
    /// the cost grows with the damage (owner decision 2026-10-20).
    /// </summary>
    public bool RepairQuoteCountsDamageTwice { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly ItemRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly ItemRuleProfile Original = new() { Name = "original", RepairQuoteCountsDamageTwice = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<ItemRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static ItemRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
