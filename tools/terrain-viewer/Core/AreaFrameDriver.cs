using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-124 (MIG12 S5): the frame source of a map that is drawn unlit. The ambient driver (<see cref="AreaAmbientDriver"/>) owns the frame step
/// of a lit map because the lighting follows the clock; a map whose lighting sources were not admitted has no ambient driver, but the game does not
/// depend on the lighting (ARC-WORLD-057 gave such a map its session): the original's clocks, event passes and AI do not care how the map is lit, so
/// this driver runs the same <see cref="FrameStep"/> (the session's three clocks, its event queue, the participants such as the AI host, the
/// change-of-place ping) and holds the same Menu pause while the Esc menu (or a save or load list) is open, with no lighting selection to follow. Before it an unlit map's session
/// clock stood still and its runs stepped on Godot time; since a lit map's driver and this one are both the frame source of the one session, time
/// goes on across both kinds of map. Caller-serialized like the session.
/// </summary>
public sealed class AreaFrameDriver
{
    private readonly GameSessionOwner _session;
    private bool _holdsPause;

    private AreaFrameDriver(GameSessionOwner session, FrameStep frame)
    {
        _session = session; Frame = frame;
        _holdsPause = session.QueryClockSnapshot().PauseDisposition == new PauseDisposition(true, PauseReason.Menu);
    }

    /// <summary>The session whose frames this driver spends.</summary>
    public GameSessionOwner Session => _session;

    /// <summary>The frame step; it always exists and runs the event passes over the world given at creation.</summary>
    public FrameStep Frame { get; }

    /// <summary>True while this driver holds the session clock in a Menu pause for the Esc menu.</summary>
    public bool MenuPaused => _holdsPause && _session.QueryClockSnapshot().PauseDisposition == new PauseDisposition(true, PauseReason.Menu);

    /// <summary>Binds to a session. As the ambient driver's, a new driver's first frame admits nothing (the reference is reset), and a Menu pause already on the
    /// session counts as held.</summary>
    public static AreaFrameDriver Create(GameSessionOwner session, ISessionEventWorld? events = null, Func<SessionSpellCaster?>? spells = null,
        Action? placePing = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.ResetFrameReference();
        return new(session, new FrameStep(session, events, spells: spells, placePing: placePing));
    }

    /// <summary>One host frame: hold or release the Menu pause for <paramref name="menuOpen"/> (the Esc menu or a list it opens, the one raiser of the pause; a
    /// pause of another reason is left alone), then run the frame step at the host stamp, as <see cref="AreaAmbientDriver.Tick"/> does.
    /// <paramref name="frozen"/> is a frame in which the original does not ping (the death cover and its slide show, TL25).</summary>
    public FrameStepReport Tick(long hostMilliseconds, bool menuOpen, bool frozen = false)
    {
        var clock = _session.QueryClockSnapshot();
        if (menuOpen && !clock.PauseDisposition.Paused) { Mutate(_session.SetPaused(clock.Token, new(true, PauseReason.Menu))); _holdsPause = true; }
        else if (!menuOpen && MenuPaused) { Mutate(_session.SetPaused(clock.Token, default)); _holdsPause = false; }
        else if (!MenuPaused) _holdsPause = false;
        return Frame.Run(hostMilliseconds, frozen);
    }

    private static void Mutate(ClockMutationResult result)
    {
        if (result.Disposition == ClockMutationDisposition.Rejected) throw new InvalidOperationException("AREA_FRAME_CLOCK_" + result.Failure);
    }
}
