using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-007 diagnostic character sheet: the session hero's values as the original sheet shows them
/// (<see cref="AreaCharacterSheet.Lines"/>), with a close button. ARC-UI-009 adds the skills page: the sixteen skills in the
/// original's four groups, each row with − and + buttons (<see cref="SkillRefundRequested"/>, <see cref="SkillSpendRequested"/>)
/// and a hover hint (the description and the key stat the next point needs). The app rebuilds it whenever the session changes.
/// </summary>
public partial class AreaCharacterSheetPanel : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _body = null!;
    private Label _hint = null!;
    private Button _close = null!;
    private AreaCharacterSheetView? _view;
    private AreaSkillsPageView? _skillsView;
    private Label _skillMessage = null!;
    private readonly List<(Button Minus, Label Text, Button Plus)> _skillRows = [];
    public event Action? CloseRequested;
    public event Action<int>? SkillSpendRequested;
    public event Action<int>? SkillRefundRequested;
    public bool IsOpen => Visible;
    internal AreaCharacterSheetView? ViewForSelfTest => _view;
    internal string TextForSelfTest => _body.Text;
    internal AreaSkillsPageView? SkillsForSelfTest => _skillsView;
    internal Button SkillPlusForSelfTest(int row) => _skillRows[row].Plus;
    internal Button SkillMinusForSelfTest(int row) => _skillRows[row].Minus;
    internal string SkillTextForSelfTest(int row) => _skillRows[row].Text.Text;
    internal string SkillHintForSelfTest(int row) => _skillRows[row].Text.TooltipText;
    internal string SkillMessageForSelfTest => _skillMessage.Text;

    public override void _Ready()
    {
        Layer = 4;
        _panel = new PanelContainer { Name = "AreaCharacterSheet", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .96f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        });
        AddChild(_panel);
        var contents = new VBoxContainer(); _panel.AddChild(contents);
        var header = new Label { Text = "Character sheet", Modulate = new Color(1f, .85f, .55f) };
        _body = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var skills = new GridContainer { Name = "Skills", Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        for (var group = 0; group < AreaSkillsPage.Rows / AreaSkillsPage.RowsPerGroup; group++)
        {
            var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            column.AddChild(new Label { Text = AreaSkillsPage.GroupLabels[group], Modulate = new Color(1f, .85f, .55f) });
            for (var i = 0; i < AreaSkillsPage.RowsPerGroup; i++)
            {
                var row = group * AreaSkillsPage.RowsPerGroup + i;
                var line = new HBoxContainer();
                var minus = new Button { Text = "−", FocusMode = Control.FocusModeEnum.All, TooltipText = "refund (down to the value when the sheet opened)" };
                var plus = new Button { Text = "+", FocusMode = Control.FocusModeEnum.All, TooltipText = "spend one unspent point" };
                var text = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart };
                minus.Pressed += () => SkillRefundRequested?.Invoke(row);
                plus.Pressed += () => SkillSpendRequested?.Invoke(row);
                line.AddChild(minus); line.AddChild(text); line.AddChild(plus);
                column.AddChild(line);
                _skillRows.Add((minus, text, plus));
            }
            skills.AddChild(column);
        }
        _skillMessage = new Label { Modulate = new Color(.85f, .9f, .95f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _close = new Button { Name = "Close", Text = "Close", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.All };
        _close.Pressed += () => CloseRequested?.Invoke();
        _hint = new Label
        {
            Modulate = new Color(.55f, .6f, .65f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "C, Escape or cancel closes | PROJECT diagnostic window: skill points only (no stat, tech discipline or spell buttons); original UI parity NOT_VERIFIED",
        };
        foreach (var control in new Control[] { header, _body, skills, _skillMessage, _close, _hint }) contents.AddChild(control);
        GetViewport().SizeChanged += ResizePanel;
        ResizePanel(); Hide();
    }

    public void Open(AreaCharacterSheetView view, AreaSkillsPageView? skills = null)
    {
        Show(); Render(view, skills);
        _close.GrabFocus();
    }

    public void CloseView()
    {
        _view = null; _skillsView = null; _skillMessage.Text = string.Empty; Hide();
        GetViewport().GuiReleaseFocus();
    }

    public void Render(AreaCharacterSheetView view, AreaSkillsPageView? skills = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view; _skillsView = skills;
        _body.Text = string.Join("\n", AreaCharacterSheet.Lines(view));
        for (var row = 0; row < _skillRows.Count; row++)
        {
            var (minus, text, plus) = _skillRows[row];
            var skill = skills?.Rows[row];
            text.Text = skill is null ? "" : AreaSkillsPage.Line(skill);
            // The original's hover hint: the name, the description and the key stat the next point needs.
            text.TooltipText = skill is null ? "" : string.Join("\n", new[] { skill.Name, skill.Description,
                skill.NextPointKeyStat == 0 ? null : $"{skill.KeyStat}: {skill.NextPointKeyStat}" }.Where(part => !string.IsNullOrEmpty(part)));
            minus.Disabled = plus.Disabled = skills is not { EditorOpen: true };
        }
        ResizePanel();
    }

    /// <summary>The message of the last + or − press (empty clears it).</summary>
    public void ShowSkillMessage(string message) => _skillMessage.Text = message;

    /// <summary>Confirm presses the close button (the skills page's buttons take the mouse or focus).</summary>
    public void Confirm() => _close.EmitSignal(BaseButton.SignalName.Pressed);

    private void ResizePanel()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        var width = Math.Max(1, Math.Min(960, size.X - 48));
        _panel.Size = new(width, 0);
        var height = Math.Min(_panel.GetCombinedMinimumSize().Y, Math.Max(1, size.Y - 48));
        _panel.Size = new(width, height);
        _panel.Position = new((size.X - width) / 2, Math.Max(24, (size.Y - height) / 2));
    }

    public override void _Process(double delta) { if (IsOpen) ResizePanel(); }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
