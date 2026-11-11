using Arcanum.Data.Hd;
using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>The visual profile setting, its persistence, the active HD pack and the on-the-fly
/// toggle (ARC-RENDER-014 path, ARC-UI-006 setting and toggle; contract
/// docs/rendering/hd-presentation-contract.md, toggle protocol).
/// <list type="bullet">
/// <item>Startup applies the saved profile (<c>user://visual_profile_v1.json</c>) before the first
/// submission. A diagnostic run may override it for that run with <c>ARC_VISUAL_PROFILE=hd|legacy</c>
/// or <c>ARC_HD_PACK=DIR</c> (which implies hd); an override is not saved.</item>
/// <item>The active pack is <c>ARC_HD_PACK</c> when set, otherwise the one the saved HD pack source chooses (ARC-DEV-350,
/// <see cref="HdPackSourceResolver"/>: Auto prefers the player's iCloud pack folder when it is on this device and whole, then the installed
/// copy in <c>user://hd-packs/</c>, then in a source run the checkout's ignored <c>local/hd-packs/</c>; Game is the installed copy alone;
/// Custom is the folder the player chose). Without one, profile Hd draws every frame
/// Legacy as <c>NoPack</c> and says so in the HUD.</item>
/// <item>H (no modifiers, WORLD and crash-site-area, outside palette, dialog and UI focus) and the
/// palette action request the other profile; the request is saved at once and committed by the
/// canvas (<see cref="TerrainCanvas.RequestVisualProfile"/>).</item>
/// <item>Automated runs (self-test, native smoke) neither read nor write the owner's file or packs:
/// they start Legacy unless an override or <c>VISUAL_PROFILE_TEST_PATH</c> says otherwise.</item>
/// </list>
/// <c>ARC_HD_BUDGET_PATH=FILE</c> selects the device budget file (default <c>user://hd_budget_v1.json</c>, which an automated
/// run does not read, ARC-DEV-032; missing or invalid means the contract ceilings).</summary>
public partial class TerrainViewerApp
{
    private HdFrameSource? _hdSource;
    private VisualProfileSettingsStore? _visualStore;
    // What decided the startup profile, the active pack line and the last request's save outcome.
    private string _visualOrigin = "default";
    private string _visualPack = "not chosen at startup";
    // ARC-DEV-350: what the pack source chose at startup (null: an environment pack or an automated run).
    private HdPackSourceChoice? _hdPackChoice;
    // ARC-DEV-350: the startup problem the box tells the player once the scene is up.
    private string? _hdPackProblem;
    private HdPackProblemPanel? _hdPackProblemPanel;
    private Action? _hdPackProblemQuitForSelfTest;
    // The last request's save outcome, and an ignored H (cleared by the next request or palette close).
    private string? _visualNotice;
    private string? _visualKeyNotice;

    private bool AutomatedVisualRun => _automatedHost || _nativeSmoke || SelfTestBootstrap || ViewerAutomatedStorage.Automated;

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
            _visualOrigin = !_visualStore.Persistent ? "default (automated run)" : _visualStore.HasSaved ? "saved setting" : "default (nothing saved yet)";
        }
        HdPack? pack = null; var open = "none"; var found = 0;
        var fromEnvironment = !string.IsNullOrWhiteSpace(packPath);
        if (fromEnvironment)
        {
            var result = HdPack.Open(packPath!);
            open = result.Status.ToString(); pack = result.Pack; found = 1;
            _visualPack = "ARC_HD_PACK";
        }
        else if (!AutomatedVisualRun)
        {
            var choice = ResolveHdPack(_gameSettings.HdPackSource, _gameSettings.HdPackPath);
            _hdPackChoice = choice;
            found = choice.Found;
            if (choice.Chosen is { } chosen) { pack = chosen.Result.Pack; open = chosen.Result.Status.ToString(); }
            _visualPack = choice.Describe();
            // No local path is printed: the setting's name, the source's label and the file counts.
            GD.Print($"ARC-DEV-350 hd pack source mode={HdPackSourceResolver.ModeName(_gameSettings.HdPackSource)} chosen={choice.Source} found={choice.Found} "
                + $"files={choice.Availability?.Files ?? 0} local={choice.Availability?.Local ?? 0} online-only={choice.Availability?.OnlineOnly ?? 0} missing={choice.Availability?.Missing ?? 0} wrong-size={choice.Availability?.WrongSize ?? 0} "
                + $"skipped=[{string.Join(" | ", choice.Skipped)}]");
        }
        // ARC-DEV-350: a player who asked for HD and gets no usable pack is told (log and a box); the run draws Legacy and the settings stay.
        // An automated run (self-test, smoke) keeps its own pack arrangements: the HD toggle tests rely on a profile with no pack.
        // ARC-UI-140: the generated interface needs no pack (it takes the Legacy pictures without one), so it alone asks nothing.
        var problem = AutomatedVisualRun ? HdPackProblem.None
            : HdPackProblem.Check(profile == VisualProfile.Hd || (_gameSettings.HdInterface && !_gameSettings.GeneratedInterface), fromEnvironment, pack, open, _hdPackChoice);
        if (problem.Message is not null)
        {
            GD.Print("ARC-DEV-350 hd pack problem: " + problem.Message);
            if (problem.DropPack) { pack = null; profile = VisualProfile.Legacy; _visualPack += "; not used"; }
            _hdPackProblem = problem.Message;
        }
        // ARC-DEV-032: an automated run reads no player budget (the contract ceilings unless ARC_HD_BUDGET_PATH names one).
        var budgetPath = System.Environment.GetEnvironmentVariable("ARC_HD_BUDGET_PATH") is { Length: > 0 } custom ? custom
            : AutomatedVisualRun ? null : ProjectSettings.GlobalizePath("user://hd_budget_v1.json");
        string? budgetText = null;
        try { if (budgetPath is not null && System.IO.File.Exists(budgetPath)) budgetText = System.IO.File.ReadAllText(budgetPath); }
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

    /// <summary>ARC-DEV-350: opens the box with the startup HD pack problem (once, above every window): Continue goes on with the Legacy graphics,
    /// Close game ends the program. A test run may take over the quit.</summary>
    private void ShowHdPackProblem(string message)
    {
        if (_hdPackProblemPanel is null)
        {
            _hdPackProblemPanel = new HdPackProblemPanel(); AddChild(_hdPackProblemPanel);
            _hdPackProblemPanel.ContinueRequested += () => { _hdPackProblemPanel.CloseView(); GD.Print("ARC-DEV-350 hd pack problem box: continue with Legacy graphics"); };
            _hdPackProblemPanel.CloseGameRequested += () =>
            {
                GD.Print("ARC-DEV-350 hd pack problem box: close game");
                if (_hdPackProblemQuitForSelfTest is { } quit) { _hdPackProblemPanel.CloseView(); quit(); return; }
                GetTree().Quit(0);
            };
        }
        _hdPackProblemPanel.Open(message);
    }

    /// <summary>ARC-DEV-350: the startup problem, shown once the viewer is in its scene tree.</summary>
    private void ShowStartupHdPackProblem()
    {
        if (_hdPackProblem is not { } message) return;
        _hdPackProblem = null;
        ShowHdPackProblem(message);
    }

    /// <summary>ARC-DEV-350: the pack folders of this device: the game's own, the iCloud copy where there is one, and (source runs only) the
    /// checkout's ignored <c>local/hd-packs</c>.</summary>
    private static HdPackSourceFolders PackFolders()
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var editor = OS.HasFeature("editor")
            ? System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "..", "local", "hd-packs")) : null;
        return new(ProjectSettings.GlobalizePath("user://hd-packs"),
            HdPackSourceFolders.DefaultIcloudFolder(home, OperatingSystem.IsMacOS(), System.IO.Directory.Exists), editor);
    }

    /// <summary>ARC-DEV-350: what the pack source would choose now (a stat of every pack file, no frame read), for the startup and for the
    /// options window's note.</summary>
    private static HdPackSourceChoice ResolveHdPack(HdPackSourceMode mode, string? customPath) =>
        HdPackSourceResolver.Resolve(mode, customPath, PackFolders(), new HdFileProbe());

    /// <summary>Requests the other visual profile (H or the palette action), saves the request and
    /// reports it. The canvas commits it once the visible frames are ready, or after the bounded wait.</summary>
    private VisualRequestOutcome RequestVisualToggle(string via) => _canvas is null ? VisualRequestOutcome.NoOp : RequestVisualProfile(_canvas.ToggledVisualProfile, via);

    /// <summary>ARC-UI-089: requests <paramref name="target"/> (the options window's HD graphics line asks for a profile, not a toggle).</summary>
    private VisualRequestOutcome RequestVisualProfile(VisualProfile target, string via)
    {
        if (_canvas is null) return VisualRequestOutcome.NoOp;
        var outcome = _canvas.RequestVisualProfile(target);
        // Persisted at request time; a failure is reported and never blocks the switch.
        _visualKeyNotice = null;
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
        if (!_worldFocused || WorldModal || WorldUiFocus)
        {
            _visualKeyNotice = "H ignored: palette or UI focus owns input";
            BindVisualPalette(); RefreshReadyStatus();
            return true;
        }
        RequestVisualToggle("key");
        return true;
    }

    private void PaletteVisualToggle() => RequestVisualToggle("palette");

    /// <summary>ARC-UI-089: per frame, the interface art follows the active pack and the HD interface setting (ARC-UI-095: its own saved
    /// switch, not the visual profile).</summary>
    private void SyncInterfaceHd()
    {
        // Windows that draw interface textures by hand repaint once when the switch or the pack changed.
        HdOptimizedStore.ConversionAllowed = _visualStore?.Persistent == true; // ARC-DEV-376: an automated run reads the cache, never writes it
        if (InterfaceHd.Configure(_canvas?.HdSource?.Pack, _gameSettings.HdInterface, _gameSettings.GeneratedInterface)) InterfaceHd.RedrawAll(GetTree().Root);
    }

    /// <summary>ARC-UI-089: whether the options window's HD graphics line shows On (the requested profile, so a pending switch shows at once).</summary>
    private bool HdGraphicsOption => (_canvas?.PendingVisualProfile ?? _canvas?.VisualProfile) == VisualProfile.Hd;

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
        return $"{active}{pending}; {pack}; startup from {_visualOrigin}" + (_visualNotice is null ? "" : "; " + _visualNotice)
            + (_visualKeyNotice is null ? "" : "; " + _visualKeyNotice);
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
    /// <summary>A saved file exists (startup reports "saved setting" only then).</summary>
    public bool HasSaved => _path is not null && global::Godot.FileAccess.FileExists(_path);

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
