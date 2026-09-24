using Arcanum.Data;
using Arcanum.Presentation.Animation;
using Godot;

namespace Arcanum.Godot;

public partial class ViewerApp : Control, IViewerPublicationSink
{
    private readonly ViewerSession _session = new();
    private readonly ViewerPublicationState _publication = new();
    private IReadOnlyList<ViewerEntry> _entries = Array.Empty<ViewerEntry>();
    private SelectedArtSnapshot? _selectedArt;
    private bool _updatingSelectors;
    private bool _closing;

    private Button _openButton = null!;
    private LineEdit _filter = null!;
    private ItemList _items = null!;
    private SpinBox _group = null!;
    private SpinBox _frame = null!;
    private SpinBox _palette = null!;
    private CheckBox _overrideEnabled = null!;
    private SpinBox _overrideFps = null!;
    private OptionButton _speed = null!;
    private OptionButton _mode = null!;
    private Label _rate = null!;
    private Label _metadataLabel = null!;
    private Label _status = null!;
    private FrameCanvas _canvas = null!;
    private FileDialog _dialog = null!;

    private AnimationTimeline Timeline => _session.Timeline;

    public override void _Ready()
    {
        var arguments = OS.GetCmdlineUserArgs();
        if (arguments.Contains("--write-synthetic-dat")) { WriteSyntheticDat(arguments); return; }
        if (arguments.Contains("--self-test")) { RunSelfTest(); return; }
        BuildUi();
    }

    public override void _ExitTree()
    {
        _closing = true;
        _session.Close();
        if (_canvas is not null)
        {
            _publication.Clear(_canvas);
        }
    }

