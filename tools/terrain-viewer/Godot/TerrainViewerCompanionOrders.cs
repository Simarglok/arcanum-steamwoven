using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-046: follower orders in crash-site-area (exterior, cave and map mode). F1–F6 send the original's presets with the tile
// under the pointer (F6 per the active rules, D2); the follower portrait strip opens each follower's order menu (walk asks for a
// tile with the next left click). Orders go through the session; the HUD and the log name the outcomes and their line ranges.
// ARC-WORLD-117: the attack order's target is the highlighted object (F2: the one under the pointer at the release; the portrait's
// attack: the object of the next left press, its target cursor), and with the AI host each reached follower gets its AI pass.
public partial class TerrainViewerApp
{
    private AreaFollowerPortraits? _followerPortraits;
    // A portrait walk order waiting for its tile, or (ARC-WORLD-117) an attack order waiting for its object (the original's target
    // cursor); any non-modifier key, a modal window, focus loss, another menu choice or a rebuilt group drops it.
    private (string Entity, CompanionOrderType Type)? _pendingOrder;
    // The portraits' last state (session, revision, group, modal), so a frame with nothing new does not rebuild them.
    private (Arcanum.Session.GameSessionOwner? Session, long Revision, AreaCompanionGroup? Group, bool Modal, object? Names, object? Combat)? _portraitStamp;
    // ARC-DEV-337 (B52): the process frame from which a shown follower without life values is asked again (null: none is missing). The stamp holds no
    // value of the bars, so a portrait built before the follower's record resolved (a map change, a window of the main map read later) would stay empty
    // at full health; a follower the host never has values for is asked twice a second, not every frame.
    private ulong? _portraitLifeRetry;
    private const ulong PortraitLifeRetryFrames = 30;

    /// <summary>Orders are taken only while the world owns input: focused, no modal window or control focus, no transfer pending.</summary>
    private bool OrderInputOpen => _worldFocused && !PanelOutsideStackOpen && _interfaceWindows.FollowerOrderKeys(AnotherHasTheTurn) && !WorldUiFocus && !AreaTransferPending;

    // The F1–F6 key pressed while the world owned input; its release sends the preset (the original acts on key up).
    private WorldKey? _orderKeyArmed;

    /// <summary>F1–F6 without modifiers while the session party is wired: the presets, sent on the release of a key pressed while the
    /// world owned input, with the tile under the pointer at the release (the original's key up). A press or release while the world
    /// does not own input shows "ignored". Other keys, a modifier, or no party, are not orders.</summary>
    private bool RouteOrderKey(WorldKeyDecision decision, WorldKeyModifiers modifiers)
    {
        if (decision.Key is < WorldKey.F1 or > WorldKey.F6 || modifiers != WorldKeyModifiers.None) return false;
        if (_areaSession is not { } session || _areaCompanions is null || _conversationHost is not { PartyWired: true }) return false;
        if (decision.Echo) return true;
        var key = decision.Key.ToString();
        if (!OrderInputOpen)
        {
            _orderKeyArmed = null;
            _companionNotice = $"{key} ignored: the mode or a window owns the input, another has the turn, the world has no input focus or a map change is pending";
            RefreshReadyStatus();
            return true;
        }
        if (decision.Pressed) { _orderKeyArmed = decision.Key; return true; }
        if (_orderKeyArmed != decision.Key) return true;
        _orderKeyArmed = null;
        var preset = CompanionOrderRules.Preset(decision.Key - WorldKey.F1, session.Rules.Orders);
        if (!preset.All)
        {
            _companionNotice = $"{key}: no order (the original's preset names nobody)";
            GD.Print($"{AreaCompanionOrders.LogPrefix} order source={key} type={preset.Type} reached=0 (names nobody)");
            RefreshReadyStatus();
            return true;
        }
        // ARC-WORLD-117: the attack preset's target is the highlighted object at the release (0x43C570).
        IssueAreaOrder(key, new(preset.Type, CompanionOrderAddressing.All, [], Tile: PointerCell())
            { Target = preset.Type == CompanionOrderType.Attack ? OrderTarget(_hoverKey) : null });
        return true;
    }

