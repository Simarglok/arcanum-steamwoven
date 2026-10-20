namespace Arcanum.Data.Magic;

/// <summary>
/// ARC-MAGIC-001: one of the 223 spell records of <c>rules\spelllist.mes</c> as the original's load leaves it (checked in the
/// 1.0.7.4 executable, ledger "Spell records"; contract docs/formats/spell-records.md). Lines +0..+4 of the record are read here;
/// line +0 (the record's target area) and the component lines (+5 onward) are kept as text for the component reader.
/// Records 0..79 are the college spells (16 colleges × 5 ranks); records 80..139 are magic outside any college; records
/// 140..222 are technology. Seconds are the data's unit; the run turns them into game time.
/// </summary>
public sealed record SpellRecord
{
    public const int Count = 223, CollegeSpells = 80, FirstTechnology = 140, Colleges = 16, Ranks = 5, Actions = 5;

    public required int Number { get; init; }
    /// <summary>The college (n / 5) of a college spell 0..79; null otherwise.</summary>
    public int? College => Number < CollegeSpells ? Number / Ranks : null;
    /// <summary>The rank 1..5 (n % 5 + 1) of a college spell; null otherwise.</summary>
    public int? Rank => Number < CollegeSpells ? Number % Ranks + 1 : null;

    /// <summary><c>IQ:</c>, the minimum willpower to learn, cast and maintain (LM6): {6, 9, 12, 15, 18} by rank unless the
    /// record gives one; 0 for every record from 80, whatever it gives.</summary>
    public required int MinimumWillpower { get; init; }
    /// <summary><c>Cost:</c> (0).</summary>
    public required int Cost { get; init; }
    /// <summary><c>Maintain:(cost@period)</c> (0, 0); a period above 0 makes the spell maintained.</summary>
    public required int MaintainCost { get; init; }
    public required int MaintainPeriod { get; init; }
    /// <summary><c>Duration:(seconds@stat)</c> (0, −1 for no stat).</summary>
    public required int DurationSeconds { get; init; }
    public required int DurationStat { get; init; }
    /// <summary><c>DurationStatInfo:(level@modifier)</c> (0, 1), read only after a <c>Duration:</c> whose stat is not −1.</summary>
    public required int DurationStatLevel { get; init; }
    public required int DurationStatModifier { get; init; }
    /// <summary><c>DurationTriggerCount:</c> (0); a value of 0 or less is not stored.</summary>
    public required int DurationTriggerCount { get; init; }
    /// <summary><c>Resist:(stat@value)</c>: the index in <see cref="SpellNameTables.StatKeys"/> (−1 for none) and the modifier (0).</summary>
    public required int ResistStat { get; init; }
    public required int ResistModifier { get; init; }
    /// <summary><c>Range:</c> (99).</summary>
    public required int Range { get; init; }
    /// <summary>Forced for records from 140, or set by a non-zero <c>Is_Tech:</c>.</summary>
    public required bool IsTechnology { get; init; }
    /// <summary>A non-zero <c>ChargesFromCells:</c>.</summary>
    public required bool ChargesFromCells { get; init; }
    /// <summary><c>ChargeBeginCost: 0</c>: no payment at BEGIN (a non-zero value changes nothing).</summary>
    public required bool SkipsBeginCost { get; init; }
    /// <summary><c>Info:</c> (neutral).</summary>
    public required SpellInfo Info { get; init; }
    /// <summary>False only for a non-zero <c>Disabled:</c>, which refuses a PC's cast from the spell UI.</summary>
    public required bool Enabled { get; init; }
    /// <summary><c>No_Resist:</c> and <c>No_Reflect:</c> by presence, whatever their value.</summary>
    public required bool NoResist { get; init; }
    public required bool NoReflect { get; init; }

    /// <summary>Line +2: <c>CastingAnim:</c> and <c>Missile:</c> (−1).</summary>
    public required int CastingAnimation { get; init; }
    public required int Missile { get; init; }
    /// <summary>Line +2: <c>[Action]Caster:</c> and <c>[Action]Target:</c> per <see cref="SpellAction"/> (−1).</summary>
    public required IReadOnlyList<int> CasterEyeCandy { get; init; }
    public required IReadOnlyList<int> TargetEyeCandy { get; init; }

    /// <summary>Line +3: <c>No_Stack:</c> as written (0).</summary>
    public required int NoStack { get; init; }
    /// <summary>Line +3: spell-flag bits (<see cref="SpellNameTables.SpellFlags"/>) of <c>Cancels_SF:</c>,
    /// <c>Cancels_EnvSF:</c>, <c>Disallowed_SF:</c> (caster) and <c>Disallowed_TSF:</c> (target); 0 when absent.</summary>
    public required uint CancelsSpellFlags { get; init; }
    public required uint CancelsEnvironmentSpellFlags { get; init; }
    public required uint DisallowedCasterSpellFlags { get; init; }
    public required uint DisallowedTargetSpellFlags { get; init; }
    /// <summary>Line +3: critter-flag bits (<see cref="SpellNameTables.CritterFlags"/>) of <c>Disallowed_TCF:</c>.</summary>
    public required uint DisallowedTargetCritterFlags { get; init; }
    /// <summary>Line +3: trigger bits (<see cref="SpellNameTables.ItemTriggerValues"/>) of <c>ItemTriggers:</c>.</summary>
    public required uint ItemTriggers { get; init; }

    /// <summary>Line +4: the ten <c>AI_*</c> priorities in <see cref="SpellNameTables.AiKeys"/> order, −1 when absent.</summary>
    public required IReadOnlyList<int> AiPriorities { get; init; }
    /// <summary>The second value of <c>AI_Defensive:(value@second)</c> (0).</summary>
    public required int AiDefensiveSecond { get; init; }

    /// <summary>Whether lines +0..+4 exist in the file.</summary>
    public required IReadOnlyList<bool> LinesPresent { get; init; }
    /// <summary>The keys of lines +1..+4 the original's cursor read, in reading order (a census and diagnostic aid).</summary>
    public required IReadOnlyList<string> KeysRead { get; init; }
    /// <summary>Line +0, the record's target area, as text (null when missing); typed by the component reader.</summary>
    public required string? AreaLine { get; init; }
    /// <summary>Lines +5 onward up to the first missing one (at most 44), as text; typed by the component reader.</summary>
    public required IReadOnlyList<string> ComponentLines { get; init; }

    public bool Equals(SpellRecord? other) => other is not null && Key() == other.Key();

    public override int GetHashCode() => Key().GetHashCode(StringComparison.Ordinal);

    private string Key() => string.Join('|', Number, MinimumWillpower, Cost, MaintainCost, MaintainPeriod, DurationSeconds, DurationStat,
        DurationStatLevel, DurationStatModifier, DurationTriggerCount, ResistStat, ResistModifier, Range, IsTechnology, ChargesFromCells,
        SkipsBeginCost, Info, Enabled, NoResist, NoReflect, CastingAnimation, Missile, string.Join(',', CasterEyeCandy),
        string.Join(',', TargetEyeCandy), NoStack, CancelsSpellFlags, CancelsEnvironmentSpellFlags, DisallowedCasterSpellFlags,
        DisallowedTargetSpellFlags, DisallowedTargetCritterFlags, ItemTriggers, string.Join(',', AiPriorities), AiDefensiveSecond,
        string.Join(',', LinesPresent), string.Join(',', KeysRead), AreaLine ?? "\0", string.Join('\0', ComponentLines));
}
