using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-WORLD-046 follower portraits: one button per follower in the PC's array (the original's portrait list; waiting NPCs have none,
/// OL16), each opening the order menu of the original's portrait (walk, attack, stay close, spread out, back off, wait, inventory,
/// character sheet; the rule leaves wait out for a mind-controlled follower, which the viewer does not model yet). Inventory and character sheet are shown disabled (not
/// modelled for followers). A diagnostic strip: no portrait art. The menu is an inline panel of buttons that take no focus, not a
/// popup window: an embedded popup takes the world window's focus, which quarantines the world and refuses the choice.
/// </summary>
public partial class AreaFollowerPortraits : CanvasLayer
{
    private VBoxContainer _list = null!;
    private PanelContainer _menu = null!;
    private VBoxContainer _menuItems = null!;
    private string? _menuEntity;
    private string _signature = "";

    /// <summary>A menu item chosen for a follower: its entity and the item.</summary>
    public event Action<string, FollowerMenuItem>? ItemChosen;

    internal IReadOnlyList<string> EntitiesForSelfTest { get; private set; } = [];
    private const string EntityMeta = "follower_entity";

    /// <summary>The screen centre of a follower's portrait button, or of an item of the open menu, for the native smoke's mouse
    /// presses (the layer has no transform).</summary>
    internal Vector2? PortraitCentreForSelfTest(string entity) =>
        _list.GetChildren().OfType<Button>().FirstOrDefault(b => b.HasMeta(EntityMeta) && (string)b.GetMeta(EntityMeta) == entity)?.GetGlobalRect().GetCenter();
    internal Vector2? MenuItemCentreForSelfTest(FollowerMenuItem item) =>
        _menuItems.GetChildren().OfType<Button>().FirstOrDefault(b => b.HasMeta(ItemMeta) && (int)b.GetMeta(ItemMeta) == (int)item)?.GetGlobalRect().GetCenter();

    /// <summary>Whether the order menu is shown.</summary>
    public bool MenuShown => _menu.Visible;

    private const string ItemMeta = "follower_menu_item";

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
        _menu = new PanelContainer { Name = "FollowerOrders", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _menu.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(22 / 255f, 25 / 255f, 34 / 255f, .95f),
            ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        AddChild(_menu);
        _menuItems = new VBoxContainer(); _menu.AddChild(_menuItems);
        GetViewport().SizeChanged += () => { CloseMenu(); Place(); };
        Hide();
    }

    /// <summary>Shows one button per follower (entity, label) in array order; hidden when there is none.</summary>
    public void Show(IReadOnlyList<(string Entity, string Label)> followers)
    {
        var signature = string.Join("\n", followers.Select(f => f.Entity + "\t" + f.Label));
        if (signature == _signature) return;
        _signature = signature;
        // Removed at once so the panel's minimum size no longer counts them when it is placed below.
        foreach (var child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
        EntitiesForSelfTest = followers.Select(f => f.Entity).ToArray();
        if (_menuEntity is { } open && !EntitiesForSelfTest.Contains(open)) CloseMenu();
        foreach (var (entity, label) in followers)
        {
            var button = new Button { Text = label + " ▸", Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => OpenMenu(entity, button);
            button.SetMeta(EntityMeta, entity);
            _list.AddChild(button);
        }
        Visible = followers.Count > 0;
        Place();
    }

    /// <summary>Opens the order menu of a follower next to its portrait button (a second press of the same portrait closes it).</summary>
    public void OpenMenu(string entity, Control? anchor = null, bool mindControlled = false)
    {
        if (_menu.Visible && _menuEntity == entity) { CloseMenu(); return; }
        _menuEntity = entity;
        foreach (var child in _menuItems.GetChildren()) { _menuItems.RemoveChild(child); child.QueueFree(); }
        foreach (var item in Enum.GetValues<FollowerMenuItem>())
        {
            if (item == FollowerMenuItem.Wait && CompanionOrderRules.PortraitOrder(item, mindControlled) is null) continue;
            var button = new Button
            {
                Text = AreaCompanionOrders.Label(item), Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None,
                Disabled = item is FollowerMenuItem.Inventory or FollowerMenuItem.CharacterSheet,
            };
            button.SetMeta(ItemMeta, (int)item);
            button.Pressed += () => Choose(item);
            _menuItems.AddChild(button);
        }
        _menu.ResetSize();
        var size = GetViewport().GetVisibleRect().Size;
        var wanted = anchor is null ? new Vector2(24, 24) : anchor.GetGlobalRect().Position + new Vector2(anchor.Size.X + 4, 0);
        var menuSize = _menu.GetCombinedMinimumSize();
        _menu.Position = new(Math.Min(wanted.X, Math.Max(0, size.X - menuSize.X)), Math.Clamp(wanted.Y, 0, Math.Max(0, size.Y - menuSize.Y)));
        _menu.Show();
    }

    private void Choose(FollowerMenuItem item)
    {
        var entity = _menuEntity;
        CloseMenu();
        if (entity is not null) ItemChosen?.Invoke(entity, item);
    }

    public void CloseMenu() { _menu.Hide(); _menuEntity = null; }

    private void Place()
    {
        if (_list?.GetParent() is not PanelContainer panel) return;
        var size = GetViewport().GetVisibleRect().Size;
        panel.ResetSize();
        panel.Position = new(12, Math.Max(12, size.Y - panel.GetCombinedMinimumSize().Y - 12 - AreaGameBarPanel.BottomInset));
    }
}
