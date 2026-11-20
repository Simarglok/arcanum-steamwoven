using Arcanum.Data.Hd;
using Arcanum.Input;
using Arcanum.Presentation.Steamwoven;
using Arcanum.Session.Settings;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>The visual profile setting, its persistence, the active HD pack and the on-the-fly
/// toggle (ARC-RENDER-014 path, ARC-UI-006 setting and toggle; contract
/// docs/rendering/hd-presentation-contract.md, toggle protocol).
/// <list type="bullet">
/// <item>Startup applies the saved profile (the <c>visual profile</c> line of <c>user://arcanum.cfg</c>) before the first
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
        // ARC-DEV-2923: the sheet HD check (ARC_AREA_SHEET_HD_VERIFY=1 with ARC_HD_PACK) turns the interface HD for itself and the Crash Site smoke's
        // later pixel observers compare Legacy source pixels, so a smoke run with that check takes the Legacy world profile unless the run names one.
        if (string.IsNullOrEmpty(requested) && _nativeSmoke && System.Environment.GetEnvironmentVariable("ARC_AREA_SHEET_HD_VERIFY") == "1") requested = "legacy";
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
            // ARC-UI-2174: the one HD setting is the profile (the interface and the relit look follow it); the game settings hold it, like any start.
            profile = _gameSettings.HdInterface ? VisualProfile.Hd : VisualProfile.Legacy;
            // ARC-UI-2221: the profile comes from the game settings, so the origin is theirs too (their file, not the visual store's).
            _visualOrigin = _gameSettingsOrigin;
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
    /// launcher screen's note.</summary>
    private static HdPackSourceChoice ResolveHdPack(HdPackSourceMode mode, string? customPath) =>
        HdPackSourceResolver.Resolve(mode, customPath, PackFolders(), new HdFileProbe());

    /// <summary>Requests the other visual profile (H or the palette action), saves the request and
    /// reports it. The canvas commits it once the visible frames are ready, or after the bounded wait.
    /// ARC-UI-2221: the other value than the saved HD value (the one setting, written at every request, so a pending switch is covered); the
    /// canvas profile can differ from it (a pack-problem fallback, an ARC_VISUAL_PROFILE override), and toggling from that would write the saved value again.</summary>
    private VisualRequestOutcome RequestVisualToggle(string via) => _canvas is null ? VisualRequestOutcome.NoOp : RequestVisualProfile(VisualToggleTarget(_gameSettings.HdInterface), via);

    /// <summary>ARC-UI-2221: what a toggle asks for: the other value than the saved HD value.</summary>
    internal static VisualProfile VisualToggleTarget(bool savedHd) => savedHd ? VisualProfile.Legacy : VisualProfile.Hd;

    /// <summary>ARC-UI-089: requests <paramref name="target"/> (an HD request names its target; the toggle asks for the other one).</summary>
    private VisualRequestOutcome RequestVisualProfile(VisualProfile target, string via)
    {
        if (_canvas is null) return VisualRequestOutcome.NoOp;
        var outcome = _canvas.RequestVisualProfile(target);
        // Persisted at request time; a failure is reported and never blocks the switch.
        _visualKeyNotice = null;
        var on = target == VisualProfile.Hd;
        // ARC-UI-2221: a request the canvas finds no change in (a fallback to Legacy while HD is saved On) still changes the saved value when it differs.
        if (outcome != VisualRequestOutcome.NoOp || on != _gameSettings.HdInterface)
        {
            // ARC-UI-2174: the one HD value; the interface follows at once (the experimental generated interface is honoured only while HD is On, so Off clears it).
            SaveGameSettings(_gameSettings with { HdInterface = on, GeneratedInterface = _gameSettings.GeneratedInterface && on });
            if (_visualStore is { } store)
                _visualNotice = !store.Persistent ? "not saved (automated run)" : store.Save(target) ? null : "save FAILED, this run only";
        }
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
        SyncInterfaceLayout();
        SyncOriginalComposition();
    }

    /// <summary>ARC-UI-2936: whether the window draws the Original look as the centred 4:3 composition: the HD interface option is off, no launcher screen is open and the run is not an automated one (those read the plain
    /// window pixels) unless <see cref="_originalCompositionForced"/> (the composition smoke).</summary>
    internal bool OriginalCompositionWanted => _originalCompositionForced is { } forced ? forced
        : !InterfaceHd.Enabled && _launcher is null && !_nativeSmoke && !SelfTestBootstrap && GetViewport() == GetWindow();

    /// <summary>Null: the viewer decides (<see cref="OriginalCompositionWanted"/>); the composition smoke sets it.</summary>
    internal bool? _originalCompositionForced;

    /// <summary>The composition smoke's render target: the composition is laid out for this size instead of the real window's (a window cannot have the size of a 4K target on every screen); the picture on screen is the same virtual screen.</summary>
    internal Vector2I? _originalCompositionTarget;

    /// <summary>The virtual screen's whole factor now in force (0: none, the window draws at its own size).</summary>
    internal int OriginalCompositionFactor { get; private set; }

    /// <summary>ARC-UI-2936: per frame, the window's content scale follows the look. In the composition the window is a virtual screen of 800 n × 600 n pixels (n = ceil(s), viewport mode, aspect keep): every interface part keeps
    /// its whole-factor layout at n, the world is drawn at the composition's scale, Godot centres the picture, reduces it smoothly to s and leaves the rest black, and the pointer is mapped back by the window. The
    /// camera's zoom follows the same factor (<see cref="OriginalComposition.ZoomScale"/>).</summary>
    private void SyncOriginalComposition()
    {
        var window = GetWindow();
        var wanted = OriginalCompositionWanted;
        var size = _originalCompositionTarget ?? window.Size;
        var factor = wanted && size.X > 0 && size.Y > 0 ? OriginalComposition.WholeFactor(size.X, size.Y) : 0;
        var virtualSize = factor == 0 ? Vector2I.Zero : new Vector2I(OriginalComposition.ScreenWidth * factor, OriginalComposition.ScreenHeight * factor);
        var contentSet = window.ContentScaleMode == Window.ContentScaleModeEnum.Viewport && window.ContentScaleAspect == Window.ContentScaleAspectEnum.Keep && window.ContentScaleSize == virtualSize;
        var changed = factor != OriginalCompositionFactor;
        if (changed || (factor != 0 && !contentSet))
        {
            if (factor == 0)
            {
                window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
                window.ContentScaleSize = Vector2I.Zero;
                window.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
                window.ContentScaleFactor = 1;
            }
            else
            {
                window.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
                window.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
                window.ContentScaleFactor = 1;
                window.ContentScaleSize = virtualSize;
            }
            // The picture beside the virtual screen is the window's clear colour: black while the composition stands, the project's colour again after it.
            if (changed) RenderingServer.SetDefaultClearColor(factor == 0 ? (Color)ProjectSettings.GetSetting("rendering/environment/defaults/default_clear_color", new Color(0.3f, 0.3f, 0.3f)) : Colors.Black);
            OriginalCompositionFactor = factor;
            if (changed) { _gameBarPanel?.Relayout(); GD.Print($"ARC-UI-2936 original composition factor={factor} window={size.X}x{size.Y}"); }
        }
        // The camera after the window: the viewport the new content scale gives is not read until the next frame, so the camera is told it.
        var (zoomScale, viewShift) = factor == 0 ? (1.0, 0.0) : (OriginalComposition.ZoomScale(factor), OriginalComposition.ViewShift(factor));
        var playHeight = factor == 0 ? 0.0 : (double)OriginalComposition.ViewHeight * factor;
        if (_camera is not null && (_camera.ZoomScale != zoomScale || _camera.ViewShift != viewShift || _camera.PlayHeight != playHeight))
        {
            _camera.ZoomScale = zoomScale; _camera.ViewShift = viewShift; _camera.PlayHeight = playHeight;
            var viewport = factor != 0 ? new Vector2(virtualSize.X, virtualSize.Y) : GetViewport() == window ? new Vector2(window.Size.X, window.Size.Y) : WorldViewport;
            if (IsWorld && _readyActorPlan is not null) _camera.ResizeWorld(WorldAnchor, viewport);
            else _camera.ReapplyZoom();
        }
    }

    private SteamwovenLayoutChoice? _appliedInterfaceLayout;

    /// <summary>ARC-UI-2890 (AC1): the layout the game option chooses (the <c>interface layout</c> line; this run's ARC_STEAMWOVEN_LAYOUT overrides it with column or full-width), given to the layout model once a change.</summary>
    internal SteamwovenLayoutChoice EffectiveInterfaceLayout =>
        SteamwovenLayoutChoices.TryParse(System.Environment.GetEnvironmentVariable("ARC_STEAMWOVEN_LAYOUT")) ?? _gameSettings.InterfaceLayout;

    private void SyncInterfaceLayout()
    {
        var choice = EffectiveInterfaceLayout;
        if (_appliedInterfaceLayout == choice) return;
        _appliedInterfaceLayout = choice;
        SteamwovenHud.Options = SteamwovenLayoutChoices.Options(choice);
        _gameBarPanel?.Relayout();
    }

    /// <summary>ARC-RENDER-2060, ARC-RENDER-2076, ARC-UI-2174: the relit look is a component of HD, not a setting of its own: it is on exactly while the active
    /// visual profile is HD (launcher HD row, H key, ARC_VISUAL_PROFILE). An old <c>relit look</c> line of the settings file is not read.</summary>
    internal static bool RelitLookWanted(VisualProfile? profile) => profile == VisualProfile.Hd;

    // Set only by the HD identity smoke (HD equals Legacy pixel for pixel), which needs the look out of the way.
    private bool _relitLookHeld;

    /// <summary>Per frame, the relit look follows the active visual profile. A change refreshes the lights the shaders read; with the look off
    /// nothing about the draw differs from Legacy.</summary>
    private void SyncRelitLook()
    {
        var want = !_relitLookHeld && RelitLookWanted(_canvas?.VisualProfile);
        if (want == AreaRelight.Look) return;
        AreaRelight.SetLook(want);
        PushAreaAmbient();
        _canvas?.RefreshRelight();
        GD.Print($"ARC-RENDER-2076 relit look {(want ? "on" : "off")} (profile={_canvas?.VisualProfile})");
    }

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

