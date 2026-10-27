using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-MAGIC-015: the AREA viewer's spell pane, the smallest bounded stand-in for the original's interface bar in its spell mode
/// (opened with M): one button per known spell, college by college, with its fatigue cost. A press activates the spell as the
/// original's pane does (0x57BC70). The pane is not a window: the world keeps its clicks, and its buttons take no focus. A
/// diagnostic panel: no original art.
/// </summary>
public partial class AreaSpellPane : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private HFlowContainer _buttons = null!;
    private IReadOnlyList<AreaSpellChoice> _shown = [];

    /// <summary>A spell button was pressed (the spell's number).</summary>
    public event Action<int>? Activated;

    public bool Shown => _panel.Visible;

    internal IReadOnlyList<AreaSpellChoice> ShownForSelfTest => _shown;

    public override void _Ready()
    {
        Layer = 2;
        _panel = new PanelContainer { Name = "SpellPane", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .88f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        AddChild(_panel);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; _panel.AddChild(column);
        _title = new Label { MouseFilter = Control.MouseFilterEnum.Ignore }; column.AddChild(_title);
        _buttons = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new(520, 0) }; column.AddChild(_buttons);
        GetViewport().SizeChanged += Place;
    }

    /// <summary>Shows the pane with <paramref name="spells"/> (rebuilt only when the list changed).</summary>
    public void Show(IReadOnlyList<AreaSpellChoice> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);
        if (!_panel.Visible || !_shown.SequenceEqual(spells))
        {
            _shown = spells;
            foreach (var child in _buttons.GetChildren()) child.QueueFree();
            _title.Text = spells.Count == 0 ? "Spells (M): no spell known" : "Spells (M): press a spell, then left-click a target; right click cancels";
            foreach (var choice in spells)
            {
                var spell = choice.Spell;
                var button = new Button
                {
                    Text = choice.Cost is { } cost ? $"{choice.Name} ({cost})" : choice.Name, FocusMode = Control.FocusModeEnum.None,
                    TooltipText = choice.College,
                };
                button.Pressed += () => Activated?.Invoke(spell);
                _buttons.AddChild(button);
            }
        }
        _panel.Show();
        Place();
    }

    public void HidePane() => _panel.Hide();

    internal void PressForSelfTest(int spell) => Activated?.Invoke(spell);

    private void Place()
    {
        if (_panel is null) return;
        var size = GetViewport().GetVisibleRect().Size;
        _panel.ResetSize();
        var panel = _panel.GetCombinedMinimumSize();
        _panel.Position = new(12, Math.Max(12, size.Y - panel.Y - 12));
    }
}
