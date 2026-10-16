using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private double _pacingLastActivity = double.NegativeInfinity;
    private CameraState? _pacingCamera;
    private double _statusAge = double.PositiveInfinity;
    // Engine cap learned per target rate, corrected once per second from the measured
    // rate after the target has held for PacingSettleSeconds.
    private readonly Dictionary<int, int> _pacingCaps = new();
    private int _pacingTarget;
    private double _pacingTargetSince, _pacingMeasuredAt;
    private const double PacingSettleSeconds = 1.5;
    // Automated self-tests and smokes keep uncapped pacing and per-frame status text.
    private bool PacingAutomated => SelfTestBootstrap || _nativeSmoke;
    private static double PacingNow() => Time.GetTicksUsec() / 1e6;
    private void NotePacingActivity() => _pacingLastActivity = PacingNow();

    /// <summary>Caps the frame rate at the active display's refresh rate while the user
    /// acts (input held or recent, camera moving) and at 60 FPS when idle; the engine
    /// cap is corrected from the measured rate (<see cref="FramePacingPolicy.CorrectCap"/>).</summary>
    private void PaceFrames()
    {
        if (PacingAutomated) { if (Engine.MaxFps != 0) Engine.MaxFps = 0; return; }
        var now = PacingNow(); var camera = _camera?.State;
        if (camera != _pacingCamera || Godot.Input.IsAnythingPressed()) { _pacingCamera = camera; _pacingLastActivity = now; }
        var target = FramePacingPolicy.Target(now - _pacingLastActivity, DisplayServer.ScreenGetRefreshRate(DisplayServer.WindowGetCurrentScreen()));
        if (target != _pacingTarget) { _pacingTarget = target; _pacingTargetSince = now; _pacingMeasuredAt = now; }
        else if (now - _pacingMeasuredAt >= 1 && now - _pacingTargetSince >= PacingSettleSeconds)
        {
            _pacingMeasuredAt = now;
            var measured = Engine.GetFramesPerSecond();
            _pacingCaps[target] = FramePacingPolicy.CorrectCap(target, _pacingCaps.GetValueOrDefault(target, target), measured);
        }
        var cap = _pacingCaps.GetValueOrDefault(target, target);
        if (Engine.MaxFps != cap) Engine.MaxFps = cap;
    }

    /// <summary>The per-frame status refresh of the WORLD loop, rebuilt at most every
    /// <see cref="FramePacingPolicy.StatusRefreshSeconds"/> outside automated tests.
    /// Event-driven refreshes call <c>RefreshReadyStatus</c> directly.</summary>
    private void RefreshReadyStatusPerFrame(double delta)
    {
        _statusAge += double.IsFinite(delta) && delta > 0 ? delta : 0;
        if (!PacingAutomated && _statusAge < FramePacingPolicy.StatusRefreshSeconds) return;
        _statusAge = 0; RefreshReadyStatus();
    }
}
