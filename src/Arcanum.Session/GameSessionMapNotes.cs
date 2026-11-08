namespace Arcanum.Session;

/// <summary>
/// ARC-UI-110 (Steamwoven rules, no original counterpart; the town maps' notes are ARC-UI-104): the notes of the world map's two views, part of the story domain. The map
/// view and the continent view each keep their own list, at pixels of the world-map picture.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>The notes a view of the world map holds (<see cref="SessionStoryLimits.MapNoteView"/> or <see cref="SessionStoryLimits.ContinentNoteView"/>), null for none or for a view outside them.</summary>
    public SessionMapNotes? QueryMapNotes(int view)
    {
        ThrowIfDisposed();
        return SessionStoryLimits.IsMapNoteView(view) && _story.MapNotes.TryGetValue(view, out var notes) ? notes : null;
    }

    /// <summary>
    /// Keeps the notes of a world-map view (the viewer's quill hands the whole list over): the same list is NoChange; an empty list removes the view's entry; a list over the
    /// limits or not canonical (ids not ascending from 200 below the next id, text over 249 characters, a pixel outside the picture) and a view outside the two are refused
    /// (InvalidMapNotes). One revision.
    /// </summary>
    public GameSessionMutationResult RecordMapNotes(GameSessionVersion expected, int view, int nextId, IReadOnlyList<SessionTownNote> notes)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(notes);
        if (!SessionStoryLimits.IsMapNoteView(view)) return Reject(GameSessionFailure.InvalidMapNotes);
        var candidate = new SessionMapNotes(view, nextId, notes);
        if (notes.Count > 0 && !candidate.IsValid()) return Reject(GameSessionFailure.InvalidMapNotes);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var held = _story.MapNotes.TryGetValue(view, out var current);
        if (notes.Count == 0)
        {
            if (!held) return NoChange();
            if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
            _story.MapNotes.Remove(view);
            return Changed();
        }
        if (held && current!.NextId == nextId && current.Notes.SequenceEqual(candidate.Notes)) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.MapNotes[view] = candidate;
        return Changed();
    }
}
