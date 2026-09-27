namespace GamepadLab.Core;

public sealed record TargetCandidate(string Id, string Kind, Vec2 Anchor, int CycleOrder, bool Alive = true, bool ContextValid = true, bool Visible = true);
public sealed record TargetingPolicy(double Radius = 180, double TieEpsilon = .001, double Hysteresis = 24);
public sealed record SelectionState(string? SelectedId = null, bool ManualLock = false);
public sealed record SelectionResult(string? Id, bool IsGround, Vec2 GroundPoint, double? Score, bool ManualLock, IReadOnlyList<TargetCandidate> Eligible);

public sealed class TargetingEngine
{
    public SelectionResult Update(IReadOnlyList<TargetCandidate> candidates, Rect viewport, SelectionState state, TargetingPolicy policy)
    {
        var focus = viewport.Center;
        var eligible = candidates.Where(c => c.Alive && c.ContextValid && c.Visible && viewport.Contains(c.Anchor) && Vec2.Distance(c.Anchor, focus) <= policy.Radius)
            .OrderBy(c => c.CycleOrder).ThenBy(c => c.Id, StringComparer.Ordinal).ToArray();
        var byId = eligible.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var best = eligible.OrderBy(c => Vec2.Distance(c.Anchor, focus)).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
        TargetCandidate? selected = null;
        var manual = state.ManualLock;
        if (state.SelectedId is not null && byId.TryGetValue(state.SelectedId, out var current))
        {
            if (manual || best is null || Vec2.Distance(current.Anchor, focus) <= Vec2.Distance(best.Anchor, focus) + policy.Hysteresis) selected = current;
        }
        if (selected is null) { selected = best; manual = false; }
        return new(selected?.Id, selected is null, focus, selected is null ? null : Vec2.Distance(selected.Anchor, focus), manual, eligible);
    }

    public SelectionState Cycle(SelectionResult result, int direction)
    {
        if (result.Eligible.Count == 0) return new(null, false);
        var index = result.Id is null ? (direction >= 0 ? -1 : 0) : result.Eligible.ToList().FindIndex(c => c.Id == result.Id);
        var next = (index + (direction >= 0 ? 1 : -1) + result.Eligible.Count) % result.Eligible.Count;
        return new(result.Eligible[next].Id, true);
    }

    public SelectionState Pick(string? id) => new(id, id is not null);
    public SelectionState CancelLock() => new(null, false);
}