    public override void _Process(double delta)
    {
        if (_closing || _group is null || _palette is null) return;
        _session.Advance(delta, (int)_group.Value, (int)_palette.Value);
        _session.DrainCompletions(this);
        SyncTimelineUi();
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationWMWindowFocusOut && Timeline.IsPlaying)
        {
            Timeline.Pause();
            SyncTimelineUi();
            SetStatus("Playback paused because the viewer lost focus.");
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key &&
            (key.Keycode == Key.F11 || (key.Keycode == Key.Escape && DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen)))
        {
            ToggleFullscreen();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildUi()
    {
        var root = new VBoxContainer { AnchorsPreset = (int)LayoutPreset.FullRect, OffsetLeft = 12, OffsetTop = 12, OffsetRight = -12, OffsetBottom = -12 };
        AddChild(root);
        var toolbar = new HBoxContainer();
        _openButton = new Button { Text = "Open DAT…" };
        toolbar.AddChild(_openButton);
        toolbar.AddChild(new Label { Text = "ART animation inspector (diagnostic; not a game runtime)", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        root.AddChild(toolbar);
        _filter = new LineEdit { PlaceholderText = "Filter ART name or decimal ordinal" };
        root.AddChild(_filter);
        _items = new ItemList { CustomMinimumSize = new Vector2(0, 150), SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(_items);

        var selectors = new HBoxContainer();
        _group = AddSelector(selectors, "Physical group");
        _frame = AddSelector(selectors, "Frame");
        _palette = AddSelector(selectors, "Palette");
        _metadataLabel = new Label { Text = "No frame", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        selectors.AddChild(_metadataLabel);
        root.AddChild(selectors);

        var playback = new HBoxContainer();
        var play = new Button { Text = "Play" };
        var pause = new Button { Text = "Pause" };
        var stop = new Button { Text = "Stop" };
        var previous = new Button { Text = "◀ Step" };
        var next = new Button { Text = "Step ▶" };
        _mode = new OptionButton();
        _mode.AddItem("Loop", (int)PlaybackMode.Loop);
        _mode.AddItem("Play once", (int)PlaybackMode.Once);
        _speed = new OptionButton();
        _speed.AddItem("0.25×", (int)PlaybackSpeed.Quarter);
        _speed.AddItem("0.5×", (int)PlaybackSpeed.Half);
        _speed.AddItem("1×", (int)PlaybackSpeed.Normal);
        _speed.AddItem("2×", (int)PlaybackSpeed.Double);
        _speed.Select(2);
        _overrideEnabled = new CheckBox { Text = "Manual FPS" };
        _overrideFps = new SpinBox { MinValue = 1, MaxValue = 120, Value = 30, AllowGreater = false, AllowLesser = false, CustomMinimumSize = new Vector2(72, 0) };
        _rate = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var control in new Control[] { play, pause, stop, previous, next, _mode, _speed, _overrideEnabled, _overrideFps, _rate }) playback.AddChild(control);
        root.AddChild(playback);

        var display = new HBoxContainer();
        var oneToOne = new Button { Text = "1:1 / reset" };
        var zoomOut = new Button { Text = "Zoom −" };
        var zoomIn = new Button { Text = "Zoom +" };
        var fit = new Button { Text = "Fit" };
        var centered = new CheckBox { Text = "Centered inspection" };
        var marker = new CheckBox { Text = "Anchor marker", ButtonPressed = true };
        var fullscreen = new Button { Text = "Fullscreen (F11)" };
        foreach (var control in new Control[] { oneToOne, zoomOut, zoomIn, fit, centered, marker, fullscreen }) display.AddChild(control);
        root.AddChild(display);
        _canvas = new FrameCanvas { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddChild(_canvas);
        _status = new Label { Text = "Open one DAT file to inspect its .art entries.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(_status);
        _dialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Title = "Select one DAT file", Filters = new[] { "*.dat ; DAT archives", "* ; Any file" } };
        AddChild(_dialog);

        _openButton.Pressed += () => _dialog.PopupCenteredRatio(0.75f);
        _dialog.FileSelected += OpenArchive;
        _filter.TextChanged += _ => RebuildList();
        _items.ItemSelected += SelectEntry;
        _group.ValueChanged += _ => ReconfigureSelection(false);
        _frame.ValueChanged += _ => ReconfigureSelection(true);
        _palette.ValueChanged += _ => ReconfigureSelection(false);
        play.Pressed += () => { Timeline.Play(); RequestCurrentFrame(); SyncTimelineUi(); };
        pause.Pressed += () => { Timeline.Pause(); SyncTimelineUi(); };
        stop.Pressed += () => { _session.Stop((int)_group.Value, (int)_palette.Value); SyncTimelineUi(); };
        previous.Pressed += () => { Timeline.Step(-1); RequestCurrentFrame(); SyncTimelineUi(); };
        next.Pressed += () => { Timeline.Step(1); RequestCurrentFrame(); SyncTimelineUi(); };
        _mode.ItemSelected += _ => { Timeline.SetMode((PlaybackMode)_mode.GetSelectedId()); SyncTimelineUi(); };
        _speed.ItemSelected += _ => { Timeline.SetSpeed((PlaybackSpeed)_speed.GetSelectedId()); SyncTimelineUi(); };
        _overrideEnabled.Toggled += _ => ApplyOverride();
        _overrideFps.ValueChanged += _ => { if (_overrideEnabled.ButtonPressed) ApplyOverride(); };
        oneToOne.Pressed += _canvas.ResetOneToOne;
        zoomOut.Pressed += () => _canvas.AdjustZoom(0.8f, _canvas.Size / 2f);
        zoomIn.Pressed += () => _canvas.AdjustZoom(1.25f, _canvas.Size / 2f);
        fit.Pressed += _canvas.SetFit;
        centered.Toggled += enabled => _canvas.SetAlignment(enabled ? FrameAlignment.Centered : FrameAlignment.RawHotspot);
        marker.Toggled += _canvas.SetAnchorMarkerVisible;
        fullscreen.Pressed += ToggleFullscreen;
        SyncTimelineUi();
    }

    private static SpinBox AddSelector(Container parent, string label)
    {
        parent.AddChild(new Label { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var selector = new SpinBox { MinValue = 0, MaxValue = 0, Step = 1, AllowGreater = false, AllowLesser = false, CustomMinimumSize = new Vector2(72, 0) };
        parent.AddChild(selector);
        return selector;
    }

    private void OpenArchive(string path)
    {
        ClearSelection(clearEntries: true);
        SetBusy("Opening DAT index…");
        _session.OpenArchive(path, this);
    }

    private void SelectEntry(long visibleIndex)
    {
        if (visibleIndex < 0 || visibleIndex >= _items.ItemCount) return;
        var sourceIndex = _items.GetItemMetadata((int)visibleIndex).AsInt64();
        if (sourceIndex < 0 || sourceIndex >= _entries.Count) return;
        ClearSelection(clearEntries: false);
        SetBusy("Reading selected ART metadata…");
        _session.SelectEntry(_entries[(int)sourceIndex].Entry, this);
    }

    private void ReconfigureSelection(bool manualFrame)
    {
        if (_updatingSelectors || _selectedArt is null) return;
        if (manualFrame) Timeline.Seek((int)_frame.Value);
        else Timeline.ReconfigureSequence(_selectedArt.Metadata.Header.FramesPerGroup);
        RequestCurrentFrame();
        SyncTimelineUi();
    }

    private void RequestCurrentFrame() => _session.RequestTarget((int)_group.Value, (int)_palette.Value);

    void IViewerPublicationSink.SelectionInvalidated(bool clearEntries) => ClearSelection(clearEntries);

    void IViewerPublicationSink.ArchiveOpened(IReadOnlyList<ViewerEntry> entries)
    {
        _entries = entries;
        RebuildList();
        SetStatus($"Indexed {_entries.Count} ART entries. Select one entry to inspect metadata and frames.");
    }

    void IViewerPublicationSink.EntryCommitted(SelectedArtSnapshot snapshot)
    {
        _selectedArt = snapshot;
        _publication.Clear(_canvas);
        _updatingSelectors = true;
        _group.MaxValue = snapshot.Metadata.Header.PhysicalGroupCount - 1;
        _frame.MaxValue = snapshot.Metadata.Header.FramesPerGroup - 1;
        _palette.MaxValue = ViewerState.MaxRequestedPaletteSlot;
        _group.Value = _frame.Value = _palette.Value = 0;
        _updatingSelectors = false;
        SyncTimelineUi();
        SetStatus("Selected ART metadata loaded; decoding frame 0.");
    }

    void IViewerPublicationSink.PublishFrame(SelectedArtSnapshot snapshot, FrameTarget target, ArtFrame decoded)
    {
        ViewerFramePublication.Commit(_canvas, _publication, snapshot, target, decoded);
        _metadataLabel.Text = $"{decoded.Width} × {decoded.Height}; raw hotspot ({decoded.Frame.HotX}, {decoded.Frame.HotY}); offset ({decoded.Frame.OffsetX}, {decoded.Frame.OffsetY}); requested palette {decoded.RequestedPaletteSlot}; effective {decoded.EffectivePaletteSlot}";
        SetStatus("Frame decoded. Raw hotspot alignment is a diagnostic projection; physical groups are not directions.");
    }

    // Shared by the production sink and deterministic resource/canvas self-test.
    // It commits nothing to the canvas until all managed RGBA and Godot texture work succeeds.
    internal static ImageTexture CreateFrameTexture(ArtFrame decoded)
    {
        if (decoded.Rgba.Count != checked(decoded.Width * decoded.Height * 4)) throw new ArtFormatException("Decoded frame has an invalid RGBA length.");
        using var image = Image.CreateFromData(decoded.Width, decoded.Height, false, Image.Format.Rgba8, decoded.Rgba.ToArray());
        ImageTexture? texture = null;
        try
        {
            texture = ImageTexture.CreateFromImage(image);
            texture.SetSizeOverride(new Vector2I(decoded.Width, decoded.Height));
            return texture;
        }
        catch
        {
            texture?.Dispose();
            throw;
        }
    }

    void IViewerPublicationSink.PublishFailure(Exception exception)
    {
        SyncTimelineUi();
        SetStatus(MapError(exception));
    }

    internal static FrameCanvasGroupLayout GroupLayout(ArtMetadata metadata, int group) => new(metadata.Frames.Skip(group * metadata.Header.FramesPerGroup).Take(metadata.Header.FramesPerGroup)
        .Select(frame => new FrameCanvasFrame(frame.Width, frame.Height, frame.HotX, frame.HotY)));

    private void ApplyOverride()
    {
        Timeline.SetManualFramesPerSecond(_overrideEnabled.ButtonPressed ? (int)_overrideFps.Value : null);
        SyncTimelineUi();
    }

    private void SyncTimelineUi()
    {
        if (!IsInstanceValid(_rate)) return;
        var source = Timeline.SourceFramesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var effective = Timeline.EffectiveFramesPerSecond?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "blocked";
        var manual = Timeline.ManualFramesPerSecond?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
        _rate.Text = $"source {source} fps; effective {effective} fps; override {manual}";
        _updatingSelectors = true;
        _frame.Value = Timeline.CurrentFrame;
        _updatingSelectors = false;
    }

    private void RebuildList()
    {
        _items.Clear();
        for (var index = 0; index < _entries.Count; index++)
        {
            if (!ViewerState.MatchesFilter(_entries[index], _filter.Text)) continue;
            _items.AddItem(_entries[index].DisplayName);
            _items.SetItemMetadata(_items.ItemCount - 1, index);
        }
    }

    private void ClearSelection(bool clearEntries)
    {
        _selectedArt = null;
        if (clearEntries)
        {
            _entries = Array.Empty<ViewerEntry>();
            RebuildList();
        }

        _publication.Clear(_canvas);
        ViewerState.ResetEntrySelectors(_group, _frame, _palette);
        _metadataLabel.Text = "No frame";
        SyncTimelineUi();
    }

    private void ToggleFullscreen() => DisplayServer.WindowSetMode(DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
    private void SetBusy(string text) { _openButton.Disabled = true; SetStatus(text); }
    private void SetStatus(string text) { _status.Text = text; _openButton.Disabled = false; }

    private static string MapError(Exception exception) => exception switch
    {
        DatLimitExceededException or ArtLimitExceededException => "The selected input exceeds this viewer’s safety limit.",
        DatUnsupportedFormatException or DatUnsupportedEntryException or ArtUnsupportedFormatException => "The selected DAT or ART variant is not supported by this viewer.",
        DatException => "The DAT archive or selected entry is malformed.",
        ArtException => "The selected ART metadata or frame is malformed or unavailable.",
        IOException or UnauthorizedAccessException => "The selected file is missing or cannot be read.",
        ArgumentOutOfRangeException or ArgumentException => "The selected group, frame, or palette is out of range.",
        _ => "The selected input could not be displayed; choose another DAT or ART entry.",
    };

    private void RunSelfTest()
    {
        try { GD.Print(GodotSelfTest.Run()); GetTree().Quit(0); }
        catch (Exception exception) { GD.PushError(exception.Message); GetTree().Quit(1); }
    }

    private void WriteSyntheticDat(IReadOnlyList<string> arguments)
    {
        var index = -1;
        for (var argumentIndex = 0; argumentIndex < arguments.Count; argumentIndex++)
        {
            if (arguments[argumentIndex] == "--write-synthetic-dat") { index = argumentIndex; break; }
        }
        if (index < 0 || index == arguments.Count - 1) { GD.PushError("--write-synthetic-dat requires an output path."); GetTree().Quit(1); return; }
        try { File.WriteAllBytes(arguments[index + 1], SyntheticFixture.CreateDat()); GD.Print("Wrote deterministic synthetic DAT fixture."); GetTree().Quit(0); }
        catch (Exception exception) { GD.PushError(exception.Message); GetTree().Quit(1); }
    }
}
