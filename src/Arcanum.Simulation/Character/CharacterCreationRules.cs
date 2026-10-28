using System.Collections.Immutable;

namespace Arcanum.Simulation.Character;

/// <summary>One row of the new-character screen's race table (ARC-UI-030, ledger NW2): the body type the race change writes and
/// whether a woman of the race may be made.</summary>
public readonly record struct CreationRace(int BodyType, bool FemaleAllowed);

/// <summary>What a race or gender button of the new-character screen did.</summary>
/// <param name="Race">The race after the press.</param>
/// <param name="Gender">The gender after the press.</param>
/// <param name="Changed">The press set a race or gender (the screen then draws again).</param>
/// <param name="ClearsBackground">The press clears the background (the screen's clear, ledger BD10), whether or not it changed.</param>
public readonly record struct CreationPress(CharacterRace Race, CharacterGender Gender, bool Changed, bool ClearsBackground);

/// <summary>
/// ARC-UI-030 (N10b, FR-CHAR-003): the race and gender buttons of the original's new-character screen (window 12), read in the
/// executable (ledger NW1–NW5). The screen offers the eight player races 0..7 (human to half-ogre); a woman only of a race whose row
/// allows it (human, elf, half-elf, half-orc). The race buttons step through the races the current gender allows and wrap; each
/// gender button toggles the gender when the race allows the new one. A press that sets a race or gender clears the background
/// first (BD10); the original's gender buttons clear it even when the race refuses a woman and nothing else changes, a gameplay bug
/// the Steamwoven setting does not repeat (<see cref="CharacterCreationRuleProfile.RefusedGenderClearsBackground"/>, NW4).
/// The setters' effects (race effect 64 + race and the female effect 330 replace the old ones, NW3) are the session's hero
/// definition; the body type and the art are not drawn yet (N15). Godot-free, stateless.
/// </summary>
public static class CharacterCreationRules
{
    public const int RaceCount = 8;

    /// <summary>The race table at 0x5C5170 (NW2), indexed by race.</summary>
    public static ImmutableArray<CreationRace> Races { get; } =
    [
        new(0, true), new(1, false), new(4, true), new(0, true), new(2, false), new(2, false), new(0, true), new(3, false),
    ];

    /// <summary>Whether the screen can make a character of this race and gender.</summary>
    public static bool Allows(CharacterRace race, CharacterGender gender) =>
        (int)race is >= 0 and < RaceCount && (gender == CharacterGender.Male || Races[(int)race].FemaleAllowed);

    /// <summary>The previous-race button (0x545490): a man steps to the race before (race 0 wraps to 7); a woman to the nearest race
    /// before it that allows women, wrapping to the last such race. The background is cleared when the race changes.</summary>
    [OriginalFunction(0x545490, OriginalFunctionScope.Partial)]
    public static CreationPress PreviousRace(CharacterRace race, CharacterGender gender) => StepRace(race, gender, -1);

    /// <summary>The next-race button (0x5455D0): a man steps to the race after (race 7 wraps to 0); a woman to the nearest race after
    /// it that allows women, wrapping to the first. The background is cleared when the race changes.</summary>
    [OriginalFunction(0x5455D0, OriginalFunctionScope.Partial)]
    public static CreationPress NextRace(CharacterRace race, CharacterGender gender) => StepRace(race, gender, 1);

    /// <summary>
    /// The gender buttons (0x545350, 0x545440; both toggle): the other gender when the race allows it. The original clears the
    /// background before it asks the race, so a press on a race without women clears it and changes nothing else (NW4); with
    /// <paramref name="rules"/>' Steamwoven setting that press changes nothing.
    /// </summary>
    [OriginalFunction(0x545350, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x545440, OriginalFunctionScope.Partial)]
    public static CreationPress ToggleGender(CharacterRace race, CharacterGender gender, CharacterCreationRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Check(race, gender);
        var other = gender == CharacterGender.Male ? CharacterGender.Female : CharacterGender.Male;
        return Allows(race, other) ? new(race, other, true, true) : new(race, gender, false, rules.RefusedGenderClearsBackground);
    }

    private static CreationPress StepRace(CharacterRace race, CharacterGender gender, int step)
    {
        Check(race, gender);
        var next = (int)race;
        // At most RaceCount steps: human allows women, so a woman always finds a race.
        for (var i = 0; i < RaceCount; i++)
        {
            next = ((next + step) % RaceCount + RaceCount) % RaceCount;
            if (Allows((CharacterRace)next, gender)) break;
        }
        return next == (int)race ? new(race, gender, false, false) : new((CharacterRace)next, gender, true, true);
    }

    private static void Check(CharacterRace race, CharacterGender gender)
    {
        if (!Allows(race, gender)) throw new ArgumentOutOfRangeException(nameof(race), $"{race} {gender} is not a creation character");
    }
}

/// <summary>
/// The two character-creation rule settings of owner decision D8 (contract docs/world/rule-modes.md): <see cref="Steamwoven"/> (the
/// original with the recorded fixes, the default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour). Each switch is a
/// behaviour of the original game confirmed in its executable (verification ledger). The profile holds no state, so it may change
/// between any two presses.
/// </summary>
public sealed record CharacterCreationRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ledger NW4: a gender button of the new-character screen clears the background before it asks whether the race allows the
    /// other gender, so on a dwarf, gnome, halfling or half-ogre the press drops the chosen background (and its effect) and changes
    /// nothing visible (the screen is not even drawn again). Steamwoven leaves the background alone when the gender does not change
    /// (a gameplay bug under the owner's rule of 2026-10-28, ARC-UI-030).
    /// </summary>
    public bool RefusedGenderClearsBackground { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly CharacterCreationRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly CharacterCreationRuleProfile Original = new() { Name = "original", RefusedGenderClearsBackground = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<CharacterCreationRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static CharacterCreationRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
