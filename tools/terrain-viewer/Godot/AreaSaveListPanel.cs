using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-010 save and load window: the save list of <see cref="AreaSaveList"/> as a scrolled list (the auto slot first, then newest
/// first), the selected save's details and preview, a description field (save mode), and Save/Load, Delete and Close buttons; a
/// question (overwrite, delete) shows Yes and No. The app owns the state and rebuilds the window after every change.
/// ARC-UI-062: with the game's art (<see cref="SetArt"/>) the window is drawn in the original's look (<see cref="AreaSaveListArtView"/>)
/// and the description is typed into the new-save row; PROJECT: the selected save's details, a notice and the Yes/No question stand in
/// the bottom strip's middle box (the original's common question box is not drawn here, F0). ARC-UI-067: the selected save's info
/// display over the preview (its texts, its portrait from <see cref="PortraitSource"/>).
/// </summary>
public partial class AreaSaveListPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _header = null!;
    private ItemList _rows = null!;
    private LineEdit _description = null!;
    private Label _detail = null!;
    private TextureRect _preview = null!;
    private Label _notice = null!;
    private Button _confirm = null!;
    private Button _delete = null!;
    private Button _close = null!;
    private HBoxContainer _question = null!;
    private Label _hint = null!;
    // ARC-UI-2968: in the original's look the selected save's details, the notice and the Yes/No question are drawn with the original's
    // text font (art 229) and the original's dialog box, on the plate (children of the art view, so on its scale); the Labels and
    // Buttons above only hold the state (and are the diagnostic window's).
    private AreaArtParagraph _detailArt = null!, _noticeArt = null!;
    private AreaDialogBoxView _dialog = null!;
    private AreaTextFonts? _fonts;
    private static readonly (byte R, byte G, byte B) DetailColour = (219, 204, 173), NoticeColour = (255, 153, 102);
    private const int BoxLeft = 208, BoxTop = AreaSaveListArtLayout.BottomStripTop + 49, BoxWidth = 384, BoxHeight = AreaSaveListArtLayout.ScreenHeight - BoxTop - 3;
    private HBoxContainer _buttons = null!;
    private Control _body = null!;
    private Control _contents = null!;
    private AreaSaveListArtView _art = null!;
    private SteamwovenColumnSidesView _sides = null!;
    private StyleBoxFlat _panelStyle = null!;
    private string _newSaveLabel = AreaSaveListArtContent.ProjectNewSaveLabel;
    private IReadOnlyDictionary<int, string> _storyTexts = new Dictionary<int, string>();
    private bool _binding;
    private bool _artLook;
    private (string Name, DateTime Saved)? _previewOf;
    public event Action<int>? RowRequested;
    public event Action<int>? RowActivated;
    public event Action? ConfirmRequested;
    public event Action? DeleteRequested;
    public event Action<bool>? AnswerRequested;
    public event Action? CloseRequested;
    public event Action<string>? DescriptionChanged;
    /// <summary>ARC-UI-062: an art button's interface sound (GB7).</summary>
    public event Action<int>? SoundRequested;
    public bool IsOpen => Visible;
    /// <summary>ARC-UI-067: a portrait id's small image (null when it does not read).</summary>
    public Func<int, Texture2D?>? PortraitSource { get; set; }
    /// <summary>True while the window shows the original's look.</summary>
    public bool ArtMode => IsOpen && _art.Visible;
    internal AreaSaveListArtView ArtForSelfTest => _art;
    /// <summary>ARC-UI-2891: for the self-test: the column's surroundings of the list.</summary>
    internal SteamwovenColumnSidesView ColumnSidesForSelfTest => _sides;
    internal string ArtDetailForSelfTest => _art.Visible ? _detailArt.Text : "";
    /// <summary>ARC-UI-2968: the notice as the original's look shows it (the dialog box's text while a question is open).</summary>
    internal string ArtNoticeForSelfTest => !_art.Visible ? "" : _dialog.Visible ? _dialog.TextForSelfTest : _noticeArt.Text;
    internal AreaDialogBoxView DialogForSelfTest => _dialog;
    internal AreaArtParagraph DetailArtForSelfTest => _detailArt;
    internal AreaArtParagraph NoticeArtForSelfTest => _noticeArt;
    /// <summary>True while the description field has the keyboard (typed keys go to it, not to the list).</summary>
    public bool TypingDescription => IsOpen && _description.Visible && _description.HasFocus();
    internal IReadOnlyList<string> RowsForSelfTest => Enumerable.Range(0, _rows.ItemCount).Select(_rows.GetItemText).ToArray();
    internal bool PreviewShownForSelfTest => _preview.Texture is not null;
    internal string NoticeForSelfTest => _notice.Text;
    internal void EmitRowActivatedForSelfTest(int row) => RowActivated?.Invoke(row);

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaSaveList", MouseFilter = Control.MouseFilterEnum.Stop };
        _panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .97f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        };
        _panel.AddThemeStyleboxOverride("panel", _panelStyle);
        // ARC-UI-2891: with the column option the list's 800 × 600 screen fills the window's height and the rest is black.
        _sides = new SteamwovenColumnSidesView();
        AddChild(_sides);
        _art = new AreaSaveListArtView();
        _art.RowPressed += row => RowRequested?.Invoke(row);
        _art.ButtonPressed += button =>
        {
            switch (button)
            {
                case AreaSaveListArtButton.Confirm: ConfirmRequested?.Invoke(); break;
                case AreaSaveListArtButton.Delete: DeleteRequested?.Invoke(); break;
                default: CloseRequested?.Invoke(); break;
            }
        };
        _art.SoundRequested += id => SoundRequested?.Invoke(id);
        _detailArt = new AreaArtParagraph { Name = "SaveDetailArt", Position = new(BoxLeft, BoxTop), WrapWidth = BoxWidth, Room = BoxHeight };
        _noticeArt = new AreaArtParagraph { Name = "SaveNoticeArt", Position = new(BoxLeft, BoxTop), WrapWidth = BoxWidth, Room = BoxHeight };
        _dialog = new AreaDialogBoxView();
        _dialog.AnswerRequested += yes => AnswerRequested?.Invoke(yes);
        _art.AddChild(_detailArt); _art.AddChild(_noticeArt); _art.AddChild(_dialog);
        AddChild(_art);
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        _contents = contents;
        _header = new Label { Modulate = new Color(.2f, .12f, .05f) };
        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _body = body;
        _rows = new ItemList
        {
            Name = "Saves", SelectMode = ItemList.SelectModeEnum.Single, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(360, 300),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _rows.ItemSelected += index => { if (!_binding) RowRequested?.Invoke((int)index); };
        _rows.ItemActivated += index => { if (!_binding) RowActivated?.Invoke((int)index); };
        var side = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
        _preview = new TextureRect { Name = "Preview", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(200, 150) };
        _detail = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.1f, .1f, .1f), CustomMinimumSize = new Vector2(220, 0) };
        side.AddChild(_preview); side.AddChild(_detail);
        body.AddChild(_rows); body.AddChild(side);
        _description = new LineEdit { Name = "Description", PlaceholderText = "description (empty: the slot name)", MaxLength = 80, FocusMode = Control.FocusModeEnum.All };
        _description.TextChanged += text => { if (!_binding) DescriptionChanged?.Invoke(text); };
        var buttons = new HBoxContainer();
        _buttons = buttons;
        _confirm = new Button { Name = "Confirm", FocusMode = Control.FocusModeEnum.None };
        _confirm.Pressed += () => ConfirmRequested?.Invoke();
        _delete = new Button { Name = "Delete", Text = "Delete", FocusMode = Control.FocusModeEnum.None };
        _delete.Pressed += () => DeleteRequested?.Invoke();
        _close = new Button { Name = "Close", Text = "Close", FocusMode = Control.FocusModeEnum.None };
        _close.Pressed += () => CloseRequested?.Invoke();
        foreach (var button in new[] { _confirm, _delete, _close }) buttons.AddChild(button);
        _question = new HBoxContainer();
        var yes = new Button { Name = "Yes", Text = "Yes", FocusMode = Control.FocusModeEnum.None };
        yes.Pressed += () => AnswerRequested?.Invoke(true);
        var no = new Button { Name = "No", Text = "No", FocusMode = Control.FocusModeEnum.None };
        no.Pressed += () => AnswerRequested?.Invoke(false);
        _question.AddChild(yes); _question.AddChild(no);
        _notice = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.45f, .1f, .05f) };
        _hint = new Label
        {
            Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Up/Down select, Return saves or loads, Delete or Backspace deletes a selected save (asks first), Escape closes; the description " +
                "is typed on the new-save row | PROJECT window over the original's " +
                "save list rules (auto-save first, newest first, auto-save not deletable); original art and layout NOT_VERIFIED",
        };
        foreach (var control in new Control[] { _header, body, _description, buttons, _question, _notice, _hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    public void Open(AreaSaveList list, byte[]? preview)
    {
        Show();
        _binding = true; _description.Text = list.Description; _binding = false;
        Render(list, preview);
        if (list.Mode == AreaSaveListMode.Load) GetViewport().GuiReleaseFocus();
    }

    /// <summary>ARC-UI-062: the save window's art (frames by number), its font and the new-save row's label; ARC-UI-067: the info
    /// display's texts, the date's order and the story texts.</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaArtFontFace font, string newSaveLabel, AreaSaveInfoMessages messages,
        AreaSaveDateOrder dateOrder, IReadOnlyDictionary<int, string> storyTexts)
    {
        _art.SetArt(art, font, messages, dateOrder);
        _fonts = new AreaTextFonts(font, font);
        _detailArt.SetFonts(_fonts); _noticeArt.SetFonts(_fonts);
        _newSaveLabel = newSaveLabel ?? throw new ArgumentNullException(nameof(newSaveLabel));
        _storyTexts = storyTexts ?? throw new ArgumentNullException(nameof(storyTexts));
    }

    /// <summary>ARC-UI-2968: the original's dialog box art for the overwrite and delete questions; without it the question is a line of the box and the keys answer.</summary>
    public void SetDialogArt(AreaDialogBoxArt art) => _dialog.SetArt(art);

    public void CloseView()
    {
        Hide(); _preview.Texture = null; _previewOf = null; _art.CloseView(); _sides.Clear(); _dialog.Close();
        GetViewport().GuiReleaseFocus();
    }

    /// <summary>The window for the list's state; <paramref name="preview"/> is the selected save's PNG (null: none).</summary>
    public void Render(AreaSaveList list, byte[]? preview)
    {
        ArgumentNullException.ThrowIfNull(list);
        _binding = true;
        var rows = list.Rows();
        _rows.Clear();
        foreach (var row in rows) _rows.AddItem(row.Title);
        if (list.Selected >= 0 && list.Selected < rows.Count) { _rows.Select(list.Selected); _rows.EnsureCurrentIsVisible(); }
        else _rows.DeselectAll();
        _binding = false;
        var save = list.Mode == AreaSaveListMode.Save;
        _header.Text = (save ? "Save game" : "Load game") + $" | {list.Entries.Count} save{(list.Entries.Count == 1 ? "" : "s")}";
        _description.Visible = save;
        // The field has the keyboard on the new-save row only (an overwrite keeps the listed save's description), so on a listed
        // save Delete and Backspace delete.
        _description.Editable = save && list.Selected == 0;
        if (save && list.Selected == 0 && list.Prompt == AreaSavePrompt.None) _description.GrabFocus();
        else if (_description.HasFocus()) _description.ReleaseFocus();
        _confirm.Text = save ? "Save" : "Load";
        var entry = list.SelectedEntry;
        _detail.Text = entry is null ? save ? "A new numbered save of the game as it is now." : "" : AreaSaveList.Detail(entry);
        _delete.Disabled = entry is null || entry.IsAuto;
        var asking = list.Prompt != AreaSavePrompt.None;
        _question.Visible = asking && !_art.ArtSet;
        _confirm.Disabled = list.Prompt != AreaSavePrompt.None || (!save && entry is null);
        _notice.Text = list.Notice ?? "";
        ShowPreview(entry is null ? null : (entry.Name, entry.SavedAtUtc), preview);
        SetLook(_art.ArtSet);
        if (_art.ArtSet)
        {
            // The rows show the descriptions only, as the original's (the slot name's description part, 0x543040); the new-save row
            // shows the typed description, or "New" (5055) before any is typed (0x543BAB).
            var info = entry is null ? null : AreaSaveListArtLayout.Info(entry, _storyTexts);
            _art.Render(list.Mode, [.. rows.Select(row => row.Entry is { } listed ? listed.DisplayDescription : NewSaveRow(list.Description))],
                list.Selected, _preview.Texture, list.Prompt != AreaSavePrompt.None, info,
                info is { Portrait: > 0 } ? PortraitSource?.Invoke(info.Portrait) : null, unread: entry is not null && info is null);
            ShowArtTexts(asking);
        }
        else { _dialog.Close(); _detailArt.SetText("", DetailColour); _noticeArt.SetText("", NoticeColour); }
        ResizePanel();
    }

    /// <summary>The box's texts in the original's font: the details, then the notice under them; a question is the dialog box (the box is small, so the details wait).</summary>
    private void ShowArtTexts(bool asking)
    {
        var notice = _notice.Text;
        var boxed = asking && _dialog.HasArt;
        if (boxed) _dialog.Show(notice, AreaDialogBoxKind.YesNo, AreaDialogBoxLayout.QuitPoint); else _dialog.Close();
        _detailArt.SetText(asking ? "" : _detail.Text, DetailColour);
        _noticeArt.SetText(boxed ? "" : notice, NoticeColour);
        var used = _fonts is { } fonts ? _detailArt.LineCount * fonts.TextLineHeight : 0;
        _noticeArt.Position = new(BoxLeft, BoxTop + used);
        _noticeArt.Room = Math.Max(1, BoxHeight - used);
    }

    private void ShowPreview((string, DateTime)? of, byte[]? png)
    {
        if (Equals(of, _previewOf)) return;
        _previewOf = of; _preview.Texture = null;
        if (png is null) return;
        var image = new Image();
        if (image.LoadPngFromBuffer(png) == Error.Ok && !image.IsEmpty()) _preview.Texture = ImageTexture.CreateFromImage(image);
    }

    /// <summary>The new-save row's text: the typed description, or the label before any (only spaces count as none, as the save's
    /// own rule).</summary>
    public string NewSaveRow(string description) => string.IsNullOrWhiteSpace(description) ? _newSaveLabel : description;

    /// <summary>ARC-UI-062: the art is dropped (the diagnostic window shows again).</summary>
    public void ClearArt() { _art.ClearArt(); SetLook(false); }

    /// <summary>The diagnostic window, or the original's look with the panel shrunk to the bottom strip's middle box (PROJECT).</summary>
    private void SetLook(bool art)
    {
        // ARC-UI-2968: the diagnostic look (no art) is Godot's default font, so it is developer-only (AreaFallbackLook): transparent in a player's run, its state and keys unchanged.
        _panel.Modulate = art || AreaFallbackLook.Allowed ? Colors.White : new Color(1, 1, 1, 0);
        if (_artLook == art) return;
        _artLook = art;
        if (!art) _art.CloseView();
        foreach (var control in new Control[] { _header, _body, _buttons, _hint }) control.Visible = !art;
        _panelStyle.BgColor = art ? new Color(0, 0, 0, 0) : new Color(232 / 255f, 222 / 255f, 196 / 255f, .97f);
        (_panelStyle.ContentMarginLeft, _panelStyle.ContentMarginRight, _panelStyle.ContentMarginTop, _panelStyle.ContentMarginBottom) = art ? (14, 14, 6, 4) : (16, 16, 12, 12);
        _panel.ClipContents = art;
        _contents.MouseFilter = art ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Pass;
        _notice.Visible = !art;
        _notice.Modulate = new Color(.45f, .1f, .05f);
        // The field keeps the keyboard in the original's look but is not seen: its text is drawn in the new-save row.
        _description.Modulate = art ? new Color(1, 1, 1, 0) : Colors.White;
        _description.MouseFilter = art ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
        if (art) _description.AddThemeFontSizeOverride("font_size", 1); // ui-text-scale: hidden (a transparent keyboard field, not read text)
        else _description.RemoveThemeFontSizeOverride("font_size");
        // The box lets the pointer through to the art outside its Yes/No buttons.
        _panel.MouseFilter = art ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
    }

    private void ResizePanel()
    {
        if (_panel is null) return;
        if (_art.Visible)
        {
            _art.Place();
            if (AreaGameBarPanel.ColumnMenuPlan(GetViewport().GetVisibleRect().Size) is { } column) _sides.Present(column);
            else _sides.Clear();
            _panel.Position = _art.Position + new Vector2(200, AreaSaveListArtLayout.BottomStripTop + 47) * _art.Scale;
            _panel.Size = new Vector2(400, AreaSaveListArtLayout.BottomStripHeight - 47) * _art.Scale;
            return;
        }
        _sides.Clear();
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(900, size.X - 48));
        var height = Math.Max(1, Math.Min(640, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
