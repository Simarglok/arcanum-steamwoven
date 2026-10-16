using Arcanum.Data.Hd;
using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>The visual profile setting, its persistence, the active HD pack and the on-the-fly
/// toggle (ARC-RENDER-014 path, ARC-UI-006 setting and toggle; contract
/// docs/rendering/hd-presentation-contract.md, toggle protocol).
/// <list type="bullet">
/// <item>Startup applies the saved profile (<c>user://visual_profile_v1.json</c>) before the first
/// submission. A diagnostic run may override it for that run with <c>ARC_VISUAL_PROFILE=hd|legacy</c>
/// or <c>ARC_HD_PACK=DIR</c> (which implies hd); an override is not saved.</item>
/// <item>The active pack is <c>ARC_HD_PACK</c> when set, otherwise the first usable pack of the
/// collection <c>user://hd-packs/</c> and, in a source run, the checkout's ignored
/// <c>local/hd-packs/</c> (<see cref="HdActivePack"/>). Without one, profile Hd draws every frame
/// Legacy as <c>NoPack</c> and says so in the HUD.</item>
/// <item>H (no modifiers, WORLD and crash-site-area, outside palette, dialog and UI focus) and the
/// palette action request the other profile; the request is saved at once and committed by the
/// canvas (<see cref="TerrainCanvas.RequestVisualProfile"/>).</item>
/// <item>Automated runs (self-test, native smoke) neither read nor write the owner's file or packs:
/// they start Legacy unless an override or <c>VISUAL_PROFILE_TEST_PATH</c> says otherwise.</item>
/// </list>
/// <c>ARC_HD_BUDGET_PATH=FILE</c> selects the device budget file (default
/// <c>user://hd_budget_v1.json</c>; missing or invalid means the contract ceilings).</summary>
public partial class TerrainViewerApp
{
    private HdFrameSource? _hdSource;
    private VisualProfileSettingsStore? _visualStore;
    // What decided the startup profile, the active pack line and the last request's save outcome.
    private string _visualOrigin = "default";
    private string _visualPack = "none";
    private string? _visualNotice;

    private bool AutomatedVisualRun => _automatedHost || _nativeSmoke || SelfTestBootstrap;

    private void ConfigureVisualProfile()
    {
        _visualStore = new VisualProfileSettingsStore(AutomatedVisualRun);
        var packPath = System.Environment.GetEnvironmentVariable("ARC_HD_PACK");
        var requested = System.Environment.GetEnvironmentVariable("ARC_VISUAL_PROFILE")?.Trim().ToLowerInvariant();
        VisualProfile profile;
        if (!string.IsNullOrEmpty(requested) || !string.IsNullOrWhiteSpace(packPath))
        {
            profile = requested switch
            {
                "legacy" => VisualProfile.Legacy,
                "hd" or null or "" => VisualProfile.Hd,
                _ => throw new ArgumentException("Usage"),
            };
            _visualOrigin = "environment";
        }
        else
        {
            profile = _visualStore.Load();
            _visualOrigin = _visualStore.Persistent ? "saved setting" : "default (automated run)";
        }
        HdPack? pack = null; var open = "none"; var found = 0;
        if (!string.IsNullOrWhiteSpace(packPath))
        {
            var result = HdPack.Open(packPath);
            open = result.Status.ToString(); pack = result.Pack; found = 1;
            _visualPack = "ARC_HD_PACK";
        }
        else if (!AutomatedVisualRun)
        {
            var listed = HdPack.Discover(PackLocations());
            found = listed.Count;
            if (HdActivePack.Choose(listed) is { } chosen) { pack = chosen.Result.Pack; open = chosen.Result.Status.ToString(); }
            _visualPack = listed.Count == 0 ? "no pack in the pack folders" : pack is null ? $"{listed.Count} found, none opened" : $"1 of {listed.Count} found";
        }
        var budgetPath = System.Environment.GetEnvironmentVariable("ARC_HD_BUDGET_PATH") is { Length: > 0 } custom ? custom
            : ProjectSettings.GlobalizePath("user://hd_budget_v1.json");
        string? budgetText = null;
        try { if (System.IO.File.Exists(budgetPath)) budgetText = System.IO.File.ReadAllText(budgetPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        var scale = pack?.Scale ?? 4;
        var budget = HdDeviceBudgetCodec.DeserializeOrCeiling(budgetText, scale);
        _hdSource?.Dispose();
        _hdSource = new HdFrameSource(pack);
        _canvas!.SetVisualProfile(profile, _hdSource, budget);
        // No local path is printed: only the outcome, the pack id and its scale.
        GD.Print($"ARC-RENDER-014 visual profile={profile} pack={(pack is null ? "none" : $"{pack.Id} {pack.Scale}x {pack.Review} sprites={pack.SpriteCount}")} open={open} budget area-visible={budget.AreaVisibleBytes} world={budget.WorldBytes}");
        GD.Print($"ARC-UI-006 visual startup profile={profile} from={_visualOrigin} pack-source={_visualPack} found={found}");
    }

    // The pack folders, in order: the game's own, then (source runs only) the checkout's ignored local/hd-packs.
    private static IReadOnlyList<HdPackLocation> PackLocations()
    {
        var locations = new List<HdPackLocation> { new(HdPackLocationKind.Collection, ProjectSettings.GlobalizePath("user://hd-packs")) };
        if (OS.HasFeature("editor"))
            locations.Add(new(HdPackLocationKind.Collection, System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "..", "local", "hd-packs"))));
        return locations;
    }

    /// <summary>Requests the other visual profile (H or the palette action), saves the request and
    /// reports it. The canvas commits it once the visible frames are ready, or after the bounded wait.</summary>
    private VisualRequestOutcome RequestVisualToggle(string via)
    {
        if (_canvas is null) return VisualRequestOutcome.NoOp;
        var target = _canvas.ToggledVisualProfile;
        var outcome = _canvas.RequestVisualProfile(target);
        // Persisted at request time; a failure is reported and never blocks the switch.
        if (outcome != VisualRequestOutcome.NoOp && _visualStore is { } store)
            _visualNotice = !store.Persistent ? "not saved (automated run)" : store.Save(target) ? null : "save FAILED, this run only";
        GD.Print($"ARC-UI-006 visual request via={via} target={target} outcome={outcome} generation={_canvas.VisualGeneration} active={_canvas.VisualProfile} saved={_visualNotice ?? "yes"}");
        BindVisualPalette();
        RefreshReadyStatus();
        return outcome;
    }

    /// <summary>H toggles the visual profile on a fresh press with no modifier (Cmd+H belongs to the
    /// system). Returns true when the key belongs to the toggle.</summary>
    private bool RouteVisualKey(WorldKeyDecision decision, WorldKeyModifiers modifiers)
    {
        if (decision.Key != WorldKey.H || modifiers != WorldKeyModifiers.None) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _visualNotice = "H ignored: palette, dialog or UI focus owns input"; return true; }
        RequestVisualToggle("key");
        return true;
    }

    private void PaletteVisualToggle() => RequestVisualToggle("palette");

    private void BindVisualPalette()
    {
        if (_worldPanel is null || !GodotObject.IsInstanceValid(_worldPanel) || _canvas is null) return;
        _worldPanel.SetVisualStatus($"Visual: {VisualSummary()}.");
    }

    // "Legacy, switching to HD; pack crash-site 4x; saved" — the palette line and the HUD suffix.
    private string VisualSummary()
    {
        if (_canvas is null) return "Legacy";
        var active = _canvas.VisualProfile == VisualProfile.Hd ? "HD" : "Legacy";
        var pending = _canvas.PendingVisualProfile is { } p ? $", switching to {(p == VisualProfile.Hd ? "HD" : "Legacy")}" : "";
        var pack = _canvas.HdSource?.Pack is { } loaded ? $"pack {loaded.Id} {loaded.Scale}x ({_visualPack})" : $"no HD pack ({_visualPack})";
        return $"{active}{pending}; {pack}; startup from {_visualOrigin}" + (_visualNotice is null ? "" : "; " + _visualNotice);
    }

    private string VisualHudStatus() => $"H visual Legacy/HD: {VisualSummary()}";
}

/// <summary>The visual profile preference (ARC-UI-006), following
/// <see cref="DiagnosticZoomSettingsStore"/>: <c>user://visual_profile_v1.json</c> holding
/// <see cref="VisualProfileCodec"/> text; a missing, unreadable or unknown value loads as Legacy.
/// <c>VISUAL_PROFILE_TEST_PATH</c> isolates tests; an automated run without it has no file at all
/// (Legacy, nothing saved), so it never reads or changes the owner's setting.</summary>
public sealed class VisualProfileSettingsStore
{
    public const string TestVariable = "VISUAL_PROFILE_TEST_PATH";
    public const string UserPath = "user://visual_profile_v1.json";
    private readonly string? _path;

    public VisualProfileSettingsStore(bool automated)
    {
        var test = OS.GetEnvironment(TestVariable);
        _path = !string.IsNullOrWhiteSpace(test) ? test : automated ? null : UserPath;
    }

    /// <summary>False for an automated run without a test path: nothing is read or written.</summary>
    public bool Persistent => _path is not null;

    public VisualProfile Load()
    {
        if (_path is null || !global::Godot.FileAccess.FileExists(_path)) return VisualProfile.Legacy;
        using var file = global::Godot.FileAccess.Open(_path, global::Godot.FileAccess.ModeFlags.Read);
        return file is null ? VisualProfile.Legacy : VisualProfileCodec.DeserializeOrLegacy(file.GetAsText());
    }

    /// <summary>Writes the profile; false when it could not be saved (the caller reports it).</summary>
    public bool Save(VisualProfile profile)
    {
        if (_path is null) return false;
        using var file = global::Godot.FileAccess.Open(_path, global::Godot.FileAccess.ModeFlags.Write);
        if (file is null) return false;
        file.StoreString(VisualProfileCodec.Serialize(profile));
        file.Flush();
        return file.GetError() == Error.Ok;
    }
}
