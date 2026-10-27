using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Navigation;

/// <summary>ARC-WORLD-073: the shared path request against the drop's own port as ARC-COMBAT-010 wrote it (the reference below), on
/// invented random maps.</summary>
[TestClass]
public sealed class OriginalPathRequestTests
{
    /// <summary>A random map: blocked tiles, refused steps (by a wall, a portal or another object), window pieces on cardinal steps,
    /// avoided traps and closed destinations, all drawn once from the seed.</summary>
    private sealed class RandomMap : IOriginalPathMap
    {
        private readonly int _size;
        private readonly HashSet<CellCoord> _blocked = [], _traps = [], _closed = [];
        private readonly Dictionary<(CellCoord, int), OriginalPathTraversal> _steps = [];

        internal RandomMap(int seed, int size)
        {
            _size = size;
            var random = new Random(seed);
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var cell = new CellCoord(x, y);
                    var roll = random.Next(100);
                    if (roll < 18) _blocked.Add(cell);
                    else if (roll < 22) _traps.Add(cell);
                    else if (roll < 24) _closed.Add(cell);
                    for (var rotation = 0; rotation < 8; rotation++)
                    {
                        var r = random.Next(100);
                        _steps[(cell, rotation)] = r < 8 ? new(true, false, random.Next(4) switch { 0 => 0, 1 => 1, 2 => 3, _ => null })
                            : r < 11 && (rotation & 1) == 1 ? new(false, true, null) : OriginalPathTraversal.Open;
                    }
                }
        }