    /// <summary>The chart cell under the pointer, or null off the map.</summary>
    private CellCoord? PointerCell()
    {
        if (_canvas is null) return null;
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition();
        return AreaCellPicking.Pick(new(chart.X, chart.Y));
    }

    /// <summary>ARC-WORLD-117: an object of the shown map as the attack order's target, or null.</summary>
    private SessionCombatant? OrderTarget(Arcanum.Data.Area.AreaObjectKey? key) =>
        _combatHost is { } combat ? AreaCompanionOrders.Target(combat, CurrentAreaMap, key) : null;

    /// <summary>Sends an order (its followers' facts are gathered here) and shows its outcomes. ARC-WORLD-117: through the shown map's
    /// AI host when it runs (the followers' combat facts, the attack event's world, each follower's AI pass).</summary>
    private SessionCompanionOrderResult? IssueAreaOrder(string source, CompanionOrderRequest request)
    {
        if (_areaSession is not { } session || _areaCompanions is not { } group || _conversationHost is not { } host
            || _worldActor.Session is not AreaGridSession pc)
        {
            _companionNotice = $"{source}: order not sent (no area session)";
            RefreshReadyStatus();
            return null;
        }
        var ai = AreaAiHostNow();
        // Only the combat orders read the combat facts (an invalid one refuses the whole order); without the host an attack order goes
        // without a target, as before ARC-WORLD-117 (a target would lift NO_ATTACK and fail for want of the attack event's world).
        Func<string, SessionPeaceFacts>? combat = ai is not null && request.Type is CompanionOrderType.Attack or CompanionOrderType.BackOff ? ai.OrderFacts : null;
        var facts = AreaCompanionOrders.Facts(session, group, pc.State.CommittedCell, host.State, CompanionName, combat);
        var (result, noGoal) = AreaCompanionOrders.Issue(session, group, request with { Followers = facts, Target = ai is null ? null : request.Target },
            pc.State.CommittedCell, WorldTime, ai);
        GD.Print(AreaCompanionOrders.Log(source, request.Type, result) + (noGoal.Count == 0 ? "" : " | goal not started: " + string.Join(", ", noGoal))
            + (request.Type == CompanionOrderType.Attack ? $" | target={request.Target?.Entity ?? "none"} ai={ai is not null}" : ""));
        _companionNotice = result.Failure != GameSessionFailure.None ? $"{source}: order refused ({result.Failure})"
            : result.Outcomes.Count == 0 ? $"{source}: {request.Type} reached no follower within {CompanionOrderRules.Reach} tiles"
            : $"{source}: " + string.Join("; ", result.Outcomes.Select(AreaCompanionOrders.Describe))
                + (noGoal.Count == 0 ? "" : $" (no goal started for {noGoal.Count}: tile off the map)");
        RefreshFollowerPortraits();
        RefreshReadyStatus();
        return result;
    }

    /// <summary>The name the PC knows a companion by: its hover name on the shown map, else its dialog script.</summary>
    private string CompanionName(AreaCompanion companion) =>
        _hoverIndex is { } index && ReferenceEquals(_hoverIndexArea, _readyArea) && index.Contains(companion.Key) && index.Name(companion.Key) is { Length: > 0 } name
            ? name : companion.Template.Created ? companion.Entity : $"script {companion.Speaker.ScriptId}";

