using System.Collections.Immutable;
using Arcanum.Simulation.Technology;

namespace Arcanum.Session;

/// <summary>The written item fields a reading needs (ARC-TECHNOLOGY-003): its written subtype and its first text line, which a
/// schematic document (subtype 5) uses as its schematic number. The host reads them with the combat field reader
/// (<c>CombatFields.Written</c>, a prototype or an instance over it).</summary>
public readonly record struct SessionWrittenFacts(int Subtype, int TextStartLine)
{
    public static SessionWrittenFacts? FromCombatFields(Arcanum.Data.Combat.CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields.Written is { } written ? new(written.Subtype, written.TextStartLine) : null;
    }
}

/// <summary>Saved schematic knowledge of the hero (snapshot schema 25): the numbers of its found schematics in the order they
/// were registered. Learned schematics are not saved: they follow the stored degrees.</summary>
public sealed class SessionSchematicState
{
    internal SessionSchematicState(ImmutableArray<int> found) => Found = found;

    public static SessionSchematicState Empty { get; } = new([]);

    public ImmutableArray<int> Found { get; }
}

/// <summary>
/// The outcome of reading a written item (ARC-TECHNOLOGY-003): the registration's status, the schematic number for a
/// schematic document, and whether the document was destroyed. The host shows <c>mes\tech.mes</c> 89 naming the schematic
/// for <see cref="SchematicReading.Registered"/>, 88 (message type 13) for <see cref="SchematicReading.AlreadyKnown"/>, and opens
/// its reading window for <see cref="SchematicReading.NotSchematic"/>.
/// </summary>
public readonly record struct SessionReadingResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SchematicReading? Reading,
    int? Schematic, bool DocumentDestroyed, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    private ImmutableArray<int> _foundSchematics = [];

    /// <summary>The hero's found schematics in registration order (ARC-TECHNOLOGY-003), or null without a hero.</summary>
    public ImmutableArray<int>? QueryHeroFoundSchematics()
    {
        ThrowIfDisposed();
        return _hero is null ? null : _foundSchematics;
    }

    /// <summary>
    /// The hero reads a written item it holds (the reading start 0x56BB60): a schematic document (subtype 5) registers its
    /// schematic (<see cref="SchematicKnowledgeRules.Read"/>): new → appended and the document destroyed (it leaves the hero's
    /// inventory, as a deletion does), one revision; known → NoChange, the document stays. Another subtype is NoChange with
    /// <see cref="SchematicReading.NotSchematic"/>. TC8 (the session's rule mode): Steamwoven refuses a document numbered 0
    /// (NoChange, kept), Original registers it. Refused before any effect: no hero or a dead one
    /// (<see cref="GameSessionFailure.InvalidLifeTarget"/>), an invalid entity (<see cref="GameSessionFailure.InvalidItem"/>), the
    /// version, an item the hero does not hold or not a written item (<see cref="GameSessionFailure.ItemNotAvailable"/>,
    /// <see cref="GameSessionFailure.InventoryMismatch"/>), a full found list (<see cref="GameSessionFailure.BudgetExceeded"/>,
    /// <see cref="GameSessionLimits.MaxFoundSchematics"/>), the revision.
    /// </summary>
    public SessionReadingResult ReadWrittenItem(GameSessionVersion expected, string entity, SessionWrittenFacts facts)
    {
        ThrowIfDisposed();
        if (_hero is null || HeroDead()) return RejectReading(GameSessionFailure.InvalidLifeTarget);
        if (!SessionValidation.Entity(entity)) return RejectReading(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectReading(GameSessionFailure.StaleVersion);
        if (HeldKind(entity) is not { } kind) return RejectReading(GameSessionFailure.ItemNotAvailable);
        if (kind.Type != SessionItemType.Written) return RejectReading(GameSessionFailure.InventoryMismatch);
        var registration = SchematicKnowledgeRules.Read(_foundSchematics, facts.Subtype, facts.TextStartLine, _limits.MaxFoundSchematics,
            _rules.Technology);
        int? number = facts.Subtype == SchematicKnowledgeRules.SchematicSubtype ? facts.TextStartLine : null;
        if (registration.Reading == SchematicReading.ListFull) return RejectReading(GameSessionFailure.BudgetExceeded);
        if (registration.Reading != SchematicReading.Registered)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, registration.Reading, number, false, Version);
        // The document is destroyed: a deletion record unless the budget of durability records is spent.
        if (_durability.Count >= _limits.MaxDurabilityRecords && !_durability.Records.ContainsKey(entity))
            return RejectReading(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectReading(GameSessionFailure.RevisionExhausted);
        _foundSchematics = registration.Found;
        Delete(entity);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, registration.Reading, number, true, Version);
    }

    private SessionReadingResult RejectReading(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, false, Version);

    // Only a hero has found schematics; a number appears once; the list fits the limit.
    private static GameSessionFailure ImportSchematics(SessionSchematicState saved, bool hero, GameSessionLimits limits, out ImmutableArray<int> found)
    {
        found = [];
        if (saved.Found.IsDefault) return GameSessionFailure.InvalidSchematics;
        if (saved.Found.Length > 0 && !hero) return GameSessionFailure.InvalidSchematics;
        if (saved.Found.Length > limits.MaxFoundSchematics) return GameSessionFailure.BudgetExceeded;
        if (saved.Found.Distinct().Count() != saved.Found.Length) return GameSessionFailure.InvalidSchematics;
        found = saved.Found;
        return GameSessionFailure.None;
    }
}
