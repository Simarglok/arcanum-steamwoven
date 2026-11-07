using Arcanum.Data.Combat;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Hotkeys;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-027: the original's hotkey bar on the bottom strip (docs/world/spell-cast-host.md#the-hotkey-bars-art-and-drag-arc-ui-027; ledger
// HK10–HK16). The view draws the session's entries with the game's art; a left click on a slot activates its entry (or drops the item the
// inventory window holds on it), a left click on a recent-action button activates that action, and a right press on a slot or on a spell
// of the spell pane (or a slot of the weapon's pane) starts a drag whose release decides with HotkeyBarRules.Drop: a move between entries,
// a put, or an entry dragged off the bar emptied. ARC-UI-032: an item entry shows its inventory art when it fits a slot, else its type
// icon (HK17, HK18); the slot shows the item's name only when neither reads.
public partial class TerrainViewerApp
{
    private AreaHotkeyBarView? _hotkeyBarView;
    private AreaHotkeyBarContent? _hotkeyBarContent;
    private string? _hotkeyBarModule;
    private IReadOnlyDictionary<int, ImageTexture[]> _hotkeyBarArt = new Dictionary<int, ImageTexture[]>();
    private AreaItemIcons? _hotkeyItemIcons;
    private readonly Dictionary<string, (ImageTexture[]? Icon, string Detail)> _hotkeyItemIconCache = new(StringComparer.Ordinal);
    // The session the cache was filled for: kit:n and made:n name other items in another session (a load, a new game).
    private GameSessionOwner? _hotkeyItemIconSession;
    private (HotkeyDragSourceKind Kind, int Entry, HotkeyEntry? Payload)? _hotkeyDrag;
    private (GameSessionOwner Session, GameSessionVersion Version, bool Dragging, bool Names)? _hotkeyBarShown;
    private int _hotkeyBarClicksForSelfTest;

