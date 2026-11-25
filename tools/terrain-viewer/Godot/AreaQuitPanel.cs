using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-DEV-2270: the quit question a close request asks (Cmd+Q on macOS, Alt+F4, the window's close button): the original's yes/no
/// dialog box with message 5100 of <c>mes\MainMenu.mes</c> (ledger SF3, DB1: the original's window procedure turns WM_CLOSE into the
/// quit question), over a dimmed window that takes every click. Y or N (either case), Return (yes) and Escape (no) answer; every other
/// key is swallowed while it asks. Without the game's dialog art (a launcher before a game folder, the self-test) the box is the
/// diagnostic parchment box with the same text and two buttons. The app owns what an answer does.
/// </summary>
public partial class AreaQuitPanel : CanvasLayer
{
    private ColorRect _veil = null!;
    private Control _screen = null!;
    private AreaDialogBoxView _dialog = null!;
    private PanelContainer _box = null!;
    private Label _question = null!;

    public event Action<bool>? AnswerRequested;
    /// <summary>True while the box asks.</summary>
    public bool Asking => Visible;
    internal AreaDialogBoxView DialogForSelfTest => _dialog;
    internal bool InArt => Visible && _dialog.Visible;
    internal string QuestionForSelfTest => _dialog.Visible ? _dialog.TextForSelfTest : _box.Visible ? _question.Text : "";
    /// <summary>The real answer wiring of a click (the diagnostic box's button, or the art box's pressed button).</summary>
    internal void PressForSelfTest(bool yes)
    {
        if (_box.Visible) ((Button)_box.FindChild(yes ? "Yes" : "No", owned: false)!).EmitSignal(BaseButton.SignalName.Pressed);
        else AnswerRequested?.Invoke(yes);
    }

    public override void _Ready()
    {
        // Above the bug report window (20) and everything else of the viewer.
        Layer = 22;
        Visible = false;
        _veil = new ColorRect { Name = "QuitVeil", Color = new Color(0, 0, 0, .45f), MouseFilter = Control.MouseFilterEnum.Stop };
        _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_veil);
        _screen = new Control { Name = "QuitScreen", MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(800, 600) };
        AddChild(_screen);
        _dialog = new AreaDialogBoxView();
        _screen.AddChild(_dialog);
        _dialog.AnswerRequested += yes => AnswerRequested?.Invoke(yes);
        _box = new AreaFallbackBox { Name = "QuitBox", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(232 / 255f, 222 / 255f, 196 / 255f, .97f),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        AddChild(_box);
        var contents = new VBoxContainer();
        _box.AddChild(contents);
        _question = new Label { Modulate = new Color(.2f, .12f, .05f), HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        contents.AddChild(_question);
        foreach (var (name, text, yes) in new[] { ("Yes", "Yes", true), ("No", "No", false) })
        {
            var answer = new Button { Name = name, Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(280, 34) };
            answer.Pressed += () => AnswerRequested?.Invoke(yes);
            contents.AddChild(answer);
        }
    }

    /// <summary>The original's dialog box art; without it the box is the diagnostic one.</summary>
    public void SetDialogArt(AreaDialogBoxArt art) => _dialog.SetArt(art);

    /// <summary>Shows the question (again) with <paramref name="text"/>.</summary>
    public void Ask(string text)
    {
        Visible = true;
        if (_dialog.HasArt)
        {
            _box.Visible = false;
            _dialog.Show(text, AreaDialogBoxKind.YesNo, AreaDialogBoxLayout.QuitPoint);
        }
        else
        {
            _dialog.Close();
            _question.Text = text;
            _box.Visible = true;
        }
        Place();
    }

    public void Dismiss() { _dialog.Close(); _box.Visible = false; Visible = false; }

    private void Place()
    {
        var visible = GetViewport().GetVisibleRect().Size;
        if (_dialog.Visible)
        {
            // The 800 × 600 screen of the original scaled to the window keeping its aspect, centred (as the death screen's box).
            var factor = Math.Min(visible.X / 800f, visible.Y / 600f);
            _screen.Scale = new Vector2(factor, factor);
            _screen.Position = new Vector2(MathF.Round((visible.X - 800 * factor) / 2), MathF.Round((visible.Y - 600 * factor) / 2));
        }
        if (!_box.Visible) return;
        var width = Math.Max(1, Math.Min(480, visible.X - 48));
        _box.Size = new(width, 0);
        _box.Position = new((visible.X - width) / 2, Math.Max(24, (visible.Y - _box.GetCombinedMinimumSize().Y) / 2));
    }

    public override void _Process(double delta) { if (Visible) Place(); }

    // While it asks it owns the keyboard: Y or Return answers yes, N or Escape no, every other key is swallowed so nothing behind acts.
    public override void _Input(InputEvent input)
    {
        if (!Visible) return;
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            // Either code: a non-Latin layout types other letters, its physical Y and N keys still answer.
            bool Is(Key wanted) => key.Keycode == wanted || key.PhysicalKeycode == wanted;
            // A Cmd/Ctrl chord is the quit key itself (a repeat of it is the close request's business), not an answer.
            if (!key.CtrlPressed && !key.MetaPressed && !key.AltPressed)
            {
                if (Is(Key.Y) || Is(Key.Enter) || Is(Key.KpEnter)) AnswerRequested?.Invoke(true);
                else if (Is(Key.N) || Is(Key.Escape)) AnswerRequested?.Invoke(false);
            }
        }
        if (input is InputEventKey or InputEventJoypadButton) GetViewport().SetInputAsHandled();
    }
}
