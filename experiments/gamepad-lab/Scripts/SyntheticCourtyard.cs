using GamepadLab.Core;
using Godot;

namespace GamepadLab;
public partial class SyntheticCourtyard : Node2D
{
    // Camera bounds are deliberately larger than the obstacle courtyard so a normal
    // 1280x800 viewport can follow the initial actor before the map-edge clamp is
    // reached. The lab still has no claim about original Arcanum camera bounds.
    public static readonly Rect2 CameraMapBounds = new(-200, -200, 2000, 2000);
    private static readonly Rect2[] Obstacles = [new(675, 430, 34, 170), new(800, 260, 150, 30), new(800, 260, 30, 120), new(900, 520, 30, 120), new(970, 520, 30, 120)];
    private readonly (string Id, string Kind, Vector2 Point, int Cycle)[] _objects =
    {
        ("npc-ada", "npc", new(570, 350), 1), ("npc-bram", "npc", new(710, 400), 2), ("enemy-ash", "enemy", new(650, 290), 3), ("enemy-briar", "enemy", new(770, 470), 4), ("door-north", "door", new(640, 210), 5), ("door-east", "door", new(930, 400), 6), ("crate", "container", new(620, 480), 7), ("tiny-gear", "item", new(650, 492), 8), ("loose-vial", "item", new(520, 455), 9), ("isolated-marker", "object", new(320, 630), 10),
        ("ally-iris", "ally", new(560, 450), 11), ("hidden-interactive", "item", new(630, 470), 12), ("scenery", "scenery", new(635, 475), 13)
    };
    private string? _selected;
    private readonly Dictionary<string, LabWorldTarget> _overrides = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);
    private HashSet<string> _highlighted = new(StringComparer.Ordinal);
    public IReadOnlyList<LabWorldTarget> WorldTargets => _objects.Where(o => !_removed.Contains(o.Id)).Select(o => _overrides.TryGetValue(o.Id, out var value) ? value : new LabWorldTarget(o.Id, o.Kind, ToCore(o.Point), o.Cycle, Visible: o.Id != "hidden-interactive", Interactive: o.Kind != "scenery")).ToArray();
    public void SetTargetState(LabWorldTarget value) { _overrides[value.Id] = value; QueueRedraw(); }
    public void RemoveTarget(string id) { _removed.Add(id); QueueRedraw(); }
    public void ReloadTargets() { _removed.Clear(); _overrides.Clear(); _highlighted.Clear(); QueueRedraw(); }
    public void SetHighlighted(IEnumerable<string> ids) { var next = ids.ToHashSet(StringComparer.Ordinal); if (!_highlighted.SetEquals(next)) { _highlighted = next; QueueRedraw(); } }
    private Vector2 _actor = new(460, 610); private Vector2 _heading = Vector2.Right; private Vector2? _goal;
    public Vector2 ActorPosition => _actor;
    public IReadOnlyList<TargetCandidate> Candidates(Node2D world) => WorldTargets.Select(o => new TargetCandidate(o.Id, o.Kind, ToCore(world.GetGlobalTransformWithCanvas() * new Vector2((float)o.Anchor.X, (float)o.Anchor.Y)), o.CycleOrder, o.Alive, o.Interactive, o.Visible)).ToArray();
    public string? PickVisible(Vector2 viewportPoint, Node2D world) => WorldTargets.Reverse().FirstOrDefault(o => o.Visible && o.Interactive && o.Alive && (world.GetGlobalTransformWithCanvas() * new Vector2((float)o.Anchor.X, (float)o.Anchor.Y)).DistanceTo(viewportPoint) <= (o.Id == "tiny-gear" ? 12 : 24))?.Id;
    public void SetSelected(string? id) { if (_selected != id) { _selected = id; QueueRedraw(); } }
    public void SetActor(Vector2 position, Vector2 heading, Vector2? goal) { _actor = position; if (heading.Length() > .01f) _heading = heading.Normalized(); _goal = goal; QueueRedraw(); }
    public bool CanOccupy(Vector2 position, float radius)
    {
        if (position.X - radius < 180 || position.X + radius > 1080 || position.Y - radius < 160 || position.Y + radius > 720) return false;
        return Obstacles.All(obstacle => !obstacle.Grow(radius).HasPoint(position));
    }
    public void RecenterActor() { _actor = new Vector2(460, 610); _heading = Vector2.Right; _goal = null; QueueRedraw(); }
    public override void _Draw()
    {
        DrawRect(CameraMapBounds, new Color("18354c"));
        for (var x = -180; x <= 1780; x += 60) DrawLine(new Vector2(x, -180), new Vector2(x, 1780), new Color("31546a"));
        for (var y = -180; y <= 1780; y += 40) DrawLine(new Vector2(-180, y), new Vector2(1780, y), new Color("31546a"));
        DrawCircle(new Vector2(635, 475), 52, new Color("455c43")); DrawCircle(new Vector2(605, 445), 28, new Color("70806b"));
        foreach (var o in WorldTargets.Where(o => o.Visible))
        {
            var point = new Vector2((float)o.Anchor.X, (float)o.Anchor.Y);
            var radius = o.Id == "tiny-gear" ? 8 : 18;
            var color = o.Kind switch { "npc" => new Color("6cc8e8"), "enemy" => new Color("e8756d"), "door" => new Color("d7a75c"), "container" => new Color("bc8b57"), "item" => new Color("d9e67a"), _ => new Color("b2a4ed") };
            DrawCircle(point, radius, color); if (o.Id == _selected) DrawArc(point, radius + 8, 0, Mathf.Tau, 32, Colors.Gold, 3);
            if (_highlighted.Contains(o.Id)) DrawArc(point, radius + 5, 0, Mathf.Tau, 32, Colors.Cyan, 3);
        }
        foreach (var obstacle in Obstacles) DrawRect(obstacle, new Color("30313d"));
        if (_goal is Vector2 goal) { DrawCircle(goal, 8, Colors.Gold, false, 2); DrawLine(_actor, goal, new Color("e8c45d", .55f), 1); }
        DrawCircle(_actor, 16, new Color("b2a4ed")); DrawArc(_actor, 20, 0, Mathf.Tau, 24, Colors.White, 2); DrawLine(_actor, _actor + _heading * 26, Colors.White, 3);
        DrawString(ThemeDB.FallbackFont, _actor + new Vector2(-32, 40), "actor", HorizontalAlignment.Left, -1, 14, Colors.White);
        DrawString(ThemeDB.FallbackFont, new(200, 150), "PROCEDURAL ISOMETRIC COURTYARD — SYNTHETIC ONLY", HorizontalAlignment.Left, -1, 16, Colors.LightSteelBlue);
    }
    private static Vec2 ToCore(Vector2 value) => new(value.X, value.Y);
}