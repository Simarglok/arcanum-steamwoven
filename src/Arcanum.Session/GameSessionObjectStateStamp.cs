namespace Arcanum.Session;

/// <summary>ARC-RENDER-3362: the identity of a session's recorded object state (<see cref="GameSessionOwner.ObjectStateStamp"/>): the session's map-change records, how often they were
/// written, and the written object flags (an immutable map, replaced by every change). Equality is all it offers.</summary>
public readonly record struct SessionObjectStateStamp
{
    internal SessionObjectStateStamp(object changes, long writes, object flags) => (Changes, Writes, Flags) = (changes, writes, flags);

    private object Changes { get; }
    private long Writes { get; }
    private object Flags { get; }
}
