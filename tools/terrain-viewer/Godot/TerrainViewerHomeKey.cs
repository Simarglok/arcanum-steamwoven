using Arcanum.Input;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-INPUT-1122 (SA44, K6; docs/architecture/interface-modes.md): the Home key of the game view (0x54EB60, reached from the handler 0x54DE50 on the key's
// release in the modes 0, 1, 2 and 18, the F1–F6 order keys' branch) centres the view on the player character at the current zoom and does nothing without
// one. The original's second case (the view already centred and a round runs) centres on the round's current participant, which the handler's own gate makes
// the player character again (it returns before the Home branch while another has the turn), so here every case is the one centring the inventory's open
// does too (`CentreGameViewOnHolder`, 0x4B8CE0, clamped to the map's rectangle). Like F1–F6, a press arms the key only while the world owns the input; the
// release acts only while it still does.
public partial class TerrainViewerApp
{
    private bool _homeKeyArmed;

    // The centring's last decision, for the smoke: what OriginalHomeKey chose for the last accepted release.
    private HomeKeyTarget _homeKeyLastTarget = HomeKeyTarget.Nothing;
    internal HomeKeyTarget HomeKeyLastTargetForSelfTest => _homeKeyLastTarget;

    /// <summary>Home without modifiers while the AREA view has a hero session: centres the view on the hero on the release of a press the world saw. The world
    /// map's own Home (its panel owns the keys) and the diagnostic worlds without a session are not this key.</summary>
    private bool RouteHomeKey(WorldKeyDecision decision, WorldKeyModifiers modifiers)
    {
        if (decision.Key != WorldKey.Home || modifiers != WorldKeyModifiers.None || WorldMapOpen) return false;
        if (_areaSession is not { } session || _camera is null || _worldActor.Session is not AreaGridSession) return false;
        if (decision.Echo) return true;
        if (!OrderInputOpen)
        {
            _homeKeyArmed = false;
            _hud?.SetNotice("Home ignored: the mode or a window owns the input, another has the turn, the world has no input focus or a map change is pending");
            return true;
        }
        if (decision.Pressed) { _homeKeyArmed = true; return true; }
        if (!_homeKeyArmed) return true;
        _homeKeyArmed = false;
        var anchor = WorldAnchor;
        var round = session.QueryCombatRound();
        var target = OriginalHomeKey.Target(session.QueryHero() is not null,
            Math.Abs(_camera.State.X - anchor.X) < .5 && Math.Abs(_camera.State.Y - anchor.Y) < .5, round is not null, round is not null);
        _homeKeyLastTarget = target;
        // A round participant other than the hero cannot get here (OrderInputOpen is false in another's turn); both targets are the hero.
        if (target != HomeKeyTarget.Nothing) CentreGameViewOnHolder();
        GD.Print($"ARC-INPUT-1122 home target={target} camera={_camera.State.X:0.##},{_camera.State.Y:0.##}");
        return true;
    }
}
