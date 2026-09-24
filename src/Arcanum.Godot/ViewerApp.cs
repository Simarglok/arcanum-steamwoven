using Arcanum.Data;
using Arcanum.Presentation.Animation;
using Godot;

namespace Arcanum.Godot;

public partial class ViewerApp : Control
{
    private readonly SemaphoreSlim _archiveGate = new(1, 1);
    private readonly AnimationTimeline _timeline = new(1, 0);
    private DatArchive? _archive;
    private Texture2D? _texture;
    private IReadOnlyList<ViewerEntry> _entries = Array.Empty<ViewerEntry>();
    private SelectedArt? _selectedArt;
    private readonly LatestDecodeQueue<FrameRequest> _decodeQueue = new();
    private FrameRequest? _displayedRequest;
    private SelectedArt? _canvasArt;
    private int? _canvasGroup;
    private long _assetGeneration;
    private long _selectionGeneration;
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
        InvalidateRequests();
        _canvasArt = null;
        _canvasGroup = null;
        DisposeTexture();
        _archive?.Dispose();
        _archive = null;
    }

    public override void _Process(double delta)
    {
        var advance = _timeline.Advance(delta);
        if (advance.Status is TimelineAdvanceStatus.FrameChanged or TimelineAdvanceStatus.Completed)
        {
            SyncTimelineUi();
            RequestCurrentFrame();
        }
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationWMWindowFocusOut && _timeline.IsPlaying)
        {
            _timeline.Pause();
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
        _dialog.FileSelected += path => _ = OpenArchiveAsync(path);
        _filter.TextChanged += _ => RebuildList();
        _items.ItemSelected += index => _ = SelectEntryAsync(index);
        _group.ValueChanged += _ => ReconfigureSelection(false);
        _frame.ValueChanged += _ => ReconfigureSelection(true);
        _palette.ValueChanged += _ => ReconfigureSelection(false);
        play.Pressed += () => { _timeline.Play(); SyncTimelineUi(); RequestCurrentFrame(); };
        pause.Pressed += () => { _timeline.Pause(); SyncTimelineUi(); };
        stop.Pressed += () => { _timeline.Stop(); ++_selectionGeneration; InvalidateRequests(); SyncTimelineUi(); RequestCurrentFrame(); };
        previous.Pressed += () => { _timeline.Step(-1); SyncTimelineUi(); RequestCurrentFrame(); };
        next.Pressed += () => { _timeline.Step(1); SyncTimelineUi(); RequestCurrentFrame(); };
        _mode.ItemSelected += _ => { _timeline.SetMode((PlaybackMode)_mode.GetSelectedId()); SyncTimelineUi(); };
        _speed.ItemSelected += _ => { _timeline.SetSpeed((PlaybackSpeed)_speed.GetSelectedId()); SyncTimelineUi(); };
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

    private async Task OpenArchiveAsync(string path)
    {
        var generation = ++_assetGeneration;
        ++_selectionGeneration;
        ClearSelection();
        _entries = Array.Empty<ViewerEntry>();
        RebuildList();
        SetBusy("Opening DAT index…");
        DatArchive? opened = null;
        try
        {
            await _archiveGate.WaitAsync();
            try { opened = await Task.Run(() => DatArchive.Open(path, ViewerState.ReaderOptions)); }
            finally { _archiveGate.Release(); }
            if (!IsCurrentAsset(generation)) { opened.Dispose(); return; }
            var previous = _archive;
            _archive = opened;
            opened = null;
            previous?.Dispose();
            _entries = ViewerState.ArtEntries(_archive);
            RebuildList();
            SetStatus($"Indexed {_entries.Count} ART entries. Select one entry to inspect metadata and frames.");
        }
        catch (Exception exception)
        {
            opened?.Dispose();
            if (IsCurrentAsset(generation)) SetStatus(MapError(exception));
        }
    }

    private async Task SelectEntryAsync(long visibleIndex)
    {
        if (visibleIndex < 0 || visibleIndex >= _items.ItemCount) return;
        var sourceIndex = _items.GetItemMetadata((int)visibleIndex).AsInt64();
        if (sourceIndex < 0 || sourceIndex >= _entries.Count || _archive is null) return;
        var assetGeneration = _assetGeneration;
        var selectionGeneration = ++_selectionGeneration;
        ClearSelection();
        SetBusy("Reading selected ART metadata…");
        try
        {
            var entry = _entries[(int)sourceIndex].Entry;
            await _archiveGate.WaitAsync();
            (byte[] Bytes, ArtMetadata Metadata) loaded;
            try { loaded = await Task.Run(() => { var bytes = _archive.ReadEntry(entry); return (bytes, ArtDecoder.Inspect(bytes, ViewerState.DecoderOptions)); }); }
            finally { _archiveGate.Release(); }
            if (!IsCurrent(assetGeneration, selectionGeneration)) return;
            _selectedArt = new SelectedArt(assetGeneration, selectionGeneration, entry.Ordinal, loaded.Bytes, loaded.Metadata);
            _updatingSelectors = true;
            _group.MaxValue = _selectedArt.Metadata.Header.PhysicalGroupCount - 1;
            _frame.MaxValue = _selectedArt.Metadata.Header.FramesPerGroup - 1;
            _palette.MaxValue = ViewerState.MaxRequestedPaletteSlot;
            _group.Value = _frame.Value = _palette.Value = 0;
            _updatingSelectors = false;
            _timeline.ResetAsset(_selectedArt.Metadata.Header.FramesPerGroup, _selectedArt.Metadata.Header.FramesPerSecond);
            SyncTimelineUi();
            RequestCurrentFrame();
        }
        catch (Exception exception)
        {
            if (IsCurrent(assetGeneration, selectionGeneration)) SetStatus(MapError(exception));
        }
    }

    private void ReconfigureSelection(bool manualFrame)
    {
        if (_updatingSelectors || _selectedArt is null) return;
        ++_selectionGeneration;
        InvalidateRequests();
        if (manualFrame) _timeline.Seek((int)_frame.Value);
        else _timeline.ReconfigureSequence(_selectedArt.Metadata.Header.FramesPerGroup);
        SyncTimelineUi();
        RequestCurrentFrame();
    }

    private void RequestCurrentFrame()
    {
        var selected = _selectedArt;
        if (selected is null || _closing) return;
        var request = new FrameRequest(selected, _selectionGeneration, (int)_group.Value, _timeline.CurrentFrame, (int)_palette.Value);
        if (_displayedRequest == request || !_decodeQueue.TryStart(request)) return;
        _ = DecodeAsync(request);
    }

    private async Task DecodeAsync(FrameRequest request)
    {
        try
        {
            var bytes = request.Art.Bytes;
            var decoded = await Task.Run(() => ArtDecoder.Decode(bytes, new ArtFrameSelection(request.Group, request.Frame, request.Palette), ViewerState.DecoderOptions));
            if (!IsCurrent(request) || _closing) return;
            if (decoded.Rgba.Count != checked(decoded.Width * decoded.Height * 4)) throw new ArtFormatException("Decoded frame has an invalid RGBA length.");
            var layout = GroupLayout(request.Art.Metadata, request.Group);
            using var image = Image.CreateFromData(decoded.Width, decoded.Height, false, Image.Format.Rgba8, decoded.Rgba.ToArray());
            ImageTexture? replacement = null;
            try
            {
                replacement = ImageTexture.CreateFromImage(image);
                replacement.SetSizeOverride(new Vector2I(decoded.Width, decoded.Height));
                var hotspot = new FrameHotspot(decoded.Frame.HotX, decoded.Frame.HotY);
                if (!ReferenceEquals(_canvasArt, request.Art))
                {
                    _canvas.ResetAsset(replacement, hotspot, layout);
                    _canvasArt = request.Art;
                    _canvasGroup = request.Group;
                }
                else
                {
                    if (_canvasGroup != request.Group)
                    {
                        _canvas.UpdateGroupLayout(layout);
                        _canvasGroup = request.Group;
                    }

                    _canvas.UpdateFrame(replacement, hotspot);
                }

                DisposeTexture();
                _texture = replacement;
                replacement = null;
            }
            finally
            {
                replacement?.Dispose();
            }
            _displayedRequest = request;
            _metadataLabel.Text = $"{decoded.Width} × {decoded.Height}; raw hotspot ({decoded.Frame.HotX}, {decoded.Frame.HotY}); offset ({decoded.Frame.OffsetX}, {decoded.Frame.OffsetY}); requested palette {decoded.RequestedPaletteSlot}; effective {decoded.EffectivePaletteSlot}";
            SetStatus("Frame decoded. Raw hotspot alignment is a diagnostic projection; physical groups are not directions.");
        }
        catch (Exception exception)
        {
            if (IsCurrent(request))
            {
                _timeline.Pause();
                _decodeQueue.MarkFailed(request);
                SyncTimelineUi();
                SetStatus(MapError(exception));
            }
        }
        finally
        {
            var next = _decodeQueue.Complete(request, IsCurrent);
            if (next is not null) _ = DecodeAsync(next);
        }
    }

    private static FrameCanvasGroupLayout GroupLayout(ArtMetadata metadata, int group) => new(metadata.Frames.Skip(group * metadata.Header.FramesPerGroup).Take(metadata.Header.FramesPerGroup)
        .Select(frame => new FrameCanvasFrame(frame.Width, frame.Height, frame.HotX, frame.HotY)));

    private void ApplyOverride()
    {
        _timeline.SetManualFramesPerSecond(_overrideEnabled.ButtonPressed ? (int)_overrideFps.Value : null);
        SyncTimelineUi();
    }

    private void SyncTimelineUi()
    {
        if (!IsInstanceValid(_rate)) return;
        var source = _timeline.SourceFramesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var effective = _timeline.EffectiveFramesPerSecond?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "blocked";
        var manual = _timeline.ManualFramesPerSecond?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
        _rate.Text = $"source {source} fps; effective {effective} fps; override {manual}";
        _updatingSelectors = true;
        _frame.Value = _timeline.CurrentFrame;
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

    private void InvalidateRequests()
    {
        _decodeQueue.InvalidatePending();
        _displayedRequest = null;
    }

    private void ClearSelection()
    {
        _selectedArt = null;
        _canvasArt = null;
        _canvasGroup = null;
        InvalidateRequests();
        _timeline.Stop();
        _canvas.ClearAsset();
        DisposeTexture();
        if (IsInstanceValid(_metadataLabel)) _metadataLabel.Text = "No frame";
        SyncTimelineUi();
    }

    private bool IsCurrent(long assetGeneration, long selectionGeneration) => !_closing &&
        assetGeneration == _assetGeneration && selectionGeneration == _selectionGeneration;

    private bool IsCurrent(FrameRequest request) => !_closing &&
        request.Art.AssetGeneration == _assetGeneration &&
        request.SelectionGeneration == _selectionGeneration &&
        ReferenceEquals(request.Art, _selectedArt);
    private bool IsCurrentAsset(long generation) => !_closing && generation == _assetGeneration;
    private void DisposeTexture() { var previous = _texture; _texture = null; previous?.Dispose(); }
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
            if (arguments[argumentIndex] == "--write-synthetic-dat")
            {
                index = argumentIndex;
                break;
            }
        }
        if (index < 0 || index == arguments.Count - 1) { GD.PushError("--write-synthetic-dat requires an output path."); GetTree().Quit(1); return; }
        try { File.WriteAllBytes(arguments[index + 1], SyntheticFixture.CreateDat()); GD.Print("Wrote deterministic synthetic DAT fixture."); GetTree().Quit(0); }
        catch (Exception exception) { GD.PushError(exception.Message); GetTree().Quit(1); }
    }

    private sealed record SelectedArt(long AssetGeneration, long SelectionGeneration, int EntryOrdinal, byte[] Bytes, ArtMetadata Metadata);
    private sealed record FrameRequest(SelectedArt Art, long SelectionGeneration, int Group, int Frame, int Palette);
}