        public CellCoord? Step(CellCoord from, int rotation)
        {
            var next = OriginalPathRequest.Neighbor(from, rotation);
            return next.X >= 0 && next.Y >= 0 && next.X < _size && next.Y < _size ? next : null;
        }
        public bool TileBlocked(CellCoord cell) => _blocked.Contains(cell);
        public OriginalPathTraversal Traverse(CellCoord from, int rotation) => _steps[(from, rotation)];
        public int EnterCost(CellCoord to) => _traps.Contains(to) ? 90 : 10;
        public bool DestinationOpen(CellCoord to) => !_blocked.Contains(to) && !_closed.Contains(to);
    }

    [TestMethod]
    public void The_shared_request_returns_what_the_drops_own_port_returned()
    {
        var compared = 0; var found = 0; var direct = 0;
        for (var seed = 1; seed <= 40; seed++)
        {
            var map = new RandomMap(seed, 70);
            var random = new Random(1000 + seed);
            for (var pair = 0; pair < 60; pair++)
            {
                var from = new CellCoord(random.Next(70), random.Next(70));
                var to = new CellCoord(Math.Clamp(from.X + random.Next(-40, 41), 0, 69), Math.Clamp(from.Y + random.Next(-40, 41), 0, 69));
                foreach (var budget in new[] { 200, RouteLimits.WalkBudget(from, to), 7 })
                {
                    var shared = OriginalPathRequest.Request(map, from, to, budget);
                    var reference = Reference.Path(map, from, to, budget);
                    var label = $"seed {seed} {from}->{to} budget {budget}";
                    Assert.AreEqual((reference.Result, reference.Partial), (shared.Result, shared.Partial), label);
                    var written = Math.Max(reference.Result > 0 ? reference.Length : 0, reference.Partial);
                    CollectionAssert.AreEqual(reference.Rotations.Take(written).ToArray(), shared.Rotations.Take(written).ToArray(), label);
                    if (reference.Result > 0) Assert.AreEqual(reference.Length, shared.Length, label);
                    compared++; if (shared.Result > 0) found++; if (shared.End == OriginalPathEnd.Direct) direct++;
                }
            }
        }
        Assert.AreEqual(40 * 60 * 3, compared);
        Assert.IsTrue(found > 500 && direct > 100, $"found {found}, direct {direct}: the maps exercise both parts");
    }

    [TestMethod]
    public void The_request_follows_the_ledger_limits()
    {
        var open = new RandomMap(0, 1);
        // An open 100 × 100 map with one wall row: the direct attempt reaches 32 on the larger axis, the search needs both axes under 32.
        var walls = new WallMap();
        Assert.AreEqual((OriginalPathEnd.Direct, 8, 32), End(OriginalPathRequest.Request(walls, new(10, 10), new(42, 10), 200)));
        Assert.AreEqual(OriginalPathEnd.SearchOutOfRange, OriginalPathRequest.Request(walls, new(10, 10), new(43, 10), 200).End, "33 tiles: neither part runs");
        var behind = OriginalPathRequest.Request(walls, new(50, 48), new(50, 60), 200);
        Assert.AreEqual(OriginalPathEnd.Search, behind.End, "the wall at y 50 is passed through its gap by the search");
        Assert.AreEqual(OriginalPathEnd.SearchOutOfRange, OriginalPathRequest.Request(walls, new(50, 48), new(50, 80), 200).End, "32 on an axis: no search");
        Assert.AreEqual(OriginalPathEnd.SearchPruned, OriginalPathRequest.Request(walls, new(50, 48), new(50, 60), 8).End, "the detour is longer than the budget");
        Assert.AreEqual(0, OriginalPathRequest.Request(open, new(0, 0), new(0, 0), 200).Result);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OriginalPathRequest.Request(walls, new(0, 0), new(1, 1), 201));
        Assert.AreEqual(5, OriginalPathRequest.Rotation(new(3, 3), new(3, 3)), "equal tiles give 5");
    }

    private static (OriginalPathEnd, int, int) End(OriginalPath path) => (path.End, path.Result, path.Length);

    /// <summary>100 × 100 open tiles, a wall along y = 50 with a gap at x = 70.</summary>
    private sealed class WallMap : IOriginalPathMap
    {
        public CellCoord? Step(CellCoord from, int rotation)
        {
            var next = OriginalPathRequest.Neighbor(from, rotation);
            return next.X is >= 0 and < 100 && next.Y is >= 0 and < 100 ? next : null;
        }
        public bool TileBlocked(CellCoord cell) => cell.Y == 50 && cell.X != 70;
        public OriginalPathTraversal Traverse(CellCoord from, int rotation) => OriginalPathTraversal.Open;
        public int EnterCost(CellCoord to) => 10;
        public bool DestinationOpen(CellCoord to) => !TileBlocked(to);
    }

    /// <summary>The drop's port of 0x41F3C0, 0x41F840 and 0x41F9F0 as ARC-COMBAT-010 wrote it in AreaDropMap (a linear scan over the
    /// 64 × 64 window), over the map interface, with the request's budget in place of its 200.</summary>
    private static class Reference
    {
        private const int Excluded = unchecked((int)0xFFFF8000), Window = 64, DirectLimit = 32, Rotations = 200, Cap = 8;

        internal sealed record Result_(int Result, int Partial, int[] Rotations, int Length);

        internal static Result_ Path(IOriginalPathMap map, CellCoord from, CellCoord to, int budget)
        {
            if (from == to || !map.DestinationOpen(to)) return new(0, 0, [], 0);
            var rotations = new int[Rotations];
            var direct = Direct(map, from, to, rotations, out var partial);
            var result = direct > 0 ? direct : Search(map, from, to, rotations, budget);
            return new(Math.Min(result, Cap), partial, rotations, result);
        }

        private static int Direct(IOriginalPathMap map, CellCoord from, CellCoord to, int[] rotations, out int partial)
        {
            partial = 0;
            if (Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) > DirectLimit) return 0;
            var cur = from;
            var i = 0;
            for (; i < Rotations && cur != to; i++)
            {
                var rotation = OriginalPathRequest.Rotation(cur, to);
                rotations[i] = rotation;
                if (map.Step(cur, rotation) is not { } next || map.TileBlocked(next) || map.Traverse(cur, rotation) is not { Blocked: false, Window: false } ||
                    map.EnterCost(next) != 10) break;
                cur = next;
            }
            if (cur == to) return i;
            partial = i;
            return 0;
        }

        private static int Search(IOriginalPathMap map, CellCoord from, CellCoord to, int[] rotations, int budget)
        {
            long dx = Math.Abs(from.X - to.X), dy = Math.Abs(from.Y - to.Y);
            if (dx >= DirectLimit || dy >= DirectLimit) return 0;
            var originX = Math.Min(from.X, to.X) - (Window - dx) / 2;
            var originY = Math.Min(from.Y, to.Y) - (Window - dy) / 2;
            int Index(CellCoord c) => (int)((c.Y - originY) * Window + (c.X - originX));
            CellCoord Cell(int index) => new((int)(originX + index % Window), (int)(originY + index / Window));
            bool Inside(CellCoord c) => c.X >= originX && c.X < originX + Window && c.Y >= originY && c.Y < originY + Window;
            var cost = new int[Window * Window];
            var parent = new int[Window * Window];
            var start = Index(from);
            var goal = Index(to);
            cost[start] = 1;
            parent[start] = -1;
            while (true)
            {
                var best = -1;
                long bestF = 0;
                for (var index = 0; index < cost.Length; index++)
                {
                    if (cost[index] <= 0) continue;
                    var f = cost[index] + 10L * Math.Max(Math.Abs(Cell(index).X - to.X), Math.Abs(Cell(index).Y - to.Y));
                    if (f / 10 > budget) { cost[index] = Excluded; continue; }
                    if (best < 0 || f < bestF) { best = index; bestF = f; }
                }
                if (best < 0) return 0;
                if (best == goal) break;
                var cur = Cell(best);
                for (var rotation = 0; rotation < 8; rotation++)
                {
                    if (map.Step(cur, rotation) is not { } n || !Inside(n)) continue;
                    var ni = Index(n);
                    if (cost[ni] == Excluded) continue;
                    if (map.TileBlocked(n)) { cost[ni] = Excluded; continue; }
                    var traversal = map.Traverse(cur, rotation);
                    if (traversal.Blocked)
                    {
                        if ((rotation & 1) == 1 && traversal.BlockerType is { } type && type is not (0 or 1)) cost[ni] = Excluded;
                        continue;
                    }
                    var step = map.EnterCost(n);
                    if (traversal.Window) step += 50;
                    var next = cost[best] + step;
                    if (parent[best] != -1 && OriginalPathRequest.Rotation(Cell(parent[best]), cur) != rotation) next++;
                    var v = cost[ni];
                    if (v == 0 || v > 0 && v > next || v < 0 && -v > next) { cost[ni] = next; parent[ni] = best; }
                }
                cost[best] = -cost[best];
            }
            var steps = 0;
            for (var k = goal; parent[k] != -1; k = parent[k]) steps++;
            if (steps > Rotations) return 0;
            var at = goal;
            for (var i = steps - 1; i >= 0; i--)
            {
                rotations[i] = OriginalPathRequest.Rotation(Cell(parent[at]), Cell(at));
                at = parent[at];
            }
            return steps;
        }
    }
}
