using Arcanum.Data.Written;
using Arcanum.Simulation.Technology;

namespace Arcanum.Session;

/// <summary>
/// The outcome of using a written item (ARC-UI-049, the item use 0x462CC0 → the reading start 0x56BB60): a schematic document's
/// registration (as <see cref="GameSessionOwner.ReadWrittenItem"/>), or the document the reading window shows (<see cref="Document"/>;
/// its <see cref="WrittenDocument.Status"/> names a document the original does not show, such as a subtype whose text file did not
/// load). Reading changes no state besides the registration (ledger WR rows).
/// </summary>
public readonly record struct SessionWrittenUse(GameSessionDisposition Disposition, GameSessionFailure Failure, SchematicReading? Reading,
    int? Schematic, bool DocumentDestroyed, WrittenDocument? Document, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The hero uses a written item it holds (0x462CC0 then 0x56BB60): the item's use script (attachment point 1) would run first and
    /// can cancel the use; an item with a spell mana store or the magical item flag takes the item-spell path instead; then a schematic
    /// document (subtype 5) registers through <see cref="ReadWrittenItem"/> (TC8, the document kept or destroyed as there), and any
    /// other subtype gives the document the window shows, read from <paramref name="texts"/> (no state change). Refused before any
    /// effect: no hero or a dead one (<see cref="GameSessionFailure.InvalidLifeTarget"/>: the original's start returns for a dead
    /// reader), an invalid entity (<see cref="GameSessionFailure.InvalidItem"/>), the version, an item the hero does not hold
    /// (<see cref="GameSessionFailure.ItemNotAvailable"/>) or that is not written (<see cref="GameSessionFailure.InventoryMismatch"/>),
    /// an item with a use script (<see cref="GameSessionFailure.ItemScriptUnsupported"/>: not run here) or on the item-spell path
    /// (<see cref="GameSessionFailure.ItemTypeUnsupported"/>), a subtype outside 0..6 or a picture art outside 0..4095
    /// (<see cref="GameSessionFailure.InvalidItem"/>: the original reads out of bounds, both modes refuse), then the registration's own
    /// refusals. The 4 action points a use costs in a turn-based round are not charged (the viewer reads outside rounds).
    /// </summary>
    public SessionWrittenUse UseWrittenItem(GameSessionVersion expected, string entity, SessionWrittenFacts facts, WrittenTexts texts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(texts);
        if (_hero is null || HeroDead()) return RejectUse(GameSessionFailure.InvalidLifeTarget);
        if (!SessionValidation.Entity(entity)) return RejectUse(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectUse(GameSessionFailure.StaleVersion);
        if (HeldKind(entity) is not { } kind) return RejectUse(GameSessionFailure.ItemNotAvailable);
        if (kind.Type != SessionItemType.Written) return RejectUse(GameSessionFailure.InventoryMismatch);
        if (facts.UseScript) return RejectUse(GameSessionFailure.ItemScriptUnsupported);
        if (facts.ItemSpell) return RejectUse(GameSessionFailure.ItemTypeUnsupported);
        if (facts.Subtype == SchematicKnowledgeRules.SchematicSubtype)
        {
            var read = ReadWrittenItem(expected, entity, facts);
            return new(read.Disposition, read.Failure, read.Reading, read.Schematic, read.DocumentDestroyed, null, read.Version);
        }
        var document = QueryWrittenDocument(facts, texts);
        if (document.Status is WrittenReadStatus.SubtypeOutOfRange or WrittenReadStatus.ArtOutOfRange) return RejectUse(GameSessionFailure.InvalidItem);
        return new(GameSessionDisposition.NoChange, GameSessionFailure.None, SchematicReading.NotSchematic, null, false, document, Version);
    }

    /// <summary>The document the reading window shows for a written item's facts (0x56BC00 / 0x56BCC0), the prototype 14092 (the executable's 14072) alternate
    /// newspaper included; changes nothing and needs no hero (a script opener reads the same way).</summary>
    public WrittenDocument QueryWrittenDocument(SessionWrittenFacts facts, WrittenTexts texts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(texts);
        return texts.Read(facts.Subtype, facts.TextStartLine, facts.Prototype == WrittenTexts.NewspaperAlternatePrototype);
    }

    private SessionWrittenUse RejectUse(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, false, null, Version);
}
