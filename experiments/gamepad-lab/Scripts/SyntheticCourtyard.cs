using GamepadLab.Core;
using Godot;

namespace GamepadLab;
public partial class SyntheticCourtyard : Node2D
{
    private static readonly Rect2[] Obstacles = [new(675, 430, 34, 170), new(800, 260, 150, 30), new(800, 260, 30, 120), new(900, 520, 30, 120), new(970, 520, 30, 120)];
    private readonly (string Id, string Kind, Vector2 Point, int Cycle)[] _objects =
    {
        ("npc-ada", "npc", new(570, 350), 1), ("npc-bram", "npc", new(710, 400), 2), ("enemy-ash", "enemy", new(650, 290), 3), ("enemy-briar", "enemy", new(770, 470), 4), ("door-north", "door", new(640, 210), 5), ("door-east", "door", new(930, 400), 6), ("crate", "container", new(620, 480), 7), ("tiny-gear", "item", new(650, 492), 8), ("loose-vial", "item", new(520, 455), 9), ("isolated-marker", "object", new(320, 630), 10)
    };
    private string? _selected;
    private Vector2 _actor = new(460, 610); private Vector2 _heading = Vector2.Right; private Vector2? _goal;
    public Vector2 ActorPosition => _actor;
    public IReadOnlyList<TargetCandidate> Candidates(Node2D world) => _objects.Select(o => new TargetCandidate(o.Id, o.Kind, ToCore(world.GetGlobalTransformWithCanvas() * o.Point), o.Cycle)).ToArray();
    public string? PickVisible(Vector2 viewportPoint, Node2D world) => _objects.Reverse().FirstOrDefault(o => (world.GetGlobalTransformWithCanvas() * o.Point).DistanceTo(viewportPoint) <= (o.Id == "tiny-gear" ? 12 : 24)).Id;
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
        DrawRect(new Rect2(180, 160, 900, 560), new Color("18354c"));
        for (var x = 200; x <= 1060; x += 60) DrawLine(new Vector2(x, 180), new Vector2(x, 700), new Color("31546a"));
        for (var y = 180; y <= 700; y += 40) DrawLine(new Vector2(200, y), new Vector2(1060, y), new Color("31546a"));
        DrawCircle(new Vector2(635, 475), 52, new Color("455c43")); DrawCircle(new Vector2(605, 445), 28, new Color("70806b"));
        foreach (var o in _objects)
        {
            var radius = o.Id == "tiny-gear" ? 8 : 18;
            var color = o.Kind switch { "npc" => new Color("6cc8e8"), "enemy" => new Color("e8756d"), "door" => new Color("d7a75c"), "container" => new Color("bc8b57"), "item" => new Color("d9e67a"), _ => new Color("b2a4ed") };
            DrawCircle(o.Point, radius, color); if (o.Id == _selected) DrawArc(o.Point, radius + 8, 0, Mathf.Tau, 32, Colors.Gold, 3);
            DrawString(ThemeDB.FallbackFont, o.Point + new Vector2(-28, -25), o.Id, HorizontalAlignment.Left, -1, 14, Colors.White);
        }
        foreach (var obstacle in Obstacles) DrawRect(obstacle, new Color("30313d"));
        if (_goal is Vector2 goal) { DrawCircle(goal, 8, Colors.Gold, false, 2); DrawLine(_actor, goal, new Color("e8c45d", .55f), 1); }
        DrawCircle(_actor, 16, new Color("b2a4ed")); DrawArc(_actor, 20, 0, Mathf.Tau, 24, Colors.White, 2); DrawLine(_actor, _actor + _heading * 26, Colors.White, 3);
        DrawString(ThemeDB.FallbackFont, _actor + new Vector2(-32, 40), "actor", HorizontalAlignment.Left, -1, 14, Colors.White);
        DrawString(ThemeDB.FallbackFont, new(200, 150), "PROCEDURAL ISOMETRIC COURTYARD — SYNTHETIC ONLY", HorizontalAlignment.Left, -1, 16, Colors.LightSteelBlue);
    }
    private static Vec2 ToCore(Vector2 value) => new(value.X, value.Y);
}