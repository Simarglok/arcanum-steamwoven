using System.Globalization;
using Arcanum.Data;
using Arcanum.Data.Hd;
using Godot;

namespace Arcanum.Godot;

// ARC-RENDER-015: before/after review of an HD pack in the ART viewer. Any pack the
// ARC-DATA-014 reader opens is accepted (network, generative, algorithm or external
// method; 2× or 4×; soft or legacy alpha). Approve/reject marks are written into the
// pack manifest; captures go to a local folder. Nothing here draws the game.
public partial class ViewerApp
{
    private const int MaxPackListItems = 2000;
    private const int MaxHdTextures = 256;
    private const long MaxHdTextureBytes = 1L << 30;
    private const double DemoWaitSeconds = 10;

    private readonly HdReviewState _hd = new();
    private readonly Dictionary<string, (ImageTexture Texture, bool AlphaMatches, long Bytes)> _hdTextures = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _hdTextureOrder = new();
    private readonly List<Control> _presentationHidden = new();
    private long _hdTextureBytes;
    private Dictionary<string, List<int>> _entriesByName = new(StringComparer.Ordinal);
    private ShownFrame? _shownFrame;
    private HdNavigation? _navigation;
    private HdDemo? _demo;
    private double? _demoSeconds;
    private bool _demoCapture;
    private int? _startupPackEntry;
    private string _captureDirectory = string.Empty;
    private bool _presentation;

    private VBoxContainer _root = null!;
    private ItemList _packItems = null!;
    private Label _packLabel = null!;
    private OptionButton _viewMode = null!;
    private SpinBox _flipSeconds = null!;
    private CheckBox _unreviewedOnly = null!;
    private CheckBox _nextAfterMark = null!;
    private FileDialog _packDialog = null!;

    private sealed record ShownFrame(SelectedArtSnapshot Snapshot, int Group, int Frame, int Palette);

    private sealed class HdNavigation(HdPackEntry entry, IEnumerable<int> candidates)
    {
        internal HdPackEntry Entry { get; } = entry;
        internal Queue<int> Candidates { get; } = new(candidates);
        internal int? Source { get; set; }
        internal SelectedArtSnapshot? Committed { get; set; }
    }

    private sealed class HdDemo(IReadOnlyList<HdPackEntry> entries, double secondsPerFrame)
    {
        internal IReadOnlyList<HdPackEntry> Entries { get; } = entries;
        internal double SecondsPerFrame { get; } = secondsPerFrame;
        internal int Position { get; set; } = -1;
        internal double Elapsed { get; set; }
        internal bool Shown { get; set; }
        internal bool Captured { get; set; }
    }

    private void BuildHdReviewUi(VBoxContainer root, HSplitContainer lists)
    {
        _root = root;
        _packItems = new ItemList { CustomMinimumSize = new Vector2(360, 150), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        lists.AddChild(_packItems);

        // Wraps instead of widening the window past its size.
        var row = new HFlowContainer();
        var openPack = new Button { Text = "Open HD pack…" };
        _viewMode = new OptionButton();
        _viewMode.AddItem("Legacy", (int)HdViewMode.Legacy);
        _viewMode.AddItem("HD", (int)HdViewMode.Hd);
        _viewMode.AddItem("Flip", (int)HdViewMode.Flip);
        _viewMode.AddItem("Split", (int)HdViewMode.Split);
        _flipSeconds = new SpinBox { MinValue = 0.1, MaxValue = 60, Step = 0.1, Value = _hd.FlipSeconds, Suffix = "s", CustomMinimumSize = new Vector2(84, 0) };
        var approve = new Button { Text = "Approve (A)" };
        var reject = new Button { Text = "Reject (R)" };
        var unmark = new Button { Text = "Unmark (U)" };
        var previous = new Button { Text = "◀ HD frame (P)" };
        var next = new Button { Text = "HD frame ▶ (N)" };
        _unreviewedOnly = new CheckBox { Text = "Unreviewed only" };
        _nextAfterMark = new CheckBox { Text = "Next after mark", ButtonPressed = true };
        var capture = new Button { Text = "Capture (F12)" };
        var presentation = new Button { Text = "Presentation (F10)" };
        foreach (var control in new Control[] { openPack, _viewMode, new Label { Text = "Flip every", VerticalAlignment = VerticalAlignment.Center }, _flipSeconds,
                     approve, reject, unmark, previous, next, _unreviewedOnly, _nextAfterMark, capture, presentation })
            row.AddChild(control);
        root.AddChild(row);
        _packLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(_packLabel);
        _packDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "Select one HD pack directory" };
        AddChild(_packDialog);
        _captureDirectory = HdReviewCapture.DefaultDirectory();

        openPack.Pressed += () => _packDialog.PopupCenteredRatio(0.75f);
        _packDialog.DirSelected += OpenPack;
        _viewMode.ItemSelected += _ => SetHdMode((HdViewMode)_viewMode.GetSelectedId());
        _flipSeconds.ValueChanged += value => _hd.SetFlipSeconds(value);
        approve.Pressed += () => Mark(HdReviewStatus.Approved);
        reject.Pressed += () => Mark(HdReviewStatus.Rejected);
        unmark.Pressed += () => Mark(HdReviewStatus.Unreviewed);
        previous.Pressed += () => StepPackFrame(-1);
        next.Pressed += () => StepPackFrame(1);
        _unreviewedOnly.Toggled += _ => RebuildPackList();
        _packItems.ItemSelected += index =>
        {
            if (_hd.Pack is { } pack && _packItems.GetItemMetadata((int)index).VariantType == Variant.Type.Int)
                NavigateTo(pack.Entries[_packItems.GetItemMetadata((int)index).AsInt32()]);
        };
        capture.Pressed += Capture;
        presentation.Pressed += TogglePresentation;
        UpdatePackSummary();
    }

