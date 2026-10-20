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
    // A portrait walk order waiting for its tile (the original's target cursor).
    private string? _pendingWalkOrder;

    /// <summary>F1–F6 while the session party is wired: the presets. Other keys, or no party, are not orders.</summary>
    private bool RouteOrderKey(WorldKeyDecision decision)
    {
        if (decision.Key is < WorldKey.F1 or > WorldKey.F6 || !decision.Pressed || decision.Echo || WorldModal || WorldUiFocus) return false;
        if (_areaSession is not { } session || _areaCompanions is null || _conversationHost is not { PartyWired: true }) return false;
        var preset = CompanionOrderRules.Preset(decision.Key - WorldKey.F1, session.Rules.Orders);
        var key = decision.Key.ToString();
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
            || _worldActor.Session is not AreaGridSession pc) return null;
        var facts = AreaCompanionOrders.Facts(session, group, pc.State.CommittedCell, host.State, CompanionName);
        var result = AreaCompanionOrders.Issue(session, group, request with { Followers = facts }, pc.State.CommittedCell, WorldTime);
        GD.Print(AreaCompanionOrders.Log(source, request.Type, result));
        _companionNotice = result.Failure != GameSessionFailure.None ? $"{source}: order refused ({result.Failure})"
            : result.Outcomes.Count == 0 ? $"{source}: {request.Type} reached no follower within {CompanionOrderRules.Reach} tiles"
            : $"{source}: " + string.Join("; ", result.Outcomes.Select(AreaCompanionOrders.Describe));
        RefreshFollowerPortraits();
        RefreshReadyStatus();
        return result;
    }

    /// <summary>The name the PC knows a companion by: its hover name on the shown map, else its dialog script.</summary>
    private string CompanionName(AreaCompanion companion) =>
        _hoverIndex is { } index && ReferenceEquals(_hoverIndexArea, _readyArea) && index.Contains(companion.Key) && index.Name(companion.Key) is { } name
            ? name : $"script {companion.Speaker.ScriptId}";

    /// <summary>Shows a portrait per follower in the array (in its order) while the session party is wired.</summary>
    private void RefreshFollowerPortraits()
    {
        if (_areaSession is not { } session || _areaCompanions is not { } group || _conversationHost is not { PartyWired: true })
        {
            _followerPortraits?.Show([]);
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
        if (CompanionOrderRules.PortraitOrder(item, false) is not { } type)
        {
            _companionNotice = $"{AreaCompanionOrders.Label(item)}: not modelled for followers";
            RefreshReadyStatus();
            return;
        }
        if (type == CompanionOrderType.WalkHere)
        {
            _pendingWalkOrder = entity;
            _companionNotice = "walk: click the tile to walk to";
            RefreshReadyStatus();
            return;
        }
        IssueAreaOrder("portrait", new(type, CompanionOrderAddressing.Portrait, [], entity, AreaCompanionOrders.Label(item)));
    }

    /// <summary>The walk order's tile: the next ground press after choosing walk (true when it took the press).</summary>
    private bool TryPendingWalkOrder(Vector2 screen)
    {
        if (_pendingWalkOrder is not { } entity || _canvas is null) return false;
        _pendingWalkOrder = null;
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is not { } cell) { _companionNotice = "walk: no tile there"; RefreshReadyStatus(); return true; }
        IssueAreaOrder("portrait", new(CompanionOrderType.WalkHere, CompanionOrderAddressing.Portrait, [], entity,
            AreaCompanionOrders.Label(FollowerMenuItem.Walk), cell));
        return true;
    }
}