    /// <summary>Shows a portrait per follower in the array (in its order) while the session party is wired; none while a modal window
    /// is open (its menu closes and a pending walk order is dropped). Rebuilt only when the session, its revision, the group or the
    /// modal state changed.</summary>
    private void RefreshFollowerPortraits()
    {
        RefreshHeroGauges();
        // ARC-UI-020: the combat host gives the portraits' bars their values.
        var stamp = (_areaSession, _areaSession?.Version.Revision ?? -1, _areaCompanions, WorldModal, (object?)_hoverIndex, (object?)_combatHost);
        if (_portraitStamp == stamp && (_portraitLifeRetry is not { } retry || Engine.GetProcessFrames() < retry)) return;
        _portraitStamp = stamp;
        _portraitLifeRetry = null;
        if (_areaSession is not { } session || _areaCompanions is not { } group || _conversationHost is not { PartyWired: true } || WorldModal)
        {
            _followerPortraits?.Show([]);
            _followerPortraits?.CloseMenu();
            if (TargetsDroppedByModal) { CancelPendingOrder("a window opened"); _orderKeyArmed = null; }
            return;
        }
        if (_followerPortraits is null)
        {
            _followerPortraits = new AreaFollowerPortraits(); AddChild(_followerPortraits);
            _followerPortraits.ItemChosen += FollowerMenuChosen;
            _followerPortraits.PortraitClicked += FollowerPortraitClick;
            // ARC-UI-024: the frames stand at the window's top left, where the diagnostic HUD starts.
            _followerPortraits.EdgeChanged += () => _hud?.SetLeftInset(_followerPortraits.RightEdge);
        }
        var shown = group.Companions.ToDictionary(c => c.Entity, StringComparer.Ordinal);
        var followers = session.QueryParty().Followers.Where(f => shown.ContainsKey(f.Entity)).Select(f => shown[f.Entity]).ToArray();
        // ARC-UI-024: the frames' art and each follower's portrait, read when a new one is needed.
        LoadFollowerPortraits(followers);
        // ARC-UI-020: each portrait's bars from the follower's life.
        var shownFollowers = followers.Select(c => (c.Entity, CompanionName(c), CompanionLife(c), FollowerPortrait(c))).ToArray();
        if (shownFollowers.Any(f => f.Item3 is null)) _portraitLifeRetry = Engine.GetProcessFrames() + PortraitLifeRetryFrames;
        _followerPortraits.Show(shownFollowers);
    }

    /// <summary>
    /// ARC-DEV-280 (B33): the left button's release over a follower's portrait is a click on that follower, as the original's portrait button
    /// handler (0x56A9D0, button event 1) passes the follower to the world click dispatcher 0x54EA80 like a press on its body: out of combat
    /// mode a talk (goal 21, 0x54ED30), in combat mode nothing without Alt. The click is taken under the world click's guards; an armed cursor
    /// or a pending order (the targets this click is not read for) leaves it unused.
    /// </summary>
    private void FollowerPortraitClick(string entity)
    {
        if (!WorldClickInputOpen || AreaTransferPending || HeroLyingDown || _gameCursorItem is not null || _spellTargeting is not null || _spellGoal is not null
            || ItemTargetOpen || PickPocketArmed || DisarmArmed || _pendingOrder is not null
            || _areaSession?.QueryCombatRound() is { } round && CombatModeActive && !round.Actor.IsHero)
        {
            _companionNotice = "follower click ignored: a window, a cursor or another's turn owns the input";
            RefreshReadyStatus();
            return;
        }
        if (HeroInCombatMode)
        {
            _companionNotice = "follower click in combat mode: attacking a follower is not ready"; // 0x54EFDA: the left Alt attacks it, nothing happens without
            RefreshReadyStatus();
            return;
        }
        var companion = _areaCompanions?.Companions.FirstOrDefault(c => c.Entity == entity);
        CancelPortalApproach("a follower click replaced it", keepArmed: true);
        CancelAreaAttack("a follower click replaced it");
        if (companion is null || PresentSpeaker(companion.Key) is not { } speaker || !TryAreaTalk(speaker))
        {
            _companionNotice = "follower click: no talk started";
            RefreshReadyStatus();
            return;
        }
        SetObjectApproachGait();
    }

