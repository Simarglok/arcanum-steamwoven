using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-WORLD-046 follower portraits: one button per follower in the PC's array (the original's portrait list; waiting NPCs have none,
/// OL16), each opening the order menu of the original's portrait (walk, attack, stay close, spread out, back off, wait, inventory,
/// character sheet; wait is left out for a mind-controlled follower). Inventory and character sheet are shown disabled (not
/// modelled for followers). A diagnostic strip: no portrait art.
/// </summary>
public partial class AreaFollowerPortraits : CanvasLayer
{
    private VBoxContainer _list = null!;
    private PopupMenu _menu = null!;
    private string? _menuEntity;
    private string _signature = "";

    /// <summary>A menu item chosen for a follower: its entity and the item.</summary>
    public event Action<string, FollowerMenuItem>? ItemChosen;

    internal IReadOnlyList<string> EntitiesForSelfTest { get; private set; } = [];

    public override void _Ready()
    {
        Layer = 3;
        var panel = new PanelContainer { Name = "FollowerPortraits", MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .85f),
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        AddChild(panel);
        _list = new VBoxContainer(); panel.AddChild(_list);
        _menu = new PopupMenu { Name = "FollowerOrders" };
        AddChild(_menu);
        _menu.IdPressed += id => { if (_menuEntity is { } entity) ItemChosen?.Invoke(entity, (FollowerMenuItem)(int)id); };
        GetViewport().SizeChanged += Place;
        Hide();
    }

    /// <summary>Shows one button per follower (entity, label) in array order; hidden when there is none.</summary>
    public void Show(IReadOnlyList<(string Entity, string Label)> followers)
    {
        var signature = string.Join("\n", followers.Select(f => f.Entity + "\t" + f.Label));
        if (signature == _signature) return;
        _signature = signature;
        foreach (var child in _list.GetChildren()) child.QueueFree();
        EntitiesForSelfTest = followers.Select(f => f.Entity).ToArray();
        foreach (var (entity, label) in followers)
        {
            var button = new Button { Text = label + " ▸", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => OpenMenu(entity, button);
            _list.AddChild(button);
        }
        Visible = followers.Count > 0;
        Place();
    }

    /// <summary>Opens the order menu of a follower (also the self-test's entry).</summary>
    public void OpenMenu(string entity, Control? anchor = null, bool mindControlled = false)
    {
        _menuEntity = entity;
        _menu.Clear();
        foreach (var item in Enum.GetValues<FollowerMenuItem>())
        {
            if (item == FollowerMenuItem.Wait && CompanionOrderRules.PortraitOrder(item, mindControlled) is null) continue;
            _menu.AddItem(AreaCompanionOrders.Label(item), (int)item);
            if (item is FollowerMenuItem.Inventory or FollowerMenuItem.CharacterSheet) _menu.SetItemDisabled(_menu.ItemCount - 1, true);
        }
        var at = anchor is null ? new Vector2I(24, 24) : (Vector2I)(anchor.GetScreenPosition() + new Vector2(anchor.Size.X, 0));
        _menu.Popup(new Rect2I(at, Vector2I.Zero));
    }

    public void CloseMenu() { _menu.Hide(); _menuEntity = null; }

    private void Place()
    {
        if (_list?.GetParent() is not PanelContainer panel) return;
        var size = GetViewport().GetVisibleRect().Size;
        panel.ResetSize();
        panel.Position = new(12, Math.Max(12, size.Y - panel.GetCombinedMinimumSize().Y - 12));
    }
}
