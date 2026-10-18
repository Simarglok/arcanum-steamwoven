using Arcanum.Data.Area;
using Arcanum.Data.Messages;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-005: in crash-site-area the pointer names the object under it and the cursor shows what a left click would
// do there. One pick serves the tooltip, the talk click and the cave-use click: the frontmost drawn body with an opaque
// pixel under the pointer, skipping OF_CLICK_THROUGH bodies (the source's target pick 0x43D9F0). The action mirrors WorldRouteClick's
// guards and order (talk, use, ground) without running any of them. Everything is recomputed only when the pointer,
// the raster transform, the map, the actor cell or the guard state changes, so an idle frame costs one comparison.
// The original's main-mode cursor does not change per target; the action cursor is project policy.
public partial class TerrainViewerApp
{
    private Task<AreaNamesLoad>? _hoverNamesTask;
    private string? _hoverNamesModule;
    private AreaNamesLoad? _hoverNames;
    private AreaHoverIndex? _hoverIndex;
    private AreaLoadResult? _hoverIndexArea;
    private AreaRouteSource? _hoverRoutes;
    private object? _hoverRoutesSession;
    private CanvasLayer? _hoverLayer;
    private PanelContainer? _hoverPanel;
    private Label? _hoverLabel;
    private object? _hoverStamp;
    private AreaHoverAction _hoverAction;
    private AreaObjectKey? _hoverKey;
    private string? _hoverText;
    private Godot.Input.CursorShape _hoverCursor = Godot.Input.CursorShape.Arrow;
    private bool _pointerInWindow = true;
    private Vector2? _hoverPointerForSelfTest;
    private bool _hoverOverGuiForSelfTest;

    internal AreaHoverAction HoverActionForSelfTest => _hoverAction;
    internal string? HoverTextForSelfTest => _hoverPanel?.Visible == true ? _hoverText : null;
    internal Godot.Input.CursorShape HoverCursorForSelfTest => _hoverCursor;
    internal AreaObjectKey? HoverKeyForSelfTest => _hoverKey;

