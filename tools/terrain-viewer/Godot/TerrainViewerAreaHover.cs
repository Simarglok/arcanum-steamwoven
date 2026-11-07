using Arcanum.Data.Area;
using Arcanum.Data.Messages;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-005: in crash-site-area the pointer names the object under it and the cursor shows what a left click would
// do there. One pick serves the tooltip, the talk click and the cave-use click: the frontmost drawn body with an opaque
// pixel under the pointer, skipping OF_CLICK_THROUGH bodies (the source's target pick 0x43D9F0). The action mirrors WorldRouteClick's
// guards and order (talk, use, ground) without running any of them. Everything is recomputed only when the pointer,
// the raster transform, the map, the actor cell or the guard state changes, so an idle frame costs one comparison.
// The original's main-mode cursor does not change per target; the action cursor is project policy, shown as system cursor shapes
// only while the original's cursor art (ARC-UI-056) is not read.
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
    // ARC-DEV-156 (B27): the tooltip's text in the original's font art (text font 229) while the fonts read; the label otherwise.
    private AreaArtTextBlock? _hoverArt;
    // ARC-UI-020: the hovered critter's health and fatigue bars under its name.
    private AreaHoverLifeBars? _hoverBars;
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
        _hoverArt = new AreaArtTextBlock { Name = "HoverArtText", FitContent = true, Visible = false };
        var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hoverBars = new AreaHoverLifeBars();
        lines.AddChild(_hoverLabel); lines.AddChild(_hoverArt); lines.AddChild(_hoverBars);
        _hoverPanel.AddChild(lines); _hoverLayer.AddChild(_hoverPanel); AddChild(_hoverLayer);
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
            _hoverIndex = new(area.Snapshot.Objects, area.Plan.Commands, _hoverNames?.Catalog, _companionTemplates.Select(t => t.Speaker.Row))
            {
                // ARC-DEV-172: an NPC the hero has met is named by its own description, read from the live session at every hover.
                Recognition = row => AreaHoverIndex.RecognitionFrom(row, entity => _areaSession?.QueryReactionRecord(entity)),
            };
            // ARC-RENDER-033: the placed items drawn under keys of their own.
            foreach (var row in _groundOnly.Values) _hoverIndex.Admit(row.Row);
            _hoverIndexArea = area; _hoverStamp = null;
        }
        return _hoverIndex;
    }

    /// <summary>The frontmost pickable object with an opaque drawn pixel under the screen point. A wall drawn faded
    /// (ARC-RENDER-011) is see-through for the pick, as the original skips walls with OWAF_TRANS_LEFT/RIGHT.</summary>
    /// <param name="roofs">False only for the roof smoke, to find a body under a roof.</param>
    /// <param name="spared">Objects the pick sees through besides the click-through ones (the spell cursor's, ARC-MAGIC-021).</param>
    private AreaObjectKey? AreaObjectAtScreen(Vector2 screen, bool roofs = true, Func<AreaObjectKey, bool>? spared = null)
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
            && _canvas?.AreaWallFadeOf(command) is null or AreaWallFade.None
            && spared?.Invoke(command.Identity!.Value) != true, AreaMovingCommands());
    }

    /// <summary>Per-frame entry from _Process: cheap unless something the decision reads has changed.</summary>
    private void UpdateAreaHover()
    {
        TrackHoverWindow();
        if (_exiting || _readyArea is null || _readyMode != TerrainViewerMode.Area || _canvas is null)
        {
            if (_hoverPanel?.Visible == true || _hoverCursor != Godot.Input.CursorShape.Arrow) ShowAreaHover(null, AreaHoverAction.None, null);
            return;
        }
        AdoptHoverNames();
        var index = HoverIndex()!;
        var screen = _hoverPointerForSelfTest ?? GetViewport().GetMousePosition();
        var overGui = _hoverPointerForSelfTest is not null ? _hoverOverGuiForSelfTest : GetViewport().GuiGetHoveredControl() is not null;
        // ARC-DEV-060: an automated run's pointer is only the one its test places; the system pointer resting inside the window is
        // never over the world (the GUI's side: AutomatedPointerGate; a headless run or a self-test's bootstrap app has no gate).
        var overWorld = _pointerInWindow && (_hoverPointerForSelfTest is not null || !AutomatedVisualRun) && !overGui
            && GetViewport().GetVisibleRect().HasPoint(screen);
        // A modal surface or lost focus hides the tooltip; the other click guards (closed gate, lifecycle, pending
        // transfer) keep the name but show no action, as the click would be ignored.
        var visible = overWorld && IsWorld && _worldInitialized && _worldFocused && !WorldModal;
        var open = visible && WorldClickInputOpen && !AreaTransferPending;
        var stamp = (screen, AreaDrawItem.RasterTransform(GetViewport()), index, visible, open,
            _worldActor.Session is AreaGridSession session ? session.State.CommittedCell : (Arcanum.Simulation.Movement.CellCoord?)null,
            _inventoryHidden, _areaCompanions?.Companions.FirstOrDefault()?.Grid.State, _areaCompanions,
            (HeroInCombatMode, LeftAltHeld, Godot.Input.IsKeyPressed(Key.Alt), _areaSession?.Version.Revision, _areaSession?.Rules.Mode, _gameBarPanel?.Visible == true),
            (_spellTargeting?.Spell, _spellTargeting?.Mask));
        if (Equals(stamp, _hoverStamp)) return;
        _hoverStamp = stamp;
        // ARC-MAGIC-021: the open spell cursor names what its pick sees, through the objects it spares.
        var key = visible ? AreaObjectAtScreen(screen, spared: SpellPickSpared()) : null;
        var action = open ? HoverAction(screen, key) : AreaHoverAction.None;
        ShowAreaHover(key, action, screen);
    }

    /// <summary>What WorldRouteClick would do for a press here (talk, then use, then ground), given open guards.</summary>
    private AreaHoverAction HoverAction(Vector2 screen, AreaObjectKey? key)
    {
        if (_worldActor.Session is not AreaGridSession session || _canvas is null) return AreaHoverAction.None;
        // ARC-MAGIC-015: the targeting cursor answers for every point.
        if (_spellTargeting is not null) return SpellTargetAt(screen) is { Test.Passes: true } ? AreaHoverAction.Cast : AreaHoverAction.CastNoTarget;
        var actor = session.Chart.ToWorld(session.State.CommittedCell);
        if (key is { } hit)
        {
            // ARC-COMBAT-005: in combat mode a living NPC is attacked; a corpse is looted only with Alt, a follower attacked only
            // with left Alt (as WorldRouteClick decides).
            if (AttackMapActive && HeroInCombatMode && AreaTargetOf(hit) is { } foe)
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
            if (!ConversationOpen && TalkMapActive && PresentSpeaker(hit) is { } speaker)
                return AreaConversationHost.Distance(speaker, actor.X, actor.Y, SpeakerCell) < Arcanum.Simulation.Ai.AiExecutorRules.HeroTalkRange ? AreaHoverAction.Talk : AreaHoverAction.WalkToTalk; // ARC-DEV-169: the hero talks from less than 5 cells (goal 21 node 4)
            if (IsAreaTrigger(hit))
            {
                var row = _hoverIndex!.Row(hit);
                var distance = Math.Max(Math.Abs(row.WorldX!.Value - actor.X), Math.Abs(row.WorldY!.Value - actor.Y));
                return distance <= AreaTransitionCoordinator.UseRange ? AreaHoverAction.Use : AreaHoverAction.WalkToUse;
            }
            if (InventoryActive && AreaInventoryTargetOf(hit) is { } target)
            {
                // ARC-DEV-175: the hero picks an item up standing on its tile (goal 15 walks with range 0); a body or a container is looted from the next cell (goal 81, range 1).
                var near = Chebyshev(target.Row, (actor.X, actor.Y)) <= (target.Kind == AreaInventoryTargetKind.GroundItem ? 0 : AreaInventoryHost.Reach);
                return target.Kind == AreaInventoryTargetKind.GroundItem
                    ? near ? AreaHoverAction.PickUp : AreaHoverAction.WalkToPickUp
                    : near ? AreaHoverAction.Loot : AreaHoverAction.WalkToLoot;
            }
        }
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is not { } cell) return AreaHoverAction.None;
        if (!ReferenceEquals(_hoverRoutesSession, session)) { _hoverRoutes = new AreaRouteSource(session.Snapshot, session.Chart); _hoverRoutesSession = session; }
        // As WorldRoute's follower: a goal beyond the local reach is a walk (its corridor, ARC-WORLD-042), not under the
        // Original rule mode (ARC-WORLD-072).
        return AreaHover.Ground(_hoverRoutes!, session.State.CommittedCell, cell, _areaSession?.Rules.Navigation.FarClickDoesNotMove == true);
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
            AreaHoverAction.Cast => Godot.Input.CursorShape.Cross,
            AreaHoverAction.CastNoTarget => Godot.Input.CursorShape.Forbidden,
            _ => Godot.Input.CursorShape.Arrow,
        };
        // ARC-UI-056: while the original's cursor art is shown these shapes are not (it does not change per target).
        if (cursor != _hoverCursor) { _hoverCursor = cursor; if (_cursorShown is null) SetCursorShape(cursor); }
        // ARC-UI-057: the hovered portal, container, scenery or item is drawn once more, additive (0x43C690).
        if (_canvas is not null) _canvas.AreaHighlight = key is { } lit && _hoverIndex is { } rows && rows.Contains(lit) && AreaHoverWindow.Highlighted(rows.Row(lit)) ? lit : null;
        // ARC-UI-057: with the interface bars drawn the object is described in the message window, as the original does; the floating
        // tooltip is the viewer's without the bars.
        var inBars = _gameBarPanel is { Visible: true };
        if (_hoverPanel is not null && _hoverLabel is not null)
        {
            if (text is null || screen is not { } at || inBars) _hoverPanel.Visible = false;
            else
            {
                _hoverLabel.Text = text;
                SetHoverText(text);
                _hoverBars?.Show(HoverLife(key!.Value));
                _hoverPanel.ResetSize();
                // Below-right of the pointer, flipped inside the viewport so the text never covers the pointed spot.
                var size = _hoverPanel.GetCombinedMinimumSize(); var view = GetViewport().GetVisibleRect().Size;
                var x = at.X + 16 + size.X > view.X ? at.X - 8 - size.X : at.X + 16;
                var y = at.Y + 20 + size.Y > view.Y ? at.Y - 8 - size.Y : at.Y + 20;
                _hoverPanel.Position = new Vector2(Mathf.Round(Math.Max(0, x)), Mathf.Round(Math.Max(0, y)));
                _hoverPanel.Visible = true;
            }
        }
        // ARC-UI-024, ARC-UI-057: the hovered object's window (a follower's portrait among it) in the message window while the bars are drawn.
        SyncHoverWindow(key);
        if (changed && refreshStatus) RefreshAreaStatus();
    }

    /// <summary>ARC-DEV-156: the tooltip's text in the original's font art when it read (the label hides), else in the label.</summary>
    private void SetHoverText(string text)
    {
        var fonts = TextFonts();
        _hoverLabel!.Visible = fonts is null;
        _hoverArt!.Visible = fonts is not null;
        if (fonts is null) return;
        var (placed, _, _) = AreaTextFontLayout.Block(fonts.Text.Font, fonts.TextLineHeight, text);
        _hoverArt.Set([new(fonts.Text, fonts.TextLineHeight, placed.Lines, 0, 0, (255, 255, 255))]);
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