    // Startup options (after `--`): --dat FILE, --hd-pack DIR, --hd-entry INDEX,
    // --hd-view legacy|hd|flip|split, --hd-flip-seconds S, --hd-capture-dir DIR,
    // --hd-demo SECONDS_PER_FRAME (walks the approved frames, or every frame not rejected,
    // in presentation mode and quits), --hd-demo-capture (the demo captures each frame once
    // while HD is shown), --presentation. Paths should be absolute: Godot runs
    // from the project directory.
    private void ApplyStartupArguments(IReadOnlyList<string> arguments)
    {
        string? Value(string name)
        {
            for (var i = 0; i < arguments.Count - 1; i++) if (arguments[i] == name) return arguments[i + 1];
            return null;
        }

        if (Value("--hd-capture-dir") is { } directory) _captureDirectory = Path.GetFullPath(directory);
        if (Value("--hd-flip-seconds") is { } flip && double.TryParse(flip, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 0.1 and <= 60)
            _flipSeconds.Value = seconds;
        if (Value("--hd-pack") is { } pack) OpenPack(Path.GetFullPath(pack));
        if (Value("--hd-view") is { } view && Enum.TryParse<HdViewMode>(view, ignoreCase: true, out var mode) && Enum.IsDefined(mode)) SetHdMode(mode);
        if (Value("--hd-entry") is { } entry && int.TryParse(entry, NumberStyles.None, CultureInfo.InvariantCulture, out var index)) _startupPackEntry = index;
        if (Value("--hd-demo") is { } demo && double.TryParse(demo, NumberStyles.Float, CultureInfo.InvariantCulture, out var perFrame) && perFrame is >= 0.5 and <= 600)
            _demoSeconds = perFrame;
        _demoCapture = arguments.Contains("--hd-demo-capture");
        if (arguments.Contains("--presentation") && !_presentation) TogglePresentation();
        if (Value("--dat") is { } dat) OpenArchive(Path.GetFullPath(dat));
        else if (_demoSeconds is not null) { GD.PushError("--hd-demo needs --dat and --hd-pack."); GetTree().Quit(1); }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || _canvas is null || _packDialog is null) return;
        if (key.CtrlPressed || key.MetaPressed || key.AltPressed || _packDialog.Visible || _dialog.Visible) return;
        // Typing into the filter or a number field keeps its letters.
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return;
        var review = _hd.Pack is not null;
        switch (key.Keycode)
        {
            case Key.F12: Capture(); break;
            case Key.F10: TogglePresentation(); break;
            case Key.A when review: Mark(HdReviewStatus.Approved); break;
            case Key.R when review: Mark(HdReviewStatus.Rejected); break;
            case Key.U when review: Mark(HdReviewStatus.Unreviewed); break;
            case Key.N or Key.Pagedown when review: StepPackFrame(1); break;
            case Key.P or Key.Pageup when review: StepPackFrame(-1); break;
            case Key.H when review: SetHdMode(_canvas.View == FrameView.Hd ? HdViewMode.Legacy : HdViewMode.Hd); break;
            case Key.F when review: SetHdMode(HdViewMode.Flip); break;
            case Key.S when review: SetHdMode(HdViewMode.Split); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void OpenPack(string directory)
    {
        var opened = HdPack.Open(directory);
        if (opened.Status != HdPackOpenStatus.Opened)
        {
            SetStatus($"The HD pack could not be opened ({opened.Status}).");
            if (_demoSeconds is not null) { GD.PushError($"HD pack: {opened.Status}"); GetTree().Quit(1); }
            return;
        }
        _navigation = null;
        ClearHdTextures();
        _hd.SetPack(opened.Pack);
        RebuildPackList();
        UpdatePackSummary();
        RequestHdForShownFrame();
        SetStatus($"Opened HD pack {opened.Pack!.Id} with {_hd.Index!.Sprites.Count} sprite frames.");
    }

    private void OnHdArchiveOpened()
    {
        _entriesByName = new(StringComparer.Ordinal);
        for (var i = 0; i < _entries.Count; i++)
        {
            var name = HdReviewState.NameSha256(_entries[i].Entry);
            if (!_entriesByName.TryGetValue(name, out var list)) _entriesByName[name] = list = new();
            list.Add(i);
        }
        RebuildPackList();
        if (_demoSeconds is { } seconds && _demo is null)
        {
            StartDemo(seconds);
            return;
        }
        if (_startupPackEntry is { } index && _hd.Pack is { } pack)
        {
            _startupPackEntry = null;
            if (index < pack.Entries.Count && pack.Entries[index].Kind == HdEntryKind.Sprite) NavigateTo(pack.Entries[index]);
            else SetStatus($"The HD pack has no sprite frame #{index}.");
        }
    }

    private void OnHdEntryCommitted(SelectedArtSnapshot snapshot)
    {
        // Applied on the next tick: the session submits frame 0 right after this callback.
        if (_navigation is { Source: { } source } navigation && _entries[source].Entry.Ordinal == snapshot.Identity.EntryOrdinal)
            navigation.Committed = snapshot;
    }

    private void OnHdFramePublished(SelectedArtSnapshot snapshot, FrameTarget target, ArtFrame decoded)
    {
        _shownFrame = new(snapshot, target.Group, target.Frame, decoded.EffectivePaletteSlot);
        RequestHdForShownFrame();
    }

    private void ResetHdFrame()
    {
        _shownFrame = null;
        _hd.Invalidate();
        UpdateHdView();
    }

    private void ProcessHdReview(double delta)
    {
        if (_navigation is { Committed: { } committed } navigation)
        {
            navigation.Committed = null;
            if (ReferenceEquals(_selectedArt, committed)) ApplyNavigationFrame(committed);
        }
        if (_hd.Poll() is { } result) ApplyHdResult(result);
        if (_hd.AdvanceFlip(delta)) UpdateHdView();
        AdvanceDemo(delta);
    }

    private void RequestHdForShownFrame()
    {
        if (_shownFrame is not { } shown || _hd.Index is null)
        {
            _hd.Invalidate();
            UpdateHdView();
            return;
        }
        if (_hd.TryCached(shown.Snapshot, shown.Group, shown.Frame, shown.Palette, entry => _hdTextures.ContainsKey(TextureKey(entry))) is { } cached)
        {
            ApplyHdResult(cached);
            return;
        }
        _hd.Request(shown.Snapshot, shown.Group, shown.Frame, shown.Palette);
        _canvas.SetHd(null);
        UpdateHdView();
    }

    private void ApplyHdResult(HdReviewResult result)
    {
        if (_navigation is { Source: { } source } navigation && _entries[source].Entry.Ordinal == result.Request.Snapshot.Identity.EntryOrdinal)
        {
            if (result.ArtSha256 != navigation.Entry.Key!.Value.ArtSha256)
            {
                NavigateNextCandidate();
                return;
            }
            var key = navigation.Entry.Key!.Value;
            if (result.Entry?.Index == navigation.Entry.Index)
            {
                _navigation = null;
                _canvas.SetFit();
            }
            else if (result.Request.Group == key.Group && result.Request.Frame == key.Frame)
            {
                // The frame is on screen but under another effective palette slot.
                _navigation = null;
                SetStatus($"Pack frame #{navigation.Entry.Index}: palette slot {key.Palette} falls back to slot {result.Request.Palette} in this ART.");
            }
        }

        ImageTexture? texture = null;
        if (result.Entry is { } entry)
        {
            var key = TextureKey(entry);
            if (_hdTextures.TryGetValue(key, out var hit))
            {
                _hdTextureOrder.Remove(key);
                _hdTextureOrder.AddLast(key);
                texture = hit.Texture;
            }
            else if (result.Frame is { Status: HdReviewFrameStatus.Compared, Hd: { } hd, Legacy: { } legacy } pair)
            {
                texture = HdReviewCapture.CreateHdTexture(hd, legacy.Width, legacy.Height);
                AddTexture(key, texture, pair.AlphaMatches, hd.Rgba!.LongLength);
            }
            else if (result.Cached)
            {
                // Evicted since the lookup: compare it again.
                _hd.Request(result.Request.Snapshot, result.Request.Group, result.Request.Frame, result.Request.Palette);
                return;
            }
        }
        _canvas.SetHd(texture);
        UpdateHdView();
    }

    private bool NavigateTo(HdPackEntry entry)
    {
        if (_entries.Count == 0)
        {
            SetStatus("Open the DAT that holds this pack frame's ART first.");
            return false;
        }
        if (!_entriesByName.TryGetValue(entry.NameSha256!, out var sources))
        {
            SetStatus($"Pack frame #{entry.Index}: its ART is not in the open DAT.");
            return false;
        }
        _navigation = new HdNavigation(entry, sources);
        SelectListItem(_packItems, entry.Index);
        return NavigateNextCandidate();
    }

    // Tries the next DAT entry carrying the frame's ART name (duplicates are possible).
    private bool NavigateNextCandidate()
    {
        var navigation = _navigation!;
        if (!navigation.Candidates.TryDequeue(out var source))
        {
            _navigation = null;
            SetStatus($"Pack frame #{navigation.Entry.Index}: the open DAT's ART bytes differ from the pack's (another DAT, a mod or another game version).");
            return false;
        }
        navigation.Source = source;
        SelectListItem(_items, source);
        if (_selectedArt is { } selected && selected.Identity.EntryOrdinal == _entries[source].Entry.Ordinal)
        {
            ApplyNavigationFrame(selected);
            return true;
        }
        ClearSelection(clearEntries: false);
        SetBusy("Reading the pack frame's ART…");
        _session.SelectEntry(_entries[source].Entry, this);
        return true;
    }

    private void ApplyNavigationFrame(SelectedArtSnapshot snapshot)
    {
        var key = _navigation!.Entry.Key!.Value;
        var header = snapshot.Metadata.Header;
        if (key.Group >= header.PhysicalGroupCount || key.Frame >= header.FramesPerGroup)
        {
            NavigateNextCandidate();
            return;
        }
        _updatingSelectors = true;
        _group.Value = key.Group;
        _palette.Value = key.Palette;
        _updatingSelectors = false;
        Timeline.Seek(key.Frame);
        Timeline.Pause();
        RequestCurrentFrame();
        SyncTimelineUi();
        // Already shown (same ART, same frame): the lookup result completes the navigation.
        RequestHdForShownFrame();
    }

    private void StepPackFrame(int step)
    {
        if (_hd.Index is not { } index) { SetStatus("Open an HD pack first."); return; }
        var from = _navigation?.Entry.Index ?? _hd.Current?.Entry?.Index ?? -1;
        var next = index.Step(from, step, _unreviewedOnly.ButtonPressed ? e => e.Review == HdReviewStatus.Unreviewed : null);
        if (next is null) { SetStatus("No pack frame left to review."); return; }
        NavigateTo(next);
    }

    private void Mark(HdReviewStatus status)
    {
        if (_hd.Pack is not { } pack) { SetStatus("Open an HD pack first."); return; }
        if (_hd.Current?.Entry is not { } entry) { SetStatus("No pack frame is on screen to mark."); return; }
        var saved = HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [entry.Index] = status });
        switch (saved.Status)
        {
            case HdReviewSaveStatus.Saved or HdReviewSaveStatus.Unchanged:
                _hd.ReplacePack(saved.Pack!);
                UpdatePackItem(saved.Pack!.Entries[entry.Index]);
                UpdatePackSummary();
                if (_hd.Current is null) RequestHdForShownFrame();
                UpdateHdView();
                SetStatus($"Pack frame #{entry.Index} marked {ReviewName(status)}; written to the pack manifest.");
                if (_nextAfterMark.ButtonPressed) StepPackFrame(1);
                break;
            case HdReviewSaveStatus.Changed:
                SetStatus("The pack manifest changed on disk since the pack was opened; nothing was written. Reopen the pack.");
                break;
            default:
                SetStatus($"The mark was not saved ({saved.Status}); the manifest is unchanged.");
                break;
        }
    }

