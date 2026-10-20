using System.Collections.Immutable;

namespace Arcanum.Simulation.Technology;

/// <summary>What reading a written item did to the reader's found schematics.</summary>
public enum SchematicReading
{
    /// <summary>A new schematic: appended to the found list; the document is destroyed and message 89 of <c>mes\tech.mes</c>
    /// names it.</summary>
    Registered,
    /// <summary>The schematic was found before: message 88 (message type 13); the document stays.</summary>
    AlreadyKnown,
    /// <summary>The document is not a schematic (another written subtype): the original opens its reading window instead.</summary>
    NotSchematic,
    /// <summary>Steamwoven (TC8): a schematic document numbered 0 registers nothing and stays.</summary>
    RefusedZero,
    /// <summary>The found list is at the session's limit (the original has none).</summary>
    ListFull,
}

/// <summary>The outcome of one registration: the status and the found list after it.</summary>
public readonly record struct SchematicRegistration(SchematicReading Reading, ImmutableArray<int> Found);

/// <summary>
/// ARC-TECHNOLOGY-003: the found-schematic registration (contract docs/world/schematic-knowledge-runtime.md, ledger
/// "Schematic knowledge"): reading a written item of subtype 5 appends its schematic number (the item's start text line) to
/// the reader's found list once. Pure over explicit values; the session owns the list and the document.
/// </summary>
public static class SchematicKnowledgeRules
{
    /// <summary>The written subtype of a schematic document.</summary>
    public const int SchematicSubtype = 5;

    /// <summary>
    /// The registration of <paramref name="number"/> for a reader whose found list is <paramref name="found"/> (0x4B0120, reached
    /// from the reading start 0x56BB60 for subtype 5): a linear search; a number already in the list is
    /// <see cref="SchematicReading.AlreadyKnown"/>; otherwise it is appended at the end, without a limit in the original
    /// (<paramref name="limit"/> here). TC8: <paramref name="profile"/> decides whether number 0 registers.
    /// </summary>
    [OriginalFunction(0x4B0120, OriginalFunctionScope.Partial)]
    public static SchematicRegistration Register(ImmutableArray<int> found, int number, int limit, TechnologyRuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (found.IsDefault) throw new ArgumentException("The found list is required.", nameof(found));
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        if (number == 0 && !profile.RegistersSchematicZero) return new(SchematicReading.RefusedZero, found);
        foreach (var known in found)
            if (known == number) return new(SchematicReading.AlreadyKnown, found);
        if (found.Length >= limit) return new(SchematicReading.ListFull, found);
        return new(SchematicReading.Registered, found.Add(number));
    }

    /// <summary>The reading start's branch (0x56BB60): a schematic document registers, any other subtype opens the reading
    /// window (<see cref="SchematicReading.NotSchematic"/>). The dead-reader refusal is the caller's.</summary>
    [OriginalFunction(0x56BB60, OriginalFunctionScope.Partial)]
    public static SchematicRegistration Read(ImmutableArray<int> found, int subtype, int startLine, int limit, TechnologyRuleProfile profile) =>
        subtype == SchematicSubtype ? Register(found, startLine, limit, profile) : new(SchematicReading.NotSchematic, found);
}
