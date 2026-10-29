using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-026 the hero's death screen: the whole window black, a slide's picture centred and scaled to fit (the original's
/// slides are 800 × 600), and a black veil whose opacity makes the fades (ledger PD7). It sits above the main menu and the bars, so
/// the menu fades in under it after the reset. The app drives it: the veil moves only by <see cref="Advance"/>, with the same
/// time as the death's state, and the input goes to the app.
/// ARC-UI-028 (Steamwoven): the box that asks whether to load the hero's newest save; its buttons take clicks, the keys go to the app.
/// ARC-WORLD-120: drawn as the original's dialog box (<see cref="AreaDialogBoxView"/>, DB1–DB4) over the slide (or the black screen)
/// at the quit question's point of the 800 × 600 screen, which is fitted to the window as the slide's picture is (PROJECT: the slide's
/// scale, so the box sits where the original puts it over the slide). Without the game's art (the synthetic self-tests) the box is the
/// diagnostic parchment box with its question, save, Yes and No.
/// </summary>
public partial class AreaDeathPanel : CanvasLayer
{
    private ColorRect _back = null!;
    private TextureRect _picture = null!;
    private ColorRect _veil = null!;
    private PanelContainer _box = null!;
    private Label _question = null!, _save = null!, _notice = null!, _hint = null!;
    private Control _screen = null!;
    private AreaDialogBoxView _dialog = null!;
    private float _from, _to;
    private double _fade, _elapsed;

    public bool IsOpen => Visible;
    /// <summary>The veil's opacity: 1 black, 0 clear.</summary>
    public float Veil => _veil.Color.A;
    public bool PictureShown => _picture.Texture is not null;
    /// <summary>ARC-UI-028: the box's Yes (true) or No (false), pressed with the mouse.</summary>
    public event Action<bool>? AnswerRequested;
    public bool Asking => Visible && (_box.Visible || _dialog.Visible);
    /// <summary>ARC-WORLD-120: true while the box is the original's dialog box.</summary>
    public bool AsksInArt => Visible && _dialog.Visible;
    internal AreaDialogBoxView DialogForSelfTest => _dialog;
    internal string QuestionForSelfTest => _box.Visible ? _question.Text : "";
    internal string SaveForSelfTest => _box.Visible ? _save.Text : "";
    internal string NoticeForSelfTest => _box.Visible ? _notice.Text : "";
    internal IReadOnlyList<string> ButtonsForSelfTest => _box.Visible ? _box.FindChildren("*", nameof(Button), owned: false).OfType<Button>().Select(button => button.Text).ToArray() : [];
    /// <summary>Emits the real button's pressed signal (the wiring a click goes through).</summary>
    internal void PressForSelfTest(bool yes) => ((Button)_box.FindChild(yes ? "Yes" : "No", owned: false)!).EmitSignal(BaseButton.SignalName.Pressed);
    internal Vector2? ButtonCentreForSelfTest(bool yes) => _dialog.Visible ? _dialog.ButtonCentreForSelfTest(yes)
        : _box.Visible && _box.FindChild(yes ? "Yes" : "No", owned: false) is Button button ? button.GetGlobalRect().GetCenter() : null;
    internal Vector2I? PictureSizeForSelfTest => _picture.Texture is { } texture ? new(texture.GetWidth(), texture.GetHeight()) : null;

    public override void _Ready()
    {
        Layer = 7;
        Visible = false;
        _back = new ColorRect { Name = "DeathBack", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop };
        _back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_back);
        _picture = new TextureRect
        {
            Name = "DeathSlide", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        _picture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_picture);
        _veil = new ColorRect { Name = "DeathVeil", Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_veil);
        // The quit box's look (AreaMainMenuPanel): the parchment panel, a dark question, Yes and No.
        _box = new PanelContainer { Name = "DeathLoadBox", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .97f),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        AddChild(_box);
        var contents = new VBoxContainer();
        _box.AddChild(contents);
        _question = new Label { Modulate = new Color(.2f, .12f, .05f), HorizontalAlignment = HorizontalAlignment.Center };
        _save = new Label { Modulate = new Color(.1f, .1f, .1f), HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        contents.AddChild(_question); contents.AddChild(_save);
        foreach (var (name, yes) in new[] { ("Yes", true), ("No", false) })
        {
            var answer = new Button { Name = name, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(280, 34) };
            answer.Pressed += () => AnswerRequested?.Invoke(yes);
            contents.AddChild(answer);
        }
        _notice = new Label { Modulate = new Color(.45f, .1f, .05f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _hint = new Label { Modulate = new Color(.3f, .3f, .3f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        contents.AddChild(_notice); contents.AddChild(_hint);
        // ARC-WORLD-120: the original's dialog box on the fitted 800 × 600 screen.
        _screen = new Control { Name = "DeathScreen", MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(800, 600) };
        AddChild(_screen);
        _dialog = new AreaDialogBoxView();
        _screen.AddChild(_dialog);
        _dialog.AnswerRequested += yes => AnswerRequested?.Invoke(yes);
    }

    /// <summary>ARC-WORLD-120: the dialog box's art; without it the box is the diagnostic one.</summary>
    public void SetDialogArt(AreaDialogBoxArt art) => _dialog.SetArt(art);

    /// <summary>ARC-WORLD-120: shows the original's dialog box (again) with <paramref name="text"/>; false without its art.</summary>
    public bool ShowDialog(string text)
    {
        if (!_dialog.HasArt) return false;
        _box.Visible = false;
        _dialog.Show(text, AreaDialogBoxKind.YesNo, AreaDialogBoxLayout.QuitPoint);
        PlaceBox();
        return true;
    }

    /// <summary>ARC-UI-028: shows the box (again) with its texts; <paramref name="notice"/> is a refused load's reason or empty.</summary>
    public void ShowChoice(string question, string save, string yes, string no, string notice, string hint)
    {
        _question.Text = question; _save.Text = save; _notice.Text = notice; _hint.Text = hint;
        ((Button)_box.FindChild("Yes", owned: false)!).Text = yes;
        ((Button)_box.FindChild("No", owned: false)!).Text = no;
        _dialog.Close();
        _box.Visible = true;
        PlaceBox();
    }

    public void HideChoice() { _box.Visible = false; _dialog.Close(); }

    private void PlaceBox()
    {
        if (_dialog.Visible)
        {
            // The slide's fit: the 800 × 600 screen scaled to the window keeping its aspect, centred.
            var visible = GetViewport().GetVisibleRect().Size;
            var factor = Math.Min(visible.X / 800f, visible.Y / 600f);
            _screen.Scale = new Vector2(factor, factor);
            _screen.Position = new Vector2(MathF.Round((visible.X - 800 * factor) / 2), MathF.Round((visible.Y - 600 * factor) / 2));
        }
        if (!_box.Visible) return;
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(560, size.X - 48));
        _box.Size = new(width, 0);
        _box.Position = new((size.X - width) / 2, Math.Max(24, (size.Y - _box.GetCombinedMinimumSize().Y) / 2));
    }

    public override void _Process(double delta) { if (Visible) PlaceBox(); }

    /// <summary>Opens over the game with the veil clear (the first fade then darkens it).</summary>
    public void Open()
    {
        if (Visible) return;
        _picture.Texture = null;
        // The game shows through until the first fade: only the back and the picture are hidden.
        _back.Visible = false;
        SetVeil(0); _from = _to = 0; _fade = 0;
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
        _picture.Texture = null;
        _box.Visible = false;
        _dialog.Close();
    }

    /// <summary>Starts a fade of the veil to black or to clear over <paramref name="milliseconds"/>.</summary>
    public void Fade(bool toBlack, int milliseconds)
    {
        _from = _veil.Color.A; _to = toBlack ? 1 : 0;
        _fade = Math.Max(1, milliseconds); _elapsed = 0;
        // A fade in without a picture is the main menu's after the reset: the back lets the new scene through under the veil.
        if (!toBlack && _picture.Texture is null) _back.Visible = false;
    }

    /// <summary>The picture stays black until it fades in; null clears it.</summary>
    public void ShowPicture(Texture2D? texture) => _picture.Texture = texture;

    /// <summary>Moves the fade on by <paramref name="milliseconds"/>.</summary>
    public void Advance(double milliseconds)
    {
        if (_fade <= 0) return;
        _elapsed = Math.Min(_fade, _elapsed + Math.Max(0, milliseconds));
        SetVeil((float)(_from + (_to - _from) * (_elapsed / _fade)));
        if (_elapsed < _fade) return;
        _fade = 0;
        // Once black the game is not shown again until the main menu: the black back stays under the pictures.
        if (_to >= 1) _back.Visible = true;
    }

    private void SetVeil(float alpha) => _veil.Color = new Color(0, 0, 0, Math.Clamp(alpha, 0, 1));
}
