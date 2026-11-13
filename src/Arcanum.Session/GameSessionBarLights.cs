namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    // ARC-UI-1542 (schema 98): the lit state of the top strip's buttons, or null while the host never recorded one.
    private SessionBarLightsState? _barLights;

    /// <summary>ARC-UI-1542: the last recorded lit state of the top strip's buttons and the Map button's face, or null when none was recorded (a
    /// new session, or one loaded from a save of schema 97 or older: all idle).</summary>
    public SessionBarLightsState? QueryBarLights() { ThrowIfDisposed(); return _barLights; }

    /// <summary>
    /// ARC-UI-1542: records the lit state the host keeps for the top strip (the original keeps it in the interface part of its save, ledger GB15). The session only holds it for the save; it never reads it. Like the lighting record, this does not move the session revision. A
    /// light slot mask outside 0..31 or a face other than 2 or 3 is <see cref="GameSessionFailure.InvalidBarLights"/>; the same state is NoChange.
    /// </summary>
    public GameSessionMutationResult RecordBarLights(SessionBarLightsState lights)
    {
        ThrowIfDisposed();
        if (!SessionValidation.BarLights(lights)) return Reject(GameSessionFailure.InvalidBarLights);
        if (_barLights == lights) return NoChange();
        _barLights = lights;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, new(_identity, _revision));
    }
}