    private void FollowerMenuChosen(string entity, FollowerMenuItem item)
    {
        // A new choice replaces a walk or attack order still waiting for its target.
        CancelPendingOrder("another menu choice");
        if (!OrderInputOpen)
        {
            _companionNotice = "follower order ignored: the mode or a window owns the input, another has the turn, the world has no input focus or a map change is pending";
            RefreshReadyStatus();
            return;
        }
        if (CompanionOrderRules.PortraitOrder(item, false) is not { } type)
        {
            _companionNotice = $"{AreaCompanionOrders.Label(item)}: not modelled for followers";
            RefreshReadyStatus();
            return;
        }
        if (type is CompanionOrderType.WalkHere or CompanionOrderType.Attack)
        {
            // OL1: the target cursor does not open for a dead or unconscious PC.
            if (HeroCannotPickOrderTarget)
            {
                _companionNotice = $"{AreaCompanionOrders.Label(item)}: not while the hero is dead or unconscious";
                RefreshReadyStatus();
                return;
            }
            // ARC-DEV-110 (MIG16 S3): the order waiting for its target is interface mode 18 (follower order) on the viewer's stack. The
            // mode is an overlay pushed over the top, so the other target modes are left first (the viewer takes one press at a time).
            var word = AreaCompanionOrders.Label(item);
            CancelSpellTargeting($"a {word} order took the next press");
            CancelArmedLockpicks($"a {word} order took the next press");
            CancelPickPocket($"a {word} order took the next press");
            CancelDisarm($"a {word} order took the next press");
            if (!OpenOverlayMode(InterfaceMode.FollowerOrder, _ => _pendingOrder = null))
            {
                _companionNotice = $"{word}: not ready (the interface mode did not open)";
                RefreshReadyStatus();
                return;
            }
            // The HUD's companion status names the pending order while it waits.
            _pendingOrder = (entity, type);
            _companionNotice = null;
            RefreshReadyStatus();
            return;
        }
        IssueAreaOrder("portrait", new(type, CompanionOrderAddressing.Portrait, [], entity, AreaCompanionOrders.Label(item)));
    }

    private bool HeroCannotPickOrderTarget => _areaSession?.QueryHeroLife()?.Status is { IsDead: true } or { IsUnconscious: true };

    /// <summary>The HUD's word of a pending order.</summary>
    private static string PendingOrderWord(CompanionOrderType type) => type == CompanionOrderType.Attack ? "attack" : "walk";

    /// <summary>Drops a pending walk or attack order with its reason in the HUD (nothing when none is pending).</summary>
    private void CancelPendingOrder(string why)
    {
        if (_pendingOrder is not { } pending) return;
        CloseTargetMode(InterfaceMode.FollowerOrder);
        _companionNotice = $"{PendingOrderWord(pending.Type)} order cancelled: {why}";
    }

    /// <summary>The pending order's target: the next press after choosing walk (its tile) or, ARC-WORLD-117, attack (the object under
    /// it, the original's object target cursor; an empty spot sends the order without one, which does nothing but the AI pass). True
    /// when it took the press.</summary>
    private bool TryPendingOrder(Vector2 screen)
    {
        if (_pendingOrder is not { } pending || _canvas is null) return false;
        var entity = pending.Entity;
        // The press pops the follower order mode (SA51: a click pops and the follower speaks the order).
        CloseTargetMode(InterfaceMode.FollowerOrder);
        // The caller (WorldRouteClick) has already ignored a press while the world does not own input.
        if (HeroCannotPickOrderTarget)
        {
            _companionNotice = $"{PendingOrderWord(pending.Type)} order cancelled: the hero is dead or unconscious";
            RefreshReadyStatus();
            return true;
        }
        if (pending.Type == CompanionOrderType.Attack)
        {
            IssueAreaOrder("portrait", new(CompanionOrderType.Attack, CompanionOrderAddressing.Portrait, [], entity,
                AreaCompanionOrders.Label(FollowerMenuItem.Attack)) { Target = OrderTarget(AreaObjectAtScreen(screen)) });
            return true;
        }
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is not { } cell) { _companionNotice = "walk: no tile there"; RefreshReadyStatus(); return true; }
        IssueAreaOrder("portrait", new(CompanionOrderType.WalkHere, CompanionOrderAddressing.Portrait, [], entity,
            AreaCompanionOrders.Label(FollowerMenuItem.Walk), cell));
        return true;
    }
}
