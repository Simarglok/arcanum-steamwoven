using Arcanum.Data;
using Godot;

namespace Arcanum.Godot;

public partial class ViewerApp : Control
{
    private readonly SemaphoreSlim _archiveGate = new(1, 1);
    private CancellationTokenSource _cancellation = new();
    private DatArchive? _archive;
    private Texture2D? _texture;
    private IReadOnlyList<ViewerEntry> _entries = Array.Empty<ViewerEntry>();
    private long _generation;
    private bool _updatingSelectors;

    private Button _openButton = null!;
    private Label _archiveLabel = null!;
    private LineEdit _filter = null!;
    private ItemList _items = null!;
    private SpinBox _group = null!;
    private SpinBox _frame = null!;
    private SpinBox _palette = null!;
    private Label _metadata = null!;
    private Label _status = null!;
    private FrameCanvas _canvas = null!;
    private FileDialog _dialog = null!;

    public override void _Ready()
    {
        var arguments = OS.GetCmdlineUserArgs();
        if (arguments.Contains("--write-synthetic-dat"))
        {
            WriteSyntheticDat(arguments);
            return;
        }

        if (arguments.Contains("--self-test"))
        {
            RunSelfTest();
            return;
        }

        BuildUi();
    }

    public override void _ExitTree()
    {
        ++_generation;
        _cancellation.Cancel();
        _texture?.Dispose();
        _archive?.Dispose();
        _cancellation.Dispose();
        _archiveGate.Dispose();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (key.Keycode == Key.F11 || (key.Keycode == Key.Escape && DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen))
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
        _archiveLabel = new Label { Text = "No DAT selected", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        toolbar.AddChild(_openButton);
        toolbar.AddChild(_archiveLabel);
        root.AddChild(toolbar);

        _filter = new LineEdit { PlaceholderText = "Filter ART name or decimal ordinal" };
        root.AddChild(_filter);
        _items = new ItemList { CustomMinimumSize = new Vector2(0, 180), SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(_items);

        var selectors = new HBoxContainer();
        _group = AddSelector(selectors, "Physical group");
        _frame = AddSelector(selectors, "Frame in group");
        _palette = AddSelector(selectors, "Requested palette");
        _metadata = new Label { Text = "No frame", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        selectors.AddChild(_metadata);
        root.AddChild(selectors);

        var controls = new HBoxContainer();
        var oneToOne = new Button { Text = "1:1 / reset" };
        var zoomOut = new Button { Text = "Zoom −" };
        var zoomIn = new Button { Text = "Zoom +" };
        var fit = new Button { Text = "Fit" };
        var fullscreen = new Button { Text = "Fullscreen (F11)" };
        foreach (var button in new[] { oneToOne, zoomOut, zoomIn, fit, fullscreen }) controls.AddChild(button);
        root.AddChild(controls);

        _canvas = new FrameCanvas { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddChild(_canvas);
        _status = new Label { Text = "Open one DAT file to list its .art entries.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(_status);

        _dialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Title = "Select one DAT file" };
        _dialog.Filters = new[] { "*.dat ; DAT archives", "* ; Any file" };
        AddChild(_dialog);
        _openButton.Pressed += () => _dialog.PopupCenteredRatio(0.75f);
        _dialog.FileSelected += path => _ = OpenArchiveAsync(path);
        _filter.TextChanged += _ => RebuildList();
        _items.ItemSelected += index => _ = SelectEntryAsync(index);
        _group.ValueChanged += value => { _ = SelectorChangedAsync(); };
        _frame.ValueChanged += value => { _ = SelectorChangedAsync(); };
        _palette.ValueChanged += value => { _ = SelectorChangedAsync(); };
        oneToOne.Pressed += _canvas.ResetOneToOne;
        zoomOut.Pressed += () => _canvas.AdjustZoom(0.8f, _canvas.Size / 2f);
        zoomIn.Pressed += () => _canvas.AdjustZoom(1.25f, _canvas.Size / 2f);
        fit.Pressed += _canvas.SetFit;
        fullscreen.Pressed += ToggleFullscreen;
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
        var generation = NextRequest();
        SetBusy("Opening DAT index…");
        DatArchive? opened = null;
        try
        {
            await _archiveGate.WaitAsync(_cancellation.Token);
            try
            {
                opened = await Task.Run(() => DatArchive.Open(path, ViewerState.ReaderOptions), _cancellation.Token);
                if (generation != _generation || !IsInsideTree())
                {
                    opened.Dispose();
                    return;
                }

                var previous = _archive;
                _archive = opened;
                opened = null;
                previous?.Dispose();
                _entries = ViewerState.ArtEntries(_archive);
                _archiveLabel.Text = System.IO.Path.GetFileName(path);
                _canvas.ClearFrame();
                DisposeTexture();
                _metadata.Text = "No frame selected";
                RebuildList();
                SetStatus($"Indexed {_entries.Count} ART entries. Select one entry to read and decode its payload.");
            }
            finally
            {
                _archiveGate.Release();
            }
        }
        catch (Exception exception)
        {
            opened?.Dispose();
            if (generation == _generation) SetStatus(MapError(exception));
        }
    }

    private async Task SelectEntryAsync(long visibleIndex)
    {
        var sourceIndex = _items.GetItemMetadata((int)visibleIndex).AsInt64();
        if (visibleIndex < 0 || visibleIndex >= _items.ItemCount || sourceIndex < 0 || sourceIndex >= _entries.Count)
        {
            return;
        }

        _updatingSelectors = true;
        ViewerState.ResetEntrySelectors(_group, _frame, _palette);
        _updatingSelectors = false;
        await LoadFrameAsync(_entries[(int)sourceIndex].Entry, 0, 0, 0);
    }

    private async Task SelectorChangedAsync()
    {
        if (_updatingSelectors || _items.GetSelectedItems().Length == 0)
        {
            return;
        }

        var visibleIndex = _items.GetSelectedItems()[0];
        var sourceIndex = _items.GetItemMetadata(visibleIndex).AsInt64();
        if (sourceIndex < 0 || sourceIndex >= _entries.Count)
        {
            return;
        }

        await LoadFrameAsync(_entries[(int)sourceIndex].Entry, (int)_group.Value, (int)_frame.Value, (int)_palette.Value);
    }

    private async Task LoadFrameAsync(DatEntry entry, int group, int frameIndex, int palette)
    {
        if (_archive is null)
        {
            return;
        }

        var generation = NextRequest();
        _canvas.ClearFrame();
        DisposeTexture();
        SetBusy("Reading selected ART payload and decoding frame…");
        try
        {
            await _archiveGate.WaitAsync(_cancellation.Token);
            try
            {
                var archive = _archive;
                var decoded = await Task.Run(() => ArtDecoder.Decode(archive.ReadEntry(entry), new ArtFrameSelection(group, frameIndex, palette), ViewerState.DecoderOptions), _cancellation.Token);
                if (generation != _generation || !IsInsideTree())
                {
                    return;
                }

                if (decoded.Rgba.Count != checked(decoded.Width * decoded.Height * 4))
                {
                    throw new ArtFormatException("Decoded frame has an invalid RGBA length.");
                }

                using var image = Image.CreateFromData(decoded.Width, decoded.Height, false, Image.Format.Rgba8, decoded.Rgba.ToArray());
                var replacement = ImageTexture.CreateFromImage(image);
                replacement.SetSizeOverride(new Vector2I(decoded.Width, decoded.Height));
                DisposeTexture();
                _texture = replacement;
                _canvas.SetTexture(replacement);
                _updatingSelectors = true;
                _group.MaxValue = decoded.Header.PhysicalGroupCount - 1;
                _frame.MaxValue = decoded.Header.FramesPerGroup - 1;
                _palette.MaxValue = ViewerState.MaxRequestedPaletteSlot;
                _group.Value = decoded.GroupIndex;
                _frame.Value = decoded.FrameIndex;
                _palette.Value = decoded.RequestedPaletteSlot;
                _updatingSelectors = false;
                var fallback = decoded.RequestedPaletteSlot == decoded.EffectivePaletteSlot ? "" : " (fallback)";
                _metadata.Text = $"{decoded.Width} × {decoded.Height}; requested slot {decoded.RequestedPaletteSlot}; effective slot {decoded.EffectivePaletteSlot}{fallback}; nearest; 1:1";
                SetStatus("Frame decoded. Physical groups are file groups, not directions.");
            }
            finally
            {
                _archiveGate.Release();
            }
        }
        catch (Exception exception)
        {
            if (generation == _generation)
            {
                _canvas.ClearFrame();
                DisposeTexture();
                SetStatus(MapError(exception));
            }
        }
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

    private long NextRequest()
    {
        ++_generation;
        _cancellation.Cancel();
        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
        return _generation;
    }

    private void DisposeTexture()
    {
        var previous = _texture;
        _texture = null;
        previous?.Dispose();
    }

    private void ToggleFullscreen()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
    }

    private void SetBusy(string text)
    {
        _openButton.Disabled = true;
        SetStatus(text);
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _openButton.Disabled = false;
    }

    private static string MapError(Exception exception) => exception switch
    {
        DatLimitExceededException or ArtLimitExceededException => "The selected input exceeds this viewer’s safety limit.",
        DatUnsupportedFormatException or DatUnsupportedEntryException or ArtUnsupportedFormatException => "The selected DAT or ART variant is not supported by this viewer.",
        DatException => "The DAT archive or selected entry is malformed.",
        ArtException => "The selected ART frame or palette is malformed or unavailable.",
        IOException or UnauthorizedAccessException => "The selected file is missing or cannot be read.",
        ArgumentOutOfRangeException or ArgumentException => "The selected group, frame, or palette is out of range.",
        OperationCanceledException => "Request superseded by a newer selection.",
        _ => "The selected input could not be displayed; choose another DAT or ART entry.",
    };

    private void RunSelfTest()
    {
        try
        {
            GD.Print(GodotSelfTest.Run());
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError(exception.Message);
            GetTree().Quit(1);
        }
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

        if (index < 0 || index == arguments.Count - 1)
        {
            GD.PushError("--write-synthetic-dat requires an output path.");
            GetTree().Quit(1);
            return;
        }

        try
        {
            File.WriteAllBytes(arguments[index + 1], SyntheticFixture.CreateDat());
            GD.Print("Wrote deterministic synthetic DAT fixture.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError(exception.Message);
            GetTree().Quit(1);
        }
    }
}
