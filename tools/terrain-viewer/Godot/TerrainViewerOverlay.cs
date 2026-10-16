using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-RENDER-008: session-only state, OFF at launch, kept across Reset scene/Reload.
    private bool _passabilityOverlayOn;
    private PassabilityOverlayLayer? _overlayLayer;
    private IGridSession? _overlaySession;
    private IPassabilityAuthority? _overlayAuthority;
    private (bool Available, bool On)? _overlayPanelState;
    private OverlayCounts _overlayCounts;

    internal bool PassabilityOverlayOnForSelfTest => _passabilityOverlayOn;
    internal PassabilityOverlayLayer? PassabilityOverlayLayerForSelfTest => _overlayLayer;
    internal IGridSession? WorldSessionForSelfTest => _worldActor.Session;

    private void SetPassabilityOverlay(bool on)
    {
        // Display only: no quarantine, camera, Follow, zoom, input or movement change.
        _passabilityOverlayOn = on;
        SyncPassabilityOverlay();
        RefreshReadyStatus();
    }

    /// <summary>Binds the overlay to the current movement authority and advances one bounded batch.
    /// The authority is the session's own production assessor; a new snapshot/epoch/context gets a new cache.</summary>
    private void SyncPassabilityOverlay()
    {
        if (_exiting || !_worldInitialized || _canvas is null) return;
        if (_overlayLayer is null || !GodotObject.IsInstanceValid(_overlayLayer))
        {
            _overlayLayer = new PassabilityOverlayLayer(); _canvas.AddChild(_overlayLayer);
        }
        var session = _readyActorPlan is null ? null : _worldActor.Session;
        if (!ReferenceEquals(session, _overlaySession))
        {
            _overlaySession = session;
            _overlayAuthority = session switch
            {
                AreaGridSession area => new AreaPassabilityAuthority(area),
                DiagnosticGridSession bounded => new CardinalPassabilityAuthority(bounded),
                _ => null,
            };
        }
        var state = (_overlayAuthority is not null, _passabilityOverlayOn);
        if (_overlayPanelState != state && _worldPanel is { } panel && GodotObject.IsInstanceValid(panel) && panel.IsInsideTree())
        {
            _overlayPanelState = state;
            panel.BindOverlay(state.Item1, state.Item2, state.Item1 ? "Show passability (read-only)" : "Show passability (unavailable: no movement authority)");
        }
        if (!_passabilityOverlayOn || _overlayAuthority is null)
        {
            if (_overlayLayer.Overlay is not null) _overlayLayer.Bind(null);
            return;
        }
        // A new session object (reset, reload, republish) or identity always binds a fresh overlay.
        if (_overlayLayer.Overlay is not { } current || !ReferenceEquals(current.Authority, _overlayAuthority))
            _overlayLayer.Bind(new PassabilityOverlay(_overlayAuthority,
                originX: _overlayAuthority is AreaPassabilityAuthority ? (int)AreaGridSession.LocationOriginX : 0));
        if (_overlayLayer.Tick(GetViewport().GetVisibleRect())) _overlayCounts = _overlayLayer.Overlay!.Count(_overlayLayer.VisibleCells);
    }

    private void RetirePassabilityOverlay()
    {
        _overlaySession = null; _overlayAuthority = null; _overlayCounts = default; _worldLastStepAttempt = null;
        if (_overlayLayer is not null && GodotObject.IsInstanceValid(_overlayLayer)) _overlayLayer.Bind(null);
    }

    private string PassabilityOverlayStatus()
    {
        if (!_passabilityOverlayOn) return "Passability overlay OFF (palette)";
        if (_overlayAuthority is null) return "Passability overlay ON: OVERLAY_UNAVAILABLE (no ready movement authority)";
        var overlay = _overlayLayer?.Overlay;
        if (overlay?.Failure is { } failure) return $"Passability overlay ON: {failure}; nothing drawn";
        var c = _overlayCounts;
        return $"Passability overlay ON (read-only display of the movement assessor, not a permit) profile {_overlayAuthority.ProfileId} actor {_overlayAuthority.ActorId}; coverage {_overlayAuthority.CoverageDescription}"
            + (_overlayLayer!.Capped ? $"; OVERLAY_ZOOM_IN: more than {PassabilityOverlayLayer.VisibleCellCap} visible cells, nothing drawn"
                : $"; visible Blocked {c.Blocked} Open {c.Open} Unknown {c.Unknown} outside {c.Outside}; edges blocked {c.BlockedEdges} unknown {c.UnknownEdges}; cached {overlay?.CachedCells} cells / {overlay?.AssessCalls} assessments")
            + $"; last step attempt {_worldLastStepAttempt ?? "none"}"
            + " | legend: red fill = every admitted incoming step Blocked; red edge/arrow = that directed step Blocked; gray hatch + outline = Unknown; gray hatch = outside coverage; no fill = Open";
    }
}
