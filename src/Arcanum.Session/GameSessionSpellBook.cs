using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// A mastery change (ARC-MAGIC-004): the disposition, the failure, the spell-book rule's status (null when refused before the rule
/// ran), the book after the call (null when refused before the rule ran) and the version.
/// </summary>
public sealed record SessionSpellBookResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SpellBookStatus? Status,
    SpellBook? Book, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>The hero's spell book (its college ranks and mastered college), or null without a hero (ARC-MAGIC-004).</summary>
    public SpellBook? QueryHeroSpellBook()
    {
        ThrowIfDisposed();
        return _hero is null ? null : new(_hero.State.Advancement.Colleges, _hero.State.Advancement.MasteredCollege);
    }

    /// <summary>
    /// An NPC's spell book from the session's advancement record (a levelled follower, or an NPC given mastery here), or null while
    /// the session holds none: the host then reads the book from the critter's fields (<see cref="SpellBookRules.FromFields"/>). An
    /// invalid entity id throws.
    /// </summary>
    public SpellBook? QueryNpcSpellBook(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return _advancement.Npcs.TryGetValue(entity, out var record) ? SpellRankLane.Book(record.Sheet) : null;
    }

    /// <summary>
    /// Gives the hero mastery of <paramref name="college"/> (0x4B1CF0, the script action's; MT21 calls it): once, and only for a
    /// college with all five ranks (<see cref="SpellBookRules.Master"/>). Refused, in order: no hero
    /// (<see cref="GameSessionFailure.SpellBookUnavailable"/>); a college outside 0..15 (<see cref="GameSessionFailure.InvalidSpellBook"/>);
    /// the version; the rule (<see cref="GameSessionFailure.SpellBookRefused"/> with its status); the revision.
    /// </summary>
    public SessionSpellBookResult MasterHeroCollege(GameSessionVersion expected, int college)
    {
        ThrowIfDisposed();
        if (_hero is null) return RejectBook(GameSessionFailure.SpellBookUnavailable);
        if (college is < 0 or >= SpellBookRules.Colleges) return RejectBook(GameSessionFailure.InvalidSpellBook);
        if (!IsCurrent(expected)) return RejectBook(GameSessionFailure.StaleVersion);
        var hero = _hero;
        var change = SpellBookRules.Master(QueryHeroSpellBook()!, college);
        if (!change.Succeeded) return new(GameSessionDisposition.Rejected, GameSessionFailure.SpellBookRefused, change.Status, change.Book, Version);
        if (_revision == long.MaxValue) return RejectBook(GameSessionFailure.RevisionExhausted);
        hero.Apply(AdvancementDomain.WithSheet(hero.State, AdvancementDomain.HeroSheet(hero.State) with { MasteredCollege = college },
            hero.State.Experience));
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, change.Status, change.Book, Version);
    }

    /// <summary>
    /// Gives an NPC mastery of <paramref name="college"/>, as <see cref="MasterHeroCollege"/>, on its advancement record:
    /// <paramref name="loaded"/> is its map data (read only while the session has no record of it, as <see cref="LevelFollower"/>
    /// takes it). Refused, in order: an invalid record (<see cref="GameSessionFailure.InvalidAdvancement"/>) or college
    /// (<see cref="GameSessionFailure.InvalidSpellBook"/>); the version; the rule; a new record beyond the budget; the revision.
    /// </summary>
    public SessionSpellBookResult MasterNpcCollege(GameSessionVersion expected, SessionNpcAdvancement loaded, int college)
    {
        ThrowIfDisposed();
        if (!AdvancementDomain.Valid(loaded)) return RejectBook(GameSessionFailure.InvalidAdvancement);
        if (college is < 0 or >= SpellBookRules.Colleges) return RejectBook(GameSessionFailure.InvalidSpellBook);
        if (!IsCurrent(expected)) return RejectBook(GameSessionFailure.StaleVersion);
        var record = _advancement.Npcs.TryGetValue(loaded.Entity, out var saved) ? saved : loaded;
        var change = SpellBookRules.Master(SpellRankLane.Book(record.Sheet), college);
        if (!change.Succeeded) return new(GameSessionDisposition.Rejected, GameSessionFailure.SpellBookRefused, change.Status, change.Book, Version);
        if (!_advancement.Npcs.ContainsKey(record.Entity) && _advancement.Npcs.Count >= _limits.MaxAdvancementRecords)
            return RejectBook(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectBook(GameSessionFailure.RevisionExhausted);
        _advancement.Npcs[record.Entity] = record with { Sheet = record.Sheet with { MasteredCollege = college } };
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, change.Status, change.Book, Version);
    }

    private SessionSpellBookResult RejectBook(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, Version);
}
