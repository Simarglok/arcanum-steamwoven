namespace Arcanum.Data.Written;

/// <summary>
/// The two reading-window rule settings of owner decision D8 (contract docs/world/rule-modes.md): <see cref="Steamwoven"/> (the
/// original with the recorded fixes, the default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour). Each switch is a
/// behaviour of the original game confirmed in its executable (verification ledger) that the owner decided to change. The profile
/// holds no state, so it may change between any two readings.
/// </summary>
public sealed record WrittenRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ledger WR11 (WRD3): the markup of a book, plaque or newspaper message (0x56C9F0) always drops the character after the font's
    /// digits, so a message without a font prefix loses its first character (and is centred when that is a <c>c</c>), as the retail
    /// executable does. Steamwoven reads a message whose first character is not a digit as having no markup: its whole text, font 0,
    /// not centred (owner decision 2026-10-21, ARC-TECHNOLOGY-009).
    /// </summary>
    public bool UnprefixedMessageLosesFirstCharacter { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly WrittenRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly WrittenRuleProfile Original = new() { Name = "original", UnprefixedMessageLosesFirstCharacter = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<WrittenRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static WrittenRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