    private void SetHdMode(HdViewMode mode)
    {
        _hd.SetMode(mode);
        for (var i = 0; i < _viewMode.ItemCount; i++)
            if (_viewMode.GetItemId(i) == (int)mode) _viewMode.Select(i);
        UpdateHdView();
    }

    private void Capture()
    {
        var utc = DateTime.UtcNow;
        var shown = _shownFrame;
        var subject = HdReviewCapture.Subject(_hd.Current?.Entry, _hd.Pack, shown?.Snapshot.Identity.EntryOrdinal, shown?.Group ?? 0, shown?.Frame ?? 0, shown?.Palette ?? 0);
        var saved = new List<string>();
        try
        {
            using (var window = GetViewport().GetTexture().GetImage())
                saved.Add(HdReviewCapture.Save(window, _captureDirectory, HdReviewCapture.FileName(subject, "window", utc)));
            if (_canvas.HdTexture is { } hdTexture && _publication.Texture is { } legacyTexture && _hd.Pack is { } pack)
            {
                using var legacy = legacyTexture.GetImage();
                using var hd = hdTexture.GetImage();
                using var pair = HdReviewCapture.ComposePair(legacy, hd, pack.Scale);
                saved.Add(HdReviewCapture.Save(pair, _captureDirectory, HdReviewCapture.FileName(subject, "pair", utc)));
            }
            SetStatus($"Captured {string.Join(" and ", saved)} into the local capture folder.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("The capture could not be written to the capture folder.");
        }
    }

    private void TogglePresentation()
    {
        _presentation = !_presentation;
        if (_presentation)
        {
            foreach (var child in _root.GetChildren().OfType<Control>())
                if (!ReferenceEquals(child, _canvas) && child.Visible) { child.Visible = false; _presentationHidden.Add(child); }
        }
        else
        {
            foreach (var child in _presentationHidden) child.Visible = true;
            _presentationHidden.Clear();
        }
        UpdateHdView();
    }

    private void StartDemo(double secondsPerFrame)
    {
        if (_hd.Index is not { } index) { GD.PushError("--hd-demo: no HD pack is open."); GetTree().Quit(1); return; }
        var approved = index.Sprites.Where(e => e.Review == HdReviewStatus.Approved).ToArray();
        var entries = approved.Length > 0 ? approved : index.Sprites.Where(e => e.Review != HdReviewStatus.Rejected).ToArray();
        if (entries.Length == 0) { GD.PushError("--hd-demo: the pack has no frame to show."); GetTree().Quit(1); return; }
        _demo = new HdDemo(entries, secondsPerFrame);
        if (!_presentation) TogglePresentation();
        NextDemoFrame();
    }

    private void NextDemoFrame()
    {
        var demo = _demo!;
        while (++demo.Position < demo.Entries.Count)
        {
            demo.Elapsed = 0;
            demo.Shown = false;
            demo.Captured = false;
            SetHdMode(HdViewMode.Flip);
            if (NavigateTo(demo.Entries[demo.Position])) return;
        }
        GD.Print($"HD demo finished: {demo.Entries.Count} frames.");
        GetTree().Quit(0);
    }

    private void AdvanceDemo(double delta)
    {
        if (_demo is not { } demo || demo.Position < 0 || demo.Position >= demo.Entries.Count) return;
        demo.Elapsed += delta;
        if (!demo.Shown)
        {
            if (_navigation is null && _hd.Current?.Entry?.Index == demo.Entries[demo.Position].Index)
            {
                demo.Shown = true;
                demo.Elapsed = 0;
                SetHdMode(HdViewMode.Flip);
            }
            else if (demo.Elapsed > DemoWaitSeconds) NextDemoFrame();
            return;
        }
        // Half a period into the HD side, so the window capture shows HD.
        if (_demoCapture && !demo.Captured && _hd.FlipShowsHd && demo.Elapsed >= _hd.FlipSeconds * 1.5)
        {
            demo.Captured = true;
            Capture();
        }
        if (demo.Elapsed >= demo.SecondsPerFrame) NextDemoFrame();
    }

    private void UpdateHdView()
    {
        if (_canvas is null) return;
        _canvas.SetView(_hd.Index is null ? FrameView.Legacy : _hd.CanvasView);
        _canvas.SetCaption(HdCaption());
    }

    private string HdCaption()
    {
        if (_hd.Pack is not { } pack || _shownFrame is null) return string.Empty;
        var hd = _canvas.HdTexture is not null;
        var side = _canvas.View switch
        {
            FrameView.Split when hd => $"ORIGINAL  |  HD {pack.Scale}×",
            FrameView.Hd when hd => $"HD {pack.Scale}×",
            _ => "ORIGINAL",
        };
        if (_presentation) return side;
        var detail = _hd.Current switch
        {
            null => "looking up the HD frame…",
            { Error: { } error } => $"HD lookup failed ({error})",
            { Entry: null } => "no HD frame in this pack",
            { Entry: { } entry } current => $"#{entry.Index} {ReviewName(entry.Review)}{Problem(current, entry)}",
        };
        return $"{side} · {detail}";
    }

    private string Problem(HdReviewResult result, HdPackEntry entry)
    {
        if (result.Frame is { } frame && frame.Status != HdReviewFrameStatus.Compared)
        {
            return frame.Status switch
            {
                HdReviewFrameStatus.ArtMismatch => " · ART bytes differ from the pack's",
                HdReviewFrameStatus.ViewOutOfRange => " · the frame's ART view is longer than this ART",
                HdReviewFrameStatus.LegacyUndecodable => " · the Legacy frame does not decode from the frame's view",
                HdReviewFrameStatus.PaletteMismatch => " · the palette slot falls back to another slot here",
                _ => frame.Hd is { Status: HdFrameStatus.Invalid } invalid ? $" · HD frame invalid ({invalid.Invalid})" : $" · {frame.Hd?.Status}",
            };
        }
        return _hdTextures.TryGetValue(TextureKey(entry), out var hit) && !hit.AlphaMatches
            ? " · alpha check failed: the game would draw Legacy" : string.Empty;
    }

    private void RebuildPackList()
    {
        if (_packItems is null) return;
        _packItems.Clear();
        if (_hd.Index is not { } index) return;
        var filtered = index.Sprites.Where(e => !_unreviewedOnly.ButtonPressed || e.Review == HdReviewStatus.Unreviewed).ToArray();
        foreach (var entry in filtered.Take(MaxPackListItems))
        {
            _packItems.AddItem(PackItemText(entry));
            _packItems.SetItemMetadata(_packItems.ItemCount - 1, entry.Index);
        }
        if (filtered.Length > MaxPackListItems)
        {
            _packItems.AddItem($"… {filtered.Length - MaxPackListItems} more (use N/P to reach them)");
            _packItems.SetItemDisabled(_packItems.ItemCount - 1, true);
        }
    }

    private void UpdatePackItem(HdPackEntry entry)
    {
        for (var i = 0; i < _packItems.ItemCount; i++)
            if (_packItems.GetItemMetadata(i).VariantType == Variant.Type.Int && _packItems.GetItemMetadata(i).AsInt32() == entry.Index)
                _packItems.SetItemText(i, PackItemText(entry));
    }

    private string PackItemText(HdPackEntry entry)
    {
        var key = entry.Key!.Value;
        var mark = entry.Review switch { HdReviewStatus.Approved => "✓", HdReviewStatus.Rejected => "✗", _ => "·" };
        var art = _entriesByName.TryGetValue(entry.NameSha256!, out var sources) && sources[0] < _entries.Count ? _entries[sources[0]].DisplayName : "(ART not in the open DAT)";
        return string.Create(CultureInfo.InvariantCulture, $"#{entry.Index} {mark} {entry.Slice} g{key.Group} f{key.Frame} p{key.Palette} {art}");
    }

    private void UpdatePackSummary()
    {
        if (_hd.Index is not { } index)
        {
            _packLabel.Text = "No HD pack open. Keys: A approve, R reject, U unmark, N/P next/previous pack frame, H Legacy/HD, F flip, S split, F12 capture, F10 presentation.";
            return;
        }
        var pack = index.Pack;
        var method = pack.Recipe.Method;
        _packLabel.Text = string.Create(CultureInfo.InvariantCulture,
            $"HD pack {pack.Id} · {pack.Scale}× · {method.Kind.ToString().ToLowerInvariant()} {method.Tool}{(method.Model is null ? "" : " / " + method.Model)} · alpha {pack.Recipe.Alpha.Mode.ToString().ToLowerInvariant()} · {index.Sprites.Count} frames: {index.Count(HdReviewStatus.Approved)} approved, {index.Count(HdReviewStatus.Rejected)} rejected, {index.Count(HdReviewStatus.Unreviewed)} unreviewed · pack {ReviewName(pack.Review)}");
    }

    private static string ReviewName(HdReviewStatus status) => status switch
    {
        HdReviewStatus.Approved => "approved",
        HdReviewStatus.Rejected => "rejected",
        _ => "unreviewed",
    };

    private static void SelectListItem(ItemList list, int metadata)
    {
        for (var i = 0; i < list.ItemCount; i++)
        {
            if (list.GetItemMetadata(i).VariantType != Variant.Type.Int || list.GetItemMetadata(i).AsInt32() != metadata) continue;
            list.Select(i);
            list.EnsureCurrentIsVisible();
            return;
        }
    }

    private string TextureKey(HdPackEntry entry) => $"{_hd.Pack!.RecipeSha256}:{entry.Sha256}:{entry.Legacy.RgbaSha256}";

    private void AddTexture(string key, ImageTexture texture, bool alphaMatches, long bytes)
    {
        _hdTextures[key] = (texture, alphaMatches, bytes);
        _hdTextureOrder.AddLast(key);
        _hdTextureBytes += bytes;
        var node = _hdTextureOrder.First;
        while (node is not null && (_hdTextures.Count > MaxHdTextures || _hdTextureBytes > MaxHdTextureBytes))
        {
            var nextNode = node.Next;
            var (old, _, size) = _hdTextures[node.Value];
            if (!ReferenceEquals(old, texture) && !ReferenceEquals(old, _canvas.HdTexture))
            {
                _hdTextures.Remove(node.Value);
                _hdTextureOrder.Remove(node);
                _hdTextureBytes -= size;
                old.Dispose();
            }
            node = nextNode;
        }
    }

    private void ClearHdTextures()
    {
        _canvas?.SetHd(null);
        foreach (var (texture, _, _) in _hdTextures.Values) texture.Dispose();
        _hdTextures.Clear();
        _hdTextureOrder.Clear();
        _hdTextureBytes = 0;
    }
}
