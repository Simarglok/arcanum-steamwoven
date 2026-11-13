using Arcanum.Data.Area;
using Arcanum.Presentation.Audio;
using Arcanum.Session;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Sound;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-204 (B16, MIG11 presentation, FR-WORLD-015): what the player sees and hears of a change of place. The session runs the plan
// (ARC-DEV-113) and the hosts' Run answers the steps that are the viewer's: the fade-out (request flag 0x2) holds the change until the
// screen is black, as the original's blocking fade does; the sound (0x10) plays on the mover at its departure point after the fade-out
// (SN29); a movie (0x1, 0x40) is NOT READY (the originals are Bink videos and the viewer has no decoder); the fade-in (0x4) runs once the
// change ran to its end. A map load shows black with the original's clock (art 601, CP17) until the destination is there. The Crash Site's
// two scenes are both loaded, so its swap has no load to show.
public partial class TerrainViewerApp
{
    private readonly AreaTransitionScreen _transitionScreen = new();
    private AreaTransitionPanel? _transitionPanel;
    private bool _transitionClockRead;
    // A hero change that waits for its fade-out: runs when the screen is black.
    private Func<bool>? _fadingChange;
    private GameSessionOwner? _fadingOwner;
    private object? _fadingGrid;
    private int _transitionLoads;
    private int _transitionClockFramesDrawn;
    private int _transitionSoundsPlayed;
    private int _transitionMoviesSkipped;

    /// <summary>True while a hero change waits for its fade-out to finish.</summary>
    private bool TransitionFadePending => _fadingChange is not null;

    /// <summary>Per frame, on the frame's real time (a smoke's frame brings its own tenth of a second): moves the veil and the clock on, draws
    /// them, and carries out the hero change that waited for the black screen.</summary>
    private void TickTransitionScreen(double milliseconds)
    {
        if (!_transitionScreen.Covering && _fadingChange is null && _transitionPanel is null) return;
        // A frame that took long (a map published on the main thread) does not eat a fade: one step is at most a quarter of a second.
        _transitionScreen.Advance(Math.Min(milliseconds, 250));
        // The change that waited is the session's and the actor's of the request: a load, a reset or a death that replaced them drops it.
        if (_fadingChange is not null && (!ReferenceEquals(_fadingOwner, _areaSession) || !ReferenceEquals(_fadingGrid, _worldActor.Session)
            || _worldLifecycleSession is not null || _worldResetPending || _worldReloadPending))
        {
            GD.Print("ARC-DEV-204 change after the fade-out dropped: the session or the actor was replaced");
            ClearTransition();
        }
        if (_fadingChange is not null && _transitionScreen.IsBlack)
        {
            var change = _fadingChange; _fadingChange = null; _fadingOwner = null; _fadingGrid = null;
            WorldObservation(() => change());
        }
        DrawTransitionScreen();
    }

    private void DrawTransitionScreen()
    {
        if (_transitionPanel is null && !_transitionScreen.Covering) return;
        var panel = TransitionPanel();
        panel.Apply(_transitionScreen);
        if (panel.ClockFrame >= 0) _transitionClockFramesDrawn++;
    }

    private AreaTransitionPanel TransitionPanel()
    {
        if (_transitionPanel is null)
        {
            _transitionPanel = new AreaTransitionPanel();
            AddChild(_transitionPanel);
        }
        return _transitionPanel;
    }

    /// <summary>Reads the clock's art once a module; without a game folder (the self-test) or when it does not read, a load shows black alone.</summary>
    private void EnsureTransitionClock()
    {
        if (_transitionClockRead) return;
        // ARC-UI-1362: a save chosen in the main menu loads before any map was published, so the module comes from the launch arguments then;
        // with no module known yet nothing is latched and the next load tries again.
        if ((_arguments ?? _launchArguments)?.ModulePath is not { } module) return;
        _transitionClockRead = true;
        if (SoundCatalogFor(module) is null || _soundContent?.Stack is not { } stack) { GD.Print("ARC-DEV-204 load indicator: no content stack, a black screen alone"); return; }
        var load = AreaInterfaceArt.Load(stack, [AreaTransitionScreen.IndicatorArt]).GetValueOrDefault(AreaTransitionScreen.IndicatorArt);
        if (load is not { Status: AreaInterfaceArtStatus.Ready }) { GD.Print($"ARC-DEV-204 load indicator art {AreaTransitionScreen.IndicatorArt}: {load?.Status.ToString() ?? "not read"} {load?.Detail}"); return; }
        TransitionPanel().SetClock(load.Frames, load.FramesPerSecond);
        GD.Print($"ARC-DEV-204 load indicator {load.Name} {load.Frames.Count} frames at {load.FramesPerSecond}/s");
    }

    /// <summary>A map is loading (a change of place, a load of a save): black with the clock until it is there.</summary>
    private void BeginTransitionLoad()
    {
        EnsureTransitionClock();
        _transitionLoads++;
        _transitionScreen.BeginLoad();
        DrawTransitionScreen();
    }

    /// <summary>The map of a change of place is there (or the load failed): the clock goes. The black stays until
    /// <see cref="ReleaseTransition"/> or the fade-in.</summary>
    private void EndTransitionLoad()
    {
        _transitionScreen.EndLoad();
        DrawTransitionScreen();
    }

    /// <summary>The draw is given back after the change (CP2): a black screen clears; a fade-in goes on.</summary>
    private void ReleaseTransition()
    {
        _transitionScreen.Release();
        DrawTransitionScreen();
    }

    /// <summary>A refused or abandoned change, a reset: no fade, no veil, no change waiting.</summary>
    private void ClearTransition()
    {
        _fadingChange = null; _fadingOwner = null; _fadingGrid = null;
        _transitionScreen.Clear();
        DrawTransitionScreen();
    }

    /// <summary>ARC-MAGIC-058 (MT9b): the hero's change to another map ends the spell runs that name an object that does not travel (0x44F7A0), through the cast host of the
    /// shown map, which is where their objects are; without a cast host (a map mode with no spell data) there are no runs to end.</summary>
    private void EndSpellRunsLeavingMap(SessionMoveList moveList) => SpellCasterNow()?.EndRunsLeavingMap(moveList.Travelling);

    /// <summary>The viewer's part of the plan's presentation steps for a hero change (<paramref name="fade"/>: the script's sound and movie
    /// numbers, when the request came from a script's fade-and-teleport).</summary>
    private bool RunTransitionStep(ChangeOfPlaceStep step, Arcanum.Scripting.Scr.ScrTeleportFade? fade)
    {
        switch (step)
        {
            case ChangeOfPlaceStep.PlaySound:
                if (fade is { Sound: > 0 } && ActorCell() is { } cell
                    && PlayCueAt(new SoundCue(fade.Sound), cell.X, cell.Y, PositionalSize.Large, "teleport").Result?.Started == true) _transitionSoundsPlayed++;
                break;
            case ChangeOfPlaceStep.PlayFirstMovie:
            case ChangeOfPlaceStep.PlaySecondMovie:
                _transitionMoviesSkipped++;
                GD.Print($"ARC-DEV-204 movie {(step == ChangeOfPlaceStep.PlayFirstMovie ? fade?.Movie.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "first" : "second")} not shown (NOT READY: no Bink playback)");
                break;
            case ChangeOfPlaceStep.FadeInScreen:
                _transitionScreen.BeginFadeIn();
                DrawTransitionScreen();
                break;
        }
        return true;
    }
}
