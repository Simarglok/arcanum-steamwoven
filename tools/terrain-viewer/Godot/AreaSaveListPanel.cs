using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-010 save and load window: the save list of <see cref="AreaSaveList"/> as a scrolled list (the auto slot first, then newest
/// first), the selected save's details and preview, a description field (save mode), and Save/Load, Delete and Close buttons; a
/// question (overwrite, delete) shows Yes and No. The app owns the state and rebuilds the window after every change.
/// ARC-UI-062: with the game's art (<see cref="SetArt"/>) the window is drawn in the original's look (<see cref="AreaSaveListArtView"/>)
/// and the description is typed into the new-save row; PROJECT: the selected save's details, a notice and the Yes/No question stand in
/// the bottom strip's middle box (the original's common question box and the info display over the preview are not drawn here, F0).
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
    private Label _artDetail = null!;
    private HBoxContainer _buttons = null!;
    private Control _body = null!;
    private AreaSaveListArtView _art = null!;
    private StyleBoxFlat _panelStyle = null!;
    private string _newSaveLabel = AreaSaveListArtContent.ProjectNewSaveLabel;
    private bool _binding;
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
    /// <summary>True while the window shows the original's look.</summary>
    public bool ArtMode => IsOpen && _art.Visible;
    internal AreaSaveListArtView ArtForSelfTest => _art;
    internal string ArtDetailForSelfTest => _artDetail.Visible ? _artDetail.Text : "";
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
        AddChild(_art);
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
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
        _artDetail = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(.86f, .8f, .68f), Visible = false };
        foreach (var control in new Control[] { _header, body, _artDetail, _description, buttons, _question, _notice, _hint }) contents.AddChild(control);
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

    /// <summary>ARC-UI-062: the save window's art (frames by number), its font and glyphs, and the new-save row's label.</summary>
    public void SetArt(IReadOnlyDictionary<int, Texture2D[]> art, AreaArtFont font, IReadOnlyDictionary<int, Texture2D> glyphs, string newSaveLabel)
    {
        _art.SetArt(art, font, glyphs);
        _newSaveLabel = newSaveLabel ?? throw new ArgumentNullException(nameof(newSaveLabel));
    }

    public void CloseView()
    {
        Hide(); _preview.Texture = null; _previewOf = null; _art.CloseView();
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
        _question.Visible = list.Prompt != AreaSavePrompt.None;
        _confirm.Disabled = list.Prompt != AreaSavePrompt.None || (!save && entry is null);
        _notice.Text = list.Notice ?? "";
        ShowPreview(entry is null ? null : (entry.Name, entry.SavedAtUtc), preview);
        SetLook(_art.ArtSet);
        if (_art.ArtSet)
        {
            // The rows show the descriptions only, as the original's (the slot name's description part, 0x543040); the new-save row
            // shows the typed description, or "New" (5055) before any is typed (0x543BAB).
            _art.Render(list.Mode, [.. rows.Select(row => row.Entry is { } listed ? listed.DisplayDescription : list.Description is { Length: > 0 } typed ? typed : _newSaveLabel)],
                list.Selected, _preview.Texture);
            _artDetail.Text = _detail.Text;
        }
        ResizePanel();
    }

    private void ShowPreview((string, DateTime)? of, byte[]? png)
    {
        if (Equals(of, _previewOf)) return;
        _previewOf = of; _preview.Texture = null;
        if (png is null) return;
        var image = new Image();
        if (image.LoadPngFromBuffer(png) == Error.Ok && !image.IsEmpty()) _preview.Texture = ImageTexture.CreateFromImage(image);
    }

    /// <summary>The diagnostic window, or the original's look with the panel shrunk to the bottom strip's middle box (PROJECT).</summary>
    private void SetLook(bool art)
    {
        if (_art.Visible == art && _artDetail.Visible == art) return;
        if (!art) _art.CloseView();
        foreach (var control in new Control[] { _header, _body, _buttons, _hint }) control.Visible = !art;
        _artDetail.Visible = art;
        _panelStyle.BgColor = art ? new Color(0, 0, 0, 0) : new Color(232 / 255f, 222 / 255f, 196 / 255f, .97f);
        _notice.Modulate = art ? new Color(1f, .6f, .4f) : new Color(.45f, .1f, .05f);
        // The field keeps the keyboard in the original's look but is not seen: its text is drawn in the new-save row.
        _description.Modulate = art ? new Color(1, 1, 1, 0) : Colors.White;
        _description.MouseFilter = art ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
        if (art) _description.AddThemeFontSizeOverride("font_size", 1);
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
            _panel.Position = _art.Position + new Vector2(200, AreaSaveListArtLayout.BottomStripTop + 47) * _art.Scale;
            _panel.Size = new Vector2(400, AreaSaveListArtLayout.BottomStripHeight - 47) * _art.Scale;
            return;
        }
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(900, size.X - 48));
        var height = Math.Max(1, Math.Min(640, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
