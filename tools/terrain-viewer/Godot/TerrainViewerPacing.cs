using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private double _pacingLastActivity = double.NegativeInfinity;
    private CameraState? _pacingCamera;
    private (GamepadLab.Core.Vec2 Anchor, int Facing)? _pacingActor;
    private double _statusAge = double.PositiveInfinity;
    // Engine cap in force, corrected once per second from the measured rate after the
    // target has held for PacingSettleSeconds (the engine's 1 s FPS window then holds
    // no frames of the previous target). Caps that met their target are remembered
    // per target and start the next episode of it.
    private readonly Dictionary<int, int> _pacingOnTargetCaps = new();
    private int _pacingTarget, _pacingCap;
    private double _pacingTargetSince, _pacingMeasuredAt;
    private const double PacingSettleSeconds = 2;
    // Automated self-tests (their bootstrap apps and the process hosting them) and smokes keep
    // uncapped pacing and per-frame status text.
    private bool _automatedHost;
    private bool PacingAutomated => SelfTestBootstrap || _automatedHost || _nativeSmoke;
    private static double PacingNow() => Time.GetTicksUsec() / 1e6;
    private void NotePacingActivity() => _pacingLastActivity = PacingNow();

    /// <summary>Caps the frame rate at the active display's refresh rate while the user
    /// acts (input held or recent, camera moving) and at 60 FPS when idle; the engine
    /// cap is corrected from the measured rate (<see cref="FramePacingPolicy.CorrectCap"/>).</summary>
    private void PaceFrames()
    {
        if (PacingAutomated) { if (Engine.MaxFps != 0) Engine.MaxFps = 0; return; }
        var now = PacingNow(); var camera = _camera?.State;
        // A walking or turning actor (a click route with a still camera) is activity as well:
        // it raises the frame cap to the display rate and the status refresh to 10 Hz.
        (GamepadLab.Core.Vec2 Anchor, int Facing)? actor = IsWorld ? (_worldActor.FootAnchor, _worldActor.State.Facing) : null;
        if (camera != _pacingCamera || actor != _pacingActor || Godot.Input.IsAnythingPressed())
        { _pacingCamera = camera; _pacingActor = actor; _pacingLastActivity = now; }
        var target = FramePacingPolicy.Target(now - _pacingLastActivity, DisplayServer.ScreenGetRefreshRate(DisplayServer.WindowGetCurrentScreen()));
        if (target != _pacingTarget)
        {
            _pacingTarget = target; _pacingCap = _pacingOnTargetCaps.GetValueOrDefault(target, target);
            _pacingTargetSince = now; _pacingMeasuredAt = now;
        }
        else if (now - _pacingMeasuredAt >= 1 && now - _pacingTargetSince >= PacingSettleSeconds)
        {
            _pacingMeasuredAt = now;
            var measured = Engine.GetFramesPerSecond();
            if (FramePacingPolicy.OnTarget(target, measured)) _pacingOnTargetCaps[target] = _pacingCap;
            _pacingCap = FramePacingPolicy.CorrectCap(target, _pacingCap, measured);
        }
        if (Engine.MaxFps != _pacingCap) Engine.MaxFps = _pacingCap;
    }

    /// <summary>The per-frame status refresh of the WORLD loop, rebuilt at most every
    /// <see cref="FramePacingPolicy.StatusRefreshSeconds"/> (idle:
    /// <see cref="FramePacingPolicy.IdleStatusRefreshSeconds"/>) outside automated tests.
    /// Event-driven refreshes call <c>RefreshReadyStatus</c> directly.</summary>
    private void RefreshReadyStatusPerFrame(double delta)
    {
        // An invalid frame delta (the quarantine path) refreshes at once.
        _statusAge = double.IsFinite(delta) && delta >= 0 ? _statusAge + delta : double.PositiveInfinity;
        var interval = FramePacingPolicy.Idle(PacingNow() - _pacingLastActivity) ? FramePacingPolicy.IdleStatusRefreshSeconds : FramePacingPolicy.StatusRefreshSeconds;
        if (!PacingAutomated && _statusAge < interval) return;
        _statusAge = 0; RefreshReadyStatus();
        // ARC-WORLD-046: the follower portraits follow modal windows and party changes (cheap when nothing changed).
        if (_readyArea is not null) RefreshFollowerPortraits();
        // ARC-UI-020: the PC's gauges also hide once no area is shown.
        else RefreshHeroGauges();
    }
}
