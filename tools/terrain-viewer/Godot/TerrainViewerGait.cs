using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-WORLD-020: walking and running in the AREA view (research ARC-RES-047 L1–L4). The Always Run option
/// (palette; saved like the original's settings value) and the Num Lock toggle decide the default gait; Ctrl on a
/// ground click runs once, or walks once with always-run; a click on an object always runs. The gait is fixed when
/// a route or approach starts and read by the PC's session when it admits each step (<see cref="GaitTiming"/>).
/// A hero out of fatigue walks (ARC-COMBAT-004), and so does a hero at encumbrance level 4 or more (ARC-WORLD-087).
/// </summary>
public partial class TerrainViewerApp
{
    private AlwaysRunSettingsStore? _alwaysRunStore;
    private bool _alwaysRunOption;
    // The original reads Num Lock as a lock state flipped by each press; the viewer starts it off.
    private bool _numLockRun;
    private MovementGait _worldGait = MovementGait.Walk;
    private string _gaitOrigin = "no move yet";

    private void ConfigureGait()
    {
        _alwaysRunStore = new AlwaysRunSettingsStore(AutomatedVisualRun);
        _alwaysRunOption = _alwaysRunStore.Load();
        _worldActor.AreaGaitTiming = GaitTiming.HumanFixed;
        // ARC-COMBAT-004: a hero out of fatigue walks whatever the chosen gait.
        _worldActor.AreaGait = StepGait;
    }

    /// <summary>Gait controls act only in the AREA game view.</summary>
    private bool GaitControlsActive => _readyMode == TerrainViewerMode.CrashSiteArea && _worldActor.Session is AreaGridSession;

    /// <summary>Research L2: option on runs unless Ctrl is held; option off runs while Ctrl is held or Num Lock is on.</summary>
    private bool EffectiveAlwaysRun(bool ctrl) => _alwaysRunOption ? !ctrl : ctrl || _numLockRun;

    /// <summary>A ground click (L3): its route runs when <see cref="EffectiveAlwaysRun"/> says so.</summary>
    private void SetGroundClickGait(bool ctrl) => SetWorldGait(EffectiveAlwaysRun(ctrl) ? MovementGait.Run : MovementGait.Walk, ctrl ? "ground click with Ctrl" : "ground click");

    /// <summary>A click on an object (L4): the approach always runs.</summary>
    private void SetObjectApproachGait() => SetWorldGait(MovementGait.Run, "object approach");

    /// <summary>The stick (a project input): the default gait, without Ctrl.</summary>
    private void SetStickGait() => SetWorldGait(EffectiveAlwaysRun(false) ? MovementGait.Run : MovementGait.Walk, "stick", newMove: !_stickHeld);

    // ARC-COMBAT-004: the stick is set every frame it is held; only its press starts a new move (the run counter's goal).
    private bool _stickHeld;

    private void SetWorldGait(MovementGait gait, string origin, bool newMove = true)
    {
        if (_worldGait != gait || _gaitOrigin != origin) GD.Print($"ARC-WORLD-020 gait={gait} via={origin}");
        _worldGait = gait; _gaitOrigin = origin;
        if (newMove) ResetCombatRunSteps();
    }

    /// <summary>Num Lock flips the always-run lock state on a fresh press (its original binding).</summary>
    private bool RouteNumLockKey(WorldKeyDecision decision)
    {
        if (decision.Key != WorldKey.NumLock || !GaitControlsActive) return false;
        if (!decision.Pressed || decision.Echo || !_worldFocused || WorldModal || WorldUiFocus) return true;
        _numLockRun = !_numLockRun;
        GD.Print($"ARC-WORLD-020 num-lock run={_numLockRun}");
        BindGaitPalette(); RefreshReadyStatus();
        return true;
    }

    private void PaletteAlwaysRunToggle()
    {
        if (!GaitControlsActive) { BindGaitPalette(); return; }
        SetAlwaysRun(!_alwaysRunOption);
    }

    /// <summary>The Always Run option set (the palette's toggle, or the options window, ARC-UI-013) and saved.</summary>
    private void SetAlwaysRun(bool value)
    {
        _alwaysRunOption = value;
        var saved = _alwaysRunStore is not { Persistent: true } store ? "not saved (automated run)" : store.Save(_alwaysRunOption) ? "saved" : "save FAILED, this run only";
        GD.Print($"ARC-WORLD-020 always-run option={_alwaysRunOption} {saved}");
        BindGaitPalette(); RefreshReadyStatus();
    }

    private void BindGaitPalette()
    {
        if (_worldPanel is null || !GodotObject.IsInstanceValid(_worldPanel)) return;
        _worldPanel.SetGaitStatus(GaitControlsActive ? $"Movement: {GaitSummary()}." : "Movement: Always Run unavailable (crash-site-area only).");
    }

    private string GaitSummary() => $"Always Run {(_alwaysRunOption ? "on" : "off")}, Num Lock run {(_numLockRun ? "on" : "off")}; "
        + $"next ground click {(EffectiveAlwaysRun(false) ? "runs" : "walks")} (Ctrl {(EffectiveAlwaysRun(true) ? "runs" : "walks")}), object clicks run; "
        + $"current gait {_worldGait} ({_gaitOrigin}){StealthSummary()}{(_prowlNotice is null ? "" : " | " + _prowlNotice)}";

    private string GaitHudStatus() => $"Gait: {GaitSummary()} | Hero body: {_heroBodyStatus}";
}

/// <summary>The Always Run option (ARC-WORLD-020), following <see cref="VisualProfileSettingsStore"/>:
/// <c>user://always_run_v1.json</c> holding <c>on</c> or <c>off</c>; a missing or unknown value is off.
/// <c>ALWAYS_RUN_TEST_PATH</c> isolates tests; an automated run without it has no file at all.</summary>
public sealed class AlwaysRunSettingsStore
{
    public const string TestVariable = "ALWAYS_RUN_TEST_PATH";
    public const string UserPath = "user://always_run_v1.json";
    private readonly string? _path;

    public AlwaysRunSettingsStore(bool automated)
    {
        var test = OS.GetEnvironment(TestVariable);
        _path = !string.IsNullOrWhiteSpace(test) ? test : automated ? null : UserPath;
    }

    public bool Persistent => _path is not null;

    public bool Load()
    {
        if (_path is null || !global::Godot.FileAccess.FileExists(_path)) return false;
        using var file = global::Godot.FileAccess.Open(_path, global::Godot.FileAccess.ModeFlags.Read);
        return file is not null && file.GetAsText().Trim() == "on";
    }

    public bool Save(bool value)
    {
        if (_path is null) return false;
        using var file = global::Godot.FileAccess.Open(_path, global::Godot.FileAccess.ModeFlags.Write);
        if (file is null) return false;
        file.StoreString(value ? "on" : "off");
        file.Flush();
        return file.GetError() == Error.Ok;
    }
}
