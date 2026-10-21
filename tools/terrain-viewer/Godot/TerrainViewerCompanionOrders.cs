using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-046: follower orders in crash-site-area (exterior, cave and map mode). F1–F6 send the original's presets with the tile
// under the pointer (F6 per the active rules, D2); the follower portrait strip opens each follower's order menu (walk asks for a
// tile with the next left click). Orders go through the session; the HUD and the log name the outcomes and their line ranges.
public partial class TerrainViewerApp
{
    private AreaFollowerPortraits? _followerPortraits;
    // A portrait walk order waiting for its tile (the original's target cursor); any non-modifier key, a modal window, focus loss,
    // another menu choice or a rebuilt group drops it.
    private string? _pendingWalkOrder;
    // The portraits' last state (session, revision, group, modal), so a frame with nothing new does not rebuild them.
    private (Arcanum.Session.GameSessionOwner? Session, long Revision, AreaCompanionGroup? Group, bool Modal, object? Names)? _portraitStamp;

    /// <summary>Orders are taken only while the world owns input: focused, no modal window or control focus, no transfer pending.</summary>
    private bool OrderInputOpen => _worldFocused && !WorldModal && !WorldUiFocus && !AreaTransferPending;

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
            _companionNotice = $"{key} ignored: a window is open, the world has no input focus or a map change is pending";
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
        IssueAreaOrder(key, new(preset.Type, CompanionOrderAddressing.All, [], Tile: PointerCell()));
        return true;
    }

    /// <summary>The chart cell under the pointer, or null off the map.</summary>
    private CellCoord? PointerCell()
    {
        if (_canvas is null) return null;
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition();
        return AreaCellPicking.Pick(new(chart.X, chart.Y));
    }

    /// <summary>Sends an order (its followers' facts are gathered here) and shows its outcomes.</summary>
    private SessionCompanionOrderResult? IssueAreaOrder(string source, CompanionOrderRequest request)
    {
        if (_areaSession is not { } session || _areaCompanions is not { } group || _conversationHost is not { } host
            || _worldActor.Session is not AreaGridSession pc)
        {
            _companionNotice = $"{source}: order not sent (no area session)";
            RefreshReadyStatus();
            return null;
        }
        var facts = AreaCompanionOrders.Facts(session, group, pc.State.CommittedCell, host.State, CompanionName);
        var (result, noGoal) = AreaCompanionOrders.Issue(session, group, request with { Followers = facts }, pc.State.CommittedCell, WorldTime);
        GD.Print(AreaCompanionOrders.Log(source, request.Type, result) + (noGoal.Count == 0 ? "" : " | goal not started: " + string.Join(", ", noGoal)));
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
            ? name : $"script {companion.Speaker.ScriptId}";

    /// <summary>Shows a portrait per follower in the array (in its order) while the session party is wired; none while a modal window
    /// is open (its menu closes and a pending walk order is dropped). Rebuilt only when the session, its revision, the group or the
    /// modal state changed.</summary>
    private void RefreshFollowerPortraits()
    {
        var stamp = (_areaSession, _areaSession?.Version.Revision ?? -1, _areaCompanions, WorldModal, (object?)_hoverIndex);
        if (_portraitStamp == stamp) return;
        _portraitStamp = stamp;
        if (_areaSession is not { } session || _areaCompanions is not { } group || _conversationHost is not { PartyWired: true } || WorldModal)
        {
            _followerPortraits?.Show([]);
            _followerPortraits?.CloseMenu();
            if (WorldModal) { CancelPendingWalkOrder("a window opened"); _orderKeyArmed = null; }
            return;
        }
        if (_followerPortraits is null)
        {
            _followerPortraits = new AreaFollowerPortraits(); AddChild(_followerPortraits);
            _followerPortraits.ItemChosen += FollowerMenuChosen;
        }
        var shown = group.Companions.ToDictionary(c => c.Entity, StringComparer.Ordinal);
        _followerPortraits.Show(session.QueryParty().Followers.Where(f => shown.ContainsKey(f.Entity))
            .Select(f => (f.Entity, CompanionName(shown[f.Entity]))).ToArray());
    }

    private void FollowerMenuChosen(string entity, FollowerMenuItem item)
    {
        // A new choice replaces a walk order still waiting for its tile.
        CancelPendingWalkOrder("another menu choice");
        if (!OrderInputOpen)
        {
            _companionNotice = "follower order ignored: a window is open, the world has no input focus or a map change is pending";
            RefreshReadyStatus();
            return;
        }
        if (CompanionOrderRules.PortraitOrder(item, false) is not { } type)
        {
            _companionNotice = $"{AreaCompanionOrders.Label(item)}: not modelled for followers";
            RefreshReadyStatus();
            return;
        }
        if (type == CompanionOrderType.WalkHere)
        {
            // OL1: the target cursor does not open for a dead or unconscious PC.
            if (HeroCannotOrderWalk)
            {
                _companionNotice = "walk: not while the hero is dead or unconscious";
                RefreshReadyStatus();
                return;
            }
            // The HUD's companion status names the pending walk while it waits.
            _pendingWalkOrder = entity;
            _companionNotice = null;
            RefreshReadyStatus();
            return;
        }
        IssueAreaOrder("portrait", new(type, CompanionOrderAddressing.Portrait, [], entity, AreaCompanionOrders.Label(item)));
    }

    private bool HeroCannotOrderWalk => _areaSession?.QueryHeroLife()?.Status is { IsDead: true } or { IsUnconscious: true };

    /// <summary>Drops a pending walk order with its reason in the HUD (nothing when none is pending).</summary>
    private void CancelPendingWalkOrder(string why)
    {
        if (_pendingWalkOrder is null) return;
        _pendingWalkOrder = null;
        _companionNotice = $"walk order cancelled: {why}";
    }

    /// <summary>The walk order's tile: the next ground press after choosing walk (true when it took the press).</summary>
    private bool TryPendingWalkOrder(Vector2 screen)
    {
        if (_pendingWalkOrder is not { } entity || _canvas is null) return false;
        _pendingWalkOrder = null;
        // The caller (WorldRouteClick) has already ignored a press while the world does not own input.
        if (HeroCannotOrderWalk)
        {
            _companionNotice = "walk order cancelled: the hero is dead or unconscious";
            RefreshReadyStatus();
            return true;
        }
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is not { } cell) { _companionNotice = "walk: no tile there"; RefreshReadyStatus(); return true; }
        IssueAreaOrder("portrait", new(CompanionOrderType.WalkHere, CompanionOrderAddressing.Portrait, [], entity,
            AreaCompanionOrders.Label(FollowerMenuItem.Walk), cell));
        return true;
    }
}