    /// <summary>Starts loading the description tables for this module once; a reload or the cave reuses them.</summary>
    private void BindAreaHover(TerrainViewerArguments arguments)
    {
        if (_hoverNamesModule != arguments.ModulePath)
        {
            _hoverNamesModule = arguments.ModulePath; _hoverNames = null;
            var module = arguments.ModulePath;
            _hoverNamesTask = Task.Run(() => AreaHover.LoadNames(module));
        }
        _hoverIndex = null; _hoverIndexArea = null; _hoverStamp = null;
        if (_hoverLayer is not null) return;
        _hoverLayer = new CanvasLayer { Name = "AreaHover", Layer = 3 };
        _hoverPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _hoverPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .9f),
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        _hoverLabel = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hoverPanel.AddChild(_hoverLabel); _hoverLayer.AddChild(_hoverPanel); AddChild(_hoverLayer);
    }

    /// <summary>Hides the tooltip and gives the default cursor back; the loaded names stay for the next publication.</summary>
    private void UnbindAreaHover()
    {
        _hoverIndex = null; _hoverIndexArea = null; _hoverRoutes = null; _hoverRoutesSession = null;
        ShowAreaHover(null, AreaHoverAction.None, null, refreshStatus: false);
        _hoverStamp = null;
    }

    private void AdoptHoverNames()
    {
        if (_hoverNames is not null || _hoverNamesTask is not { IsCompleted: true } task) return;
        _hoverNames = task.IsCompletedSuccessfully ? task.Result
            : new(AreaNamesStatus.StackFailed, null, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _hoverNamesTask = null; _hoverIndex = null;
        GD.Print($"ARC-UI-005 names status={_hoverNames.Status} detail={_hoverNames.Detail}");
    }

    private AreaHoverIndex? HoverIndex()
    {
        if (_readyArea is not { } area) return null;
        if (_hoverIndex is null || !ReferenceEquals(_hoverIndexArea, area))
        {
            // ARC-WORLD-017: a companion from another map is named and picked where it is drawn here.
            _hoverIndex = new(area.Snapshot.Objects, area.Plan.Commands, _hoverNames?.Catalog, _companionTemplates.Select(t => t.Speaker.Row));
            _hoverIndexArea = area; _hoverStamp = null;
        }
        return _hoverIndex;
    }

    /// <summary>The frontmost pickable object with an opaque drawn pixel under the screen point. A wall drawn faded
    /// (ARC-RENDER-011) is see-through for the pick, as the original skips walls with OWAF_TRANS_LEFT/RIGHT.</summary>
    /// <param name="roofs">False only for the roof smoke, to find a body under a roof.</param>
    private AreaObjectKey? AreaObjectAtScreen(Vector2 screen, bool roofs = true)
    {
        if (_readyArea is not { } area || HoverIndex() is not { } index) return null;
        // ARC-RENDER-022: a drawn roof pixel picks nothing (RF10's hit test); the pick does not test the roof cover.
        if (roofs && AreaRoofHit(screen)) return null;
        var transform = AreaDrawItem.RasterTransform(GetViewport());
        var sx = (int)Math.Floor(screen.X); var sy = (int)Math.Floor(screen.Y);
        // ARC-UI-004: an item the PC took is no longer in the world.
        // ARC-WORLD-016: a companion is picked where it is drawn now, never on its T0 cell.
        // ARC-WORLD-017: a companion standing on another map is not picked on its T0 cell here.
        return index.Pick(command => !AreaTakenByPc(command) && _areaCompanions?.Absent.Contains(command.Identity!.Value) != true
            && AreaAlphaAt(command, AreaFrames(), transform, sx, sy) != 0
            && _canvas?.AreaWallFadeOf(command) is null or AreaWallFade.None, AreaMovingCommands());
    }

    /// <summary>Per-frame entry from _Process: cheap unless something the decision reads has changed.</summary>
    private void UpdateAreaHover()
    {
        if (_exiting || _readyArea is null || _readyMode != TerrainViewerMode.CrashSiteArea || _canvas is null)
        {
            if (_hoverPanel?.Visible == true || _hoverCursor != Godot.Input.CursorShape.Arrow) ShowAreaHover(null, AreaHoverAction.None, null);
            return;
        }
        AdoptHoverNames();
        var index = HoverIndex()!;
        var screen = _hoverPointerForSelfTest ?? GetViewport().GetMousePosition();
        var overGui = _hoverPointerForSelfTest is not null ? _hoverOverGuiForSelfTest : GetViewport().GuiGetHoveredControl() is not null;
        var overWorld = _pointerInWindow && !overGui && GetViewport().GetVisibleRect().HasPoint(screen);
        // A modal surface or lost focus hides the tooltip; the other click guards (closed gate, lifecycle, pending
        // transfer) keep the name but show no action, as the click would be ignored.
        var visible = overWorld && IsWorld && _worldInitialized && _worldFocused && !WorldModal;
        var open = visible && WorldClickInputOpen && !AreaTransferPending;
        var stamp = (screen, AreaDrawItem.RasterTransform(GetViewport()), index, visible, open,
            _worldActor.Session is AreaGridSession session ? session.State.CommittedCell : (Arcanum.Simulation.Movement.CellCoord?)null,
            _inventoryHidden, _areaCompanions?.Companions.FirstOrDefault()?.Grid.State, _areaCompanions,
            (HeroInCombatMode, LeftAltHeld, Godot.Input.IsKeyPressed(Key.Alt), _areaSession?.Version.Revision));
        if (Equals(stamp, _hoverStamp)) return;
        _hoverStamp = stamp;
        var key = visible ? AreaObjectAtScreen(screen) : null;
        var action = open ? HoverAction(screen, key) : AreaHoverAction.None;
        ShowAreaHover(key, action, screen);
    }

    /// <summary>What WorldRouteClick would do for a press here (talk, then use, then ground), given open guards.</summary>
    private AreaHoverAction HoverAction(Vector2 screen, AreaObjectKey? key)
    {
        if (_worldActor.Session is not AreaGridSession session || _canvas is null) return AreaHoverAction.None;
        var actor = session.Chart.ToWorld(session.State.CommittedCell);
        if (key is { } hit)
        {
            // ARC-COMBAT-005: in combat mode a living NPC is attacked; a corpse is looted only with Alt, a follower attacked only
            // with left Alt (as WorldRouteClick decides).
            if (AttackMapActive && HeroInCombatMode && _combatHost!.Target(_areaSession!, CurrentAreaMap, hit) is { } foe)
            {
                if (foe.Dead) return Godot.Input.IsKeyPressed(Key.Alt) && InventoryActive && _inventoryHost!.Target(_areaSession!, CurrentAreaMap, hit) is { } corpse
                    ? Chebyshev(corpse.Row, (actor.X, actor.Y)) <= AreaInventoryHost.Reach ? AreaHoverAction.Loot : AreaHoverAction.WalkToLoot
                    : AreaHoverAction.None;
                if (foe.Follower && !LeftAltHeld || IsAreaCompanion(hit) || foe.Build.Combat is null) return AreaHoverAction.None;
                if (_areaSession!.QueryHeroAttack() is not { Supported: true } hero || _areaSession.QueryHeroLife() is not { Status.IsActive: true })
                    return AreaHoverAction.None;
                var reach = hero.Range;
                return Chebyshev(foe.Row, (actor.X, actor.Y)) <= reach ? AreaHoverAction.Attack : AreaHoverAction.WalkToAttack;
            }
            if (!ConversationOpen && TalkMapActive && _conversationHost!.Speakers.FirstOrDefault(s => s.Row.Key == hit && SpeakerPresent(s)) is { } speaker)
                return AreaConversationHost.Distance(speaker, actor.X, actor.Y, SpeakerCell) <= _conversationHost.Profile.Reach ? AreaHoverAction.Talk : AreaHoverAction.WalkToTalk;
            if (IsAreaTrigger(hit))
            {
                var row = _hoverIndex!.Row(hit);
                var distance = Math.Max(Math.Abs(row.WorldX!.Value - actor.X), Math.Abs(row.WorldY!.Value - actor.Y));
                return distance <= AreaTransitionCoordinator.UseRange ? AreaHoverAction.Use : AreaHoverAction.WalkToUse;
            }
            if (InventoryActive && _inventoryHost!.Target(_areaSession!, CurrentAreaMap, hit) is { } target)
            {
                var near = Chebyshev(target.Row, (actor.X, actor.Y)) <= AreaInventoryHost.Reach;
                return target.Kind == AreaInventoryTargetKind.GroundItem
                    ? near ? AreaHoverAction.PickUp : AreaHoverAction.WalkToPickUp
                    : near ? AreaHoverAction.Loot : AreaHoverAction.WalkToLoot;
            }
        }
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is not { } cell) return AreaHoverAction.None;
        if (!ReferenceEquals(_hoverRoutesSession, session)) { _hoverRoutes = new AreaRouteSource(session.Snapshot, session.Chart); _hoverRoutesSession = session; }
        // WorldRoute's follower uses the default limits.
        return AreaHover.Ground(_hoverRoutes!, session.State.CommittedCell, cell);
    }

    private void ShowAreaHover(AreaObjectKey? key, AreaHoverAction action, Vector2? screen, bool refreshStatus = true)
    {
        var text = key is { } hit && _hoverIndex is { } index ? AreaHover.Tooltip(index.Name(hit), action) : null;
        var changed = key != _hoverKey || action != _hoverAction || text != _hoverText;
        _hoverKey = key; _hoverAction = action; _hoverText = text;
        var cursor = action switch
        {
            AreaHoverAction.Walk => Godot.Input.CursorShape.Cross,
            AreaHoverAction.NoRoute => Godot.Input.CursorShape.Forbidden,
            AreaHoverAction.Talk or AreaHoverAction.WalkToTalk => Godot.Input.CursorShape.Help,
            AreaHoverAction.Use or AreaHoverAction.WalkToUse or AreaHoverAction.PickUp or AreaHoverAction.WalkToPickUp
                or AreaHoverAction.Loot or AreaHoverAction.WalkToLoot => Godot.Input.CursorShape.PointingHand,
            AreaHoverAction.Attack or AreaHoverAction.WalkToAttack => Godot.Input.CursorShape.Drag,
            _ => Godot.Input.CursorShape.Arrow,
        };
        if (cursor != _hoverCursor) { _hoverCursor = cursor; Godot.Input.SetDefaultCursorShape(cursor); }
        if (_hoverPanel is not null && _hoverLabel is not null)
        {
            if (text is null || screen is not { } at) _hoverPanel.Visible = false;
            else
            {
                _hoverLabel.Text = text;
                _hoverPanel.ResetSize();
                // Below-right of the pointer, flipped inside the viewport so the text never covers the pointed spot.
                var size = _hoverPanel.GetCombinedMinimumSize(); var view = GetViewport().GetVisibleRect().Size;
                var x = at.X + 16 + size.X > view.X ? at.X - 8 - size.X : at.X + 16;
                var y = at.Y + 20 + size.Y > view.Y ? at.Y - 8 - size.Y : at.Y + 20;
                _hoverPanel.Position = new Vector2(Math.Max(0, x), Math.Max(0, y));
                _hoverPanel.Visible = true;
            }
        }
        if (changed && refreshStatus) RefreshAreaStatus();
    }

    private string AreaHoverStatus()
    {
        var names = _hoverNames is null ? " (names loading)" : _hoverNames.Status == AreaNamesStatus.Ready ? string.Empty : $" (names {_hoverNames.Status})";
        // A click can rebuild the index for a new map before the next hover update clears the old key.
        var name = _hoverKey is { } key && _hoverIndex is { } index && ReferenceEquals(_hoverIndexArea, _readyArea) && index.Contains(key)
            ? index.Name(key) ?? "unnamed" : "-";
        return $" | hover {name} -> {AreaHover.Label(_hoverAction)}{names}";
    }

    internal void HoverNamesForSelfTest(ObjectDescriptionCatalog catalog)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        _hoverNamesTask = null; _hoverNames = new(AreaNamesStatus.Ready, catalog, "synthetic");
        _hoverIndex = null; _hoverStamp = null;
    }

    /// <summary>Places the test pointer over the world, or (null) outside the window; <paramref name="overGui"/> stands
    /// for a native control (HUD, palette) under the pointer.</summary>
    internal void HoverAtForSelfTest(Vector2? screen, bool overGui = false)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        _hoverPointerForSelfTest = screen; _pointerInWindow = screen is not null; _hoverOverGuiForSelfTest = overGui; _hoverStamp = null;
        UpdateAreaHover();
    }

    internal void HoverReleaseForSelfTest() { _hoverPointerForSelfTest = null; _pointerInWindow = true; _hoverOverGuiForSelfTest = false; _hoverStamp = null; }
}