    /// <summary>Per frame while the bars show: the bar's view on the bottom strip, its art read once a module, and its look redrawn when the
    /// session or the drag changed.</summary>
    private void SyncHotkeyBar()
    {
        if (_gameBarPanel is null || _areaSession is not { } session) return;
        if (_hotkeyBarView is null)
        {
            _hotkeyBarView = new AreaHotkeyBarView();
            _gameBarPanel.AddPart(AreaBarStrip.Bottom, _hotkeyBarView);
            _hotkeyBarView.SlotClicked += HotkeySlotClicked;
            _hotkeyBarView.SlotRightPressed += entry => StartHotkeyDrag(HotkeyDragSourceKind.Entry, entry, null);
            _hotkeyBarView.RecentPressed += _ => PlayBarSound(AreaGameBarLayout.PressSound, "recent action");
            _hotkeyBarView.RecentReleased += HotkeyRecentReleased;
            _hotkeyBarView.LightPressed += () => PlayBarSound(AreaGameBarLayout.PressSound, "hotkey light");
            // The light button has no function in 1.0.7.4 (HK14): its release over it only sounds.
            _hotkeyBarView.LightReleased += over => { if (over) PlayBarSound(AreaGameBarLayout.ReleaseSound, "hotkey light"); };
            _hotkeyBarView.DragReleased += HotkeyDragReleased;
            _hotkeyBarView.SlotEntered += HotkeySlotEntered;
            _hotkeyBarView.RecentEntered += HotkeyRecentEntered;
            _hotkeyBarView.SlotLeft += _ => HotkeyDescriptionLeft();
            _hotkeyBarView.RecentLeft += _ => HotkeyDescriptionLeft();
        }
        var module = _arguments?.ModulePath;
        if (_hotkeyBarContent is null || module != _hotkeyBarModule)
        {
            _hotkeyBarModule = module;
            _hotkeyBarContent = SelfTestBootstrap || module is null ? AreaHotkeyBarContent.Empty("self-test: no game folder") : AreaHotkeyBarContent.Load(module);
            GD.Print($"ARC-UI-027 hotkey bar art {_hotkeyBarContent.Detail}");
            _hotkeyBarArt = _hotkeyBarContent.Art.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready).ToDictionary(load => load.Number,
                load => load.Frames.Select(HotkeyTexture).ToArray());
            _hotkeyItemIcons?.Dispose();
            _hotkeyItemIcons = null;
            _hotkeyItemIconCache.Clear();
            if (!SelfTestBootstrap && module is not null)
            {
                _hotkeyItemIcons = AreaItemIcons.Open(module, out var detail);
                GD.Print($"ARC-UI-032 hotkey item icons {detail}");
            }
            _hotkeyBarShown = null;
            // ARC-UI-053: the spell pane draws the same art.
            _spellBarShown = null;
        }
        var shown = (session, session.Version, _hotkeyDrag is not null, InventoryActive);
        if (_hotkeyBarShown == shown) return;
        _hotkeyBarShown = shown;
        _hotkeyBarView.Bind(HotkeyBarLook(session));
    }

    /// <summary>The bars were hidden: a drag going on ends with no change (no release can reach them).</summary>
    private void HideHotkeyBar()
    {
        if (_hotkeyDrag is null) return;
        _hotkeyDrag = null; _hotkeyBarView?.SetDrag(false, null); _hotkeyBarShown = null;
    }

    private AreaHotkeyBarLook HotkeyBarLook(GameSessionOwner session)
    {
        var bar = session.QueryHotkeys();
        Dictionary<string, string>? names = null;
        string? Name(string item)
        {
            names ??= InventoryActive ? InventoryView(null).Pc.ToDictionary(line => line.Entity, line => line.Text, StringComparer.Ordinal) : [];
            return names.GetValueOrDefault(item) ?? item;
        }
        var slots = Enumerable.Range(0, HotkeyBarRules.Entries).Select(i =>
        {
            var entry = bar.Entries[i];
            var hidden = _hotkeyDrag is { Kind: HotkeyDragSourceKind.Entry } drag && drag.Entry == i;
            var count = session.QueryHotkeyCount(i) is { } n ? HotkeyBarRules.CountText(n) : null;
            return new AreaHotkeySlotLook(First(HotkeyIcon(entry)), entry is { Kind: HotkeyKind.Item, Item: { } item } ? Name(item) : null, count, hidden);
        }).ToArray();
        return new(First(Art(HotkeyBarRules.SlotArt)), slots, [.. bar.Recent.Select(HotkeyIcon)], First(Art(HotkeyBarRules.LightArt)));
    }

    private ImageTexture[]? Art(int number) => _hotkeyBarArt.GetValueOrDefault(number);

    private static ImageTexture HotkeyTexture(AreaInterfaceFrame frame) =>
        ImageTexture.CreateFromImage(Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Rgba));

    private static ImageTexture? First(ImageTexture[]? frames) => frames is { Length: > 0 } ? frames[0] : null;

    // An entry's icon (HK12): a spell's (also an item's spell), a skill-bar button's, an item's (ARC-UI-032).
    private ImageTexture[]? HotkeyIcon(HotkeyEntry entry) => entry.Kind switch
    {
        HotkeyKind.Spell or HotkeyKind.ItemSpell => HotkeyBarRules.SpellIcon(entry.Index) is { } spell ? Art(spell) : null,
        HotkeyKind.Skill => Art(HotkeyBarRules.SkillIcon(entry.Index)),
        HotkeyKind.Item when entry.Item is { } item => HotkeyItemIcon(item).Icon,
        _ => null,
    };

    /// <summary>ARC-UI-032: an item entry's icon (its inventory art up to 32 × 32, else its type icon, HK17, HK18), read once per item and
    /// redrawn when the session identifies it; null when the item's fields or art do not read.</summary>
    private (ImageTexture[]? Icon, string Detail) HotkeyItemIcon(string item)
    {
        if (_areaSession is not { } session || _hotkeyItemIcons is not { } icons || HeroItemKind(session, item) is not { } kind)
            return (null, "no item icons");
        if (!ReferenceEquals(_hotkeyItemIconSession, session)) { _hotkeyItemIconCache.Clear(); _hotkeyItemIconSession = session; }
        var identified = session.QueryItemIdentified(item);
        var key = item + (identified ? "|identified" : "");
        if (_hotkeyItemIconCache.TryGetValue(key, out var known)) return known;
        CombatFields? row = null;
        foreach (var map in _inventoryHost?.Maps ?? [])
            if (map.TryGetRow(item, out var found)) { row = CombatFieldReader.Read(found).Fields; break; }
        var icon = icons.Icon(kind, row, identified);
        (ImageTexture[]? Icon, string Detail) shown = icon.Status switch
        {
            AreaItemIconStatus.InventoryArt => ([HotkeyTexture(icon.Art!)], icon.Detail),
            AreaItemIconStatus.TypeIcon => (Art(icon.TypeIcon!.Value), icon.Detail),
            _ => (null, icon.Detail),
        };
        GD.Print($"ARC-UI-032 hotkey item-icon item={item} {icon.Status} {icon.Detail}");
        _hotkeyItemIconCache[key] = shown;
        return shown;
    }

    // The kind of an item the hero holds: a kit item, an item taken from a map or a made item.
    private static SessionItemKind? HeroItemKind(GameSessionOwner session, string item) =>
        session.QueryHeroKit().FirstOrDefault(held => held.Entity == item) is { Entity: not null } kit ? kit.Kind
        : session.QueryPcInventory().FirstOrDefault(held => held.Entity == item) is { Entity: not null } taken ? taken.Kind
        : session.QueryMadeItems().FirstOrDefault(held => held.Entity == item) is { Entity: not null } made ? made.Kind : null;

    private bool HotkeyItemHeld => _inventoryPanel?.Held is not null && InventoryOpen;

    /// <summary>A right press on a slot or a pane's button: the drag starts when nothing is dragged or held and the source holds something
    /// (0x57E5D0); the dragged entry's icon hides in its slot and follows the pointer.</summary>
    private void StartHotkeyDrag(HotkeyDragSourceKind kind, int entry, HotkeyEntry? payload)
    {
        // Only a shown bar can take the release that ends the drag.
        if (!HotkeysActive || _areaSession is not { } session || _hotkeyBarView is not { } view || !view.IsVisibleInTree()) return;
        var empty = kind == HotkeyDragSourceKind.Entry && session.QueryHotkeys().Entries[entry].IsEmpty;
        if (!HotkeyBarRules.StartsDrag(_hotkeyDrag is not null, HotkeyItemHeld, kind, empty)) return;
        _hotkeyDrag = (kind, entry, payload);
        var shown = kind == HotkeyDragSourceKind.Entry ? session.QueryHotkeys().Entries[entry] : payload!;
        view.SetDrag(true, First(HotkeyIcon(shown)));
        GD.Print($"ARC-UI-027 hotkey-drag start {kind} entry={entry} {Describe(shown)}");
    }

    private void HotkeyDragReleased(MouseButton button, Vector2I point)
    {
        // ARC-DEV-051: a release over the bug report's shade drops nothing (the bar view sees the mouse before the app).
        if (_hotkeyDrag is not { } drag || _areaSession is not { } session || BugReportOpen) return;
        var target = HotkeyBarRules.EntryAt(point.X, point.Y);
        var drop = HotkeyBarRules.Drop(drag.Kind, drag.Entry, target, button == MouseButton.Left, false);
        if (drop.Kind == HotkeyDropKind.Continue) return;
        _hotkeyDrag = null;
        _hotkeyBarView?.SetDrag(false, null);
        // ARC-UI-087 (HK15): an item entry dragged off the bar leaves its item on the game cursor (the entry itself is emptied, the fix of the original's lost hidden entry).
        var heldOff = drop.Kind == HotkeyDropKind.Clear && drag.Kind == HotkeyDragSourceKind.Entry && session.QueryHotkeys().Entries[drag.Entry] is { Kind: HotkeyKind.Item, Item: { } offItem } ? offItem : null;
        GameSessionMutationResult? result = drop.Kind switch
        {
            HotkeyDropKind.Move => session.MoveHotkey(session.Version, drop.Source, drop.Target),
            HotkeyDropKind.Put => session.AssignHotkey(session.Version, drop.Target, drag.Payload!),
            HotkeyDropKind.Clear => session.ClearHotkey(session.Version, drop.Source),
            _ => null,
        };
        _hotkeyNotice = $"hotkey drop {drop.Kind}{(target is { } at ? " on " + KeyName(at) : " off the bar")}{(result is { } done ? $": {done.Disposition} {done.Failure}" : "")}";
        if (heldOff is not null && result is { Disposition: GameSessionDisposition.Changed })
            _hotkeyNotice += HoldItemOnGameCursor(heldOff, "hotkey-drag") is { } why ? $"; {why}" : "; the item is on the cursor";
        GD.Print($"ARC-UI-027 hotkey-drop {drop.Kind} source={drop.Source} target={drop.Target} disposition={result?.Disposition.ToString() ?? "-"} failure={result?.Failure.ToString() ?? "-"}");
        _hotkeyBarShown = null;
        RefreshAreaStatus();
    }

    /// <summary>A left click on a slot (0x57DC60): the held item goes on the entry, otherwise the entry activates.</summary>
    private void HotkeySlotClicked(int entry)
    {
        if (!HotkeysActive || _areaSession is not { } session) return;
        _hotkeyBarClicksForSelfTest++;
        switch (HotkeyBarRules.ClickEntry(_hotkeyDrag is not null, HotkeyItemHeld, session.QueryHotkeys().Entries[entry].IsEmpty))
        {
            case HotkeyClickKind.DropHeld: DropHeldItemOnEntry(entry); break;
            case HotkeyClickKind.Activate: ActivateHotkey(session.QueryHotkeys().Entries[entry], KeyName(entry)); break;
            default: _hotkeyNotice = $"hotkey {KeyName(entry)}: {(_hotkeyDrag is not null ? "a drag goes on" : "empty")}"; RefreshAreaStatus(); break;
        }
    }

    /// <summary>A recent-action button came up: over it, its action activates (0x57DC60 → 0x54FCF0), whatever is dragged or held (the original
    /// tests neither there); an empty one does nothing.</summary>
    private void HotkeyRecentReleased(int index, bool over)
    {
        if (!over) return;
        PlayBarSound(AreaGameBarLayout.ReleaseSound, "recent action");
        if (!HotkeysActive || _areaSession is not { } session) return;
        _hotkeyBarClicksForSelfTest++;
        var action = session.QueryHotkeys().Recent[index];
        if (action.IsEmpty) { _hotkeyNotice = $"recent action {index + 1}: empty"; RefreshAreaStatus(); return; }
        ActivateHotkey(action, index == 0 ? "A" : "recent 2");
    }

    /// <summary>The spell pane's spell or the weapon pane's slot under a right press.</summary>
    private void HotkeyPaneDrag(int spell) => StartHotkeyDrag(HotkeyDragSourceKind.Spell, -1, HotkeyEntry.ForSpell(spell));

    private void HotkeyWeaponPaneDrag(int slot)
    {
        if (_itemPaneItem is not { } item || SpellCasterNow() is not { } caster || caster.Item(item) is not { } spells || slot < 0 || slot >= spells.Spells.Count) return;
        StartHotkeyDrag(HotkeyDragSourceKind.ItemSpell, -1, HotkeyEntry.ForItemSpell(item, spells.Spells[slot]));
    }
}
