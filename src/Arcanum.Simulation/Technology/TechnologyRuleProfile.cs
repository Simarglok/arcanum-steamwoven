namespace Arcanum.Simulation.Technology;

/// <summary>
/// The two technology rule settings of owner decision D8 (contract docs/world/rule-modes.md): <see cref="Steamwoven"/> (the
/// original with the recorded fixes, the default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour). The profile holds
/// no state, so it may change between any two technology decisions.
/// </summary>
public sealed record TechnologyRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ledger TC8: one installed schematic document carries the schematic number 0; reading it registers schematic 0, whose
    /// record reads all zeros and can never be built, and destroys the document, as the retail executable does (0x4B0120).
    /// Steamwoven refuses the registration and keeps the document (a project improvement the owner recorded on 2026-10-20).
    /// </summary>
    public bool RegistersSchematicZero { get; init; }

    /// <summary>
    /// Ledger TC1: an item that pays a spell from charge cells needs <c>2 × cost − adjust(cost)</c>, and the magic adjustment of a
    /// technological item is <c>cost − trunc(cost × p / c)</c>, so the item needs <c>cost + trunc(cost × p / c)</c>: a strong
    /// technologist pays twice the cost and a mage the cost, as the retail executable does (0x450420 with 0x461590). Steamwoven
    /// needs <c>2 × cost − trunc(cost × p / c)</c> for a technological item, so an experienced technologist pays fewer cells
    /// (owner decision 2026-10-20). Magickal and neutral items are the same in both.
    /// </summary>
    public bool ItemCellsFavourMagick { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly TechnologyRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly TechnologyRuleProfile Original = new() { Name = "original", RegistersSchematicZero = true, ItemCellsFavourMagick = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<TechnologyRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static TechnologyRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
