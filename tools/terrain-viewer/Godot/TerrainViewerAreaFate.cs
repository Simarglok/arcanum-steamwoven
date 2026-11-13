using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1112: the Fate Point window (original 0x56FC70, art 292; docs/rendering/terrain-viewer.md#fate-point-window-arc-ui-1112, ledger FW1–FW6) over an
// AREA session. The bar's Fate Points button and the key F run one function (0x56FBF0): an open window closes, otherwise it opens for a living hero. The
// window has twelve toggle buttons, one for each fate kind: a press that turns one on arms the kind (GameSessionOwner.ArmHeroFate: a point is spent; the
// full heal is applied at once), turning it off undoes it (UndoHeroFate: the point comes back). The buttons follow the hero's armed kinds after each
// answer and each frame, so a refused press pops back and a kind used by an event (or the full heal) pops up. Space closes the window (0x56FE70). The window
// is no interface mode of the stack (the original's modes are the 21 of SA37): it stays while the world runs, and it closes when another interface window or the dialog, the barter,
// reading, save-list, menu, palette or world-map windows take the screen (it opens in none of them), when the hero dies or the session is replaced (PROJECT: the original's
// callers 0x541710, 0x541D40 and 0x542200 close it with the other interfaces; which events call them is not read here).
public partial class TerrainViewerApp
{
    private CanvasLayer? _fateLayer;
    private AreaFateView? _fateView;
    private Task<AreaFateContent>? _fateContentTask;
    private AreaFateContent? _fateContent;
    private string? _fateModule;
    private GameSessionOwner? _fateSession;
    private string? _fateNotice;
    // Native observation (self-test and smoke): the arm and undo calls and their answers, newest last.
    private readonly List<(int Kind, bool Arm, GameSessionDisposition Disposition, GameSessionFailure Failure)> _fateLog = [];

    private bool FateOpen => _fateView is { Visible: true };
    private bool FateActive => _areaSession is not null && _readyMode == TerrainViewerMode.Area;

    /// <summary>Starts reading the window's art and lines for this module (once; a reload, the cave or a map transfer reuses them).</summary>
    private void BindAreaFate(TerrainViewerArguments arguments)
    {
        if (_fateModule == arguments.ModulePath && _fateContentTask is not null) return;
        _fateModule = arguments.ModulePath; _fateContent = null;
        _fateView?.CloseView();
        _fateView?.ClearArt();
        var module = arguments.ModulePath;
        _fateContentTask = SelfTestBootstrap || module is null ? Task.FromResult(AreaFateContent.Empty("self-test: no game folder"))
            : Task.Run(() => AreaFateContent.Load(module));
    }

    private AreaFateContent? AdoptFateContent()
    {
        if (_fateContent is null && _fateContentTask is { IsCompleted: true } task)
        {
            _fateContent = task.IsCompletedSuccessfully ? task.Result : AreaFateContent.Empty("load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
            GD.Print($"ARC-UI-1112 fate window art ready={_fateContent.Ready} {_fateContent.Detail}");
            if (_fateView is not null) _fateView.CloseView();
        }
        return _fateContent;
    }

    /// <summary>The view, made on first use and given its art once it has read; null while it has not (or did not).</summary>
    private AreaFateView? FateViewOrNull()
    {
        if (AdoptFateContent() is not { Ready: true } content) return null;
        if (_fateView is null)
        {
            _fateLayer = new CanvasLayer { Layer = 4, Name = "FateLayer" };
            _fateView = new AreaFateView();
            _fateLayer.AddChild(_fateView);
            AddChild(_fateLayer);
            _fateView.KindToggled += FateToggled;
            _fateView.SoundRequested += id => PlayBarSound(id, "fate button");
        }
        if (!_fateView.ArtSet)
        {
            var frames = AreaFateLayout.ArtNumbers.Where(number => number != AreaFateLayout.FontArt)
                .ToDictionary(number => number, number => content.Art[number].Frames.Select(frame => (Texture2D)InterfaceHd.Texture(frame, premultipliedAlpha: true)).ToArray());
            _fateView.SetArt(frames, new AreaArtFontFace(content.Art[AreaFateLayout.FontArt].Frames), content.Texts);
        }
        return _fateView;
    }

    /// <summary>The Fate Points button and the key F (0x56FBF0): closes an open window; otherwise opens it for the living hero.</summary>
    private void ToggleFate(string source)
    {
        var (dead, _) = HeroDownState();
        var toggle = AreaFateWindowRules.Decide(FateOpen, _areaSession?.QueryHero() is not null, dead);
        GD.Print($"ARC-UI-1112 fate window {source} {toggle}");
        switch (toggle)
        {
            case AreaFateWindowRules.Toggle.Closed: CloseFate(); return;
            case AreaFateWindowRules.Toggle.RefusedNoHero: _fateNotice = "fate points: needs a hero"; break;
            case AreaFateWindowRules.Toggle.RefusedDead: _fateNotice = "fate points: not for a dead hero"; break;
            default:
                if (FateViewOrNull() is not { } view)
                {
                    _fateNotice = _fateContent is null ? "fate points: the window's art is still reading" : "fate points: the window's art did not read";
                    break;
                }
                _fateNotice = null;
                _fateSession = _areaSession;
                view.Open();
                view.Follow(_areaSession!.QueryHeroFate());
                break;
        }
        if (_fateNotice is not null) _gameBarNotice = _fateNotice;
        RefreshAreaStatus();
    }

    private void CloseFate()
    {
        _fateView?.CloseView();
        _fateSession = null;
        _fateNotice = null;
    }

    /// <summary>A button was turned on (arms the kind) or off (undoes it, 0x56FE70); the buttons then follow the hero's fate, which pops a refused press back.</summary>
    private void FateToggled(int kind, bool on)
    {
        if (_areaSession is not { } session || _fateView is null) return;
        var result = on ? session.ArmHeroFate(session.Version, (FateKind)kind) : session.UndoHeroFate(session.Version, (FateKind)kind);
        _fateLog.Add((kind, on, result.Disposition, result.Failure));
        GD.Print($"ARC-UI-1112 fate {(on ? "arm" : "undo")} kind={kind} {result.Disposition} {result.Failure} points={session.QueryHeroFate()?.Points}");
        _fateView.Follow(session.QueryHeroFate());
        RefreshAreaStatus();
    }

    /// <summary>Per frame: the open window follows the hero's fate (a kind an event used pops up) and closes when its world went.</summary>
    private void SyncAreaFate()
    {
        if (!FateOpen) return;
        // Another interface window (the sheet, the inventory, the logbook, the schematics, the sleep window ...), the dialog and the panels that take the screen close it.
        var covered = WorldModal || ConversationOpen || BarterOpen || ItemServiceOpen || ReadingOpen || WorldMapOpen;
        if (_areaSession is not { } session || !ReferenceEquals(session, _fateSession) || session.QueryHero() is null || HeroDownState().Dead || covered)
        {
            CloseFate();
            return;
        }
        _fateView!.Place();
        _fateView.Follow(session.QueryHeroFate());
    }

    /// <summary>The original key F (both cases) of the in-game key filter calls the same function as the bar's button (K3 0x54B5D0). True when the key belongs to it.</summary>
    private bool RouteFateKey(WorldKeyDecision decision)
    {
        if (!FateActive || decision.Key != WorldKey.F) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _fateNotice = "F ignored: palette, dialog, window or UI focus owns input"; _gameBarNotice = _fateNotice; RefreshAreaStatus(); return true; }
        ToggleFate("key F");
        return true;
    }

    /// <summary>While the window is open, a fresh press of Space closes it and the key goes no further (the window's own handler takes scan code 0x39, 0x56FE70).</summary>
    private bool HandleFateKey(InputEventKey key)
    {
        if (!FateOpen || !key.Pressed || key.Echo) return false;
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (routed != Key.Space) return false;
        CloseFate();
        return true;
    }

    // Native observation (self-test and smoke).
    internal bool FateOpenForSelfTest => FateOpen;
    internal AreaFateView? FateViewForSelfTest => _fateView;
    internal IReadOnlyList<(int Kind, bool Arm, GameSessionDisposition Disposition, GameSessionFailure Failure)> FateLogForSelfTest => _fateLog;
    internal bool FateArtReadyForSelfTest => AdoptFateContent() is { Ready: true };
    internal void ToggleFateForSelfTest() => ToggleFate("self-test");
}