/// <summary>The visual profile preference (ARC-UI-006): since ARC-UI-2174 the one <c>hd</c> line (0 or 1) of the one settings file
/// (ARC-UI-1130, <see cref="ViewerSettingsConfig"/>), HD On meaning profile Hd; a missing or unknown value loads as Legacy (an old file without an <c>hd</c> line
/// reads its <c>hd interface</c> line; the old <c>visual profile</c> line is ignored).
/// <c>VISUAL_PROFILE_TEST_PATH</c> names a settings file for tests; an automated run without it has no file at all
/// (Legacy, nothing saved), so it never reads or changes the owner's setting.</summary>
public sealed class VisualProfileSettingsStore
{
    public const string TestVariable = "VISUAL_PROFILE_TEST_PATH";
    private readonly string? _path;

    public VisualProfileSettingsStore(bool automated) => _path = ViewerSettingsFile.Choose(TestVariable, automated);

    /// <summary>False for an automated run without a test path: nothing is read or written.</summary>
    public bool Persistent => _path is not null;
    /// <summary>A saved line exists (startup reports "saved setting" only then).</summary>
    public bool HasSaved => _path is not null && ViewerSettingsConfig.HasHd(ArcanumConfig.Read(_path));

    public VisualProfile Load() => _path is null || !ViewerSettingsConfig.ReadHd(ArcanumConfig.Read(_path)) ? VisualProfile.Legacy : VisualProfile.Hd;

    /// <summary>Writes the profile; false when it could not be saved (the caller reports it).</summary>
    public bool Save(VisualProfile profile) => _path is not null && ArcanumConfig.Save(_path, ViewerSettingsConfig.HdChanges(profile == VisualProfile.Hd)) == ArcanumConfigSaveStatus.Saved;
}
