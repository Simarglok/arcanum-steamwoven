using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3098: the kit's fixtures, the required details the old window arts painted into themselves (pits, fields, count signs, dials, discs, the ruler, gears, cell wells; <c>SteamwovenKitTable.FixtureUnits</c>). The bar panel
/// publishes the fixtures of the open window that the set holds at the table's size, exactly while it draws that window's kit background; the windows draw a fixture at its place in their own units where it is here, and their plain
/// stand-in (<see cref="AreaKitStandIns"/>) where it is not. Nothing is published without a complete kit background, with the HD option off or after the window closes.
/// </summary>
internal static class AreaKitFixtures
{
    private static readonly Dictionary<string, Texture2D> Held = [];

    /// <summary>The ids drawn from the kit now, for the self-test.</summary>
    internal static IReadOnlyCollection<string> ShownForSelfTest => [.. Held.Keys.Order()];

    /// <summary>The ids drawn from the kit, and the ids a stand-in took the place of, since <see cref="ResetForSelfTest"/>.</summary>
    internal static IReadOnlyCollection<string> DrawnForSelfTest => [.. Drawn.Order()];

    private static readonly HashSet<string> Drawn = [];

    internal static void ResetForSelfTest() => Drawn.Clear();

    internal static bool Has(string id) => Held.ContainsKey(id);

    internal static bool Same(IReadOnlyDictionary<string, Texture2D>? fixtures) =>
        Held.Count == (fixtures?.Count ?? 0) && (fixtures is null || fixtures.All(pair => Held.TryGetValue(pair.Key, out var held) && ReferenceEquals(held, pair.Value)));

    internal static void Publish(IReadOnlyDictionary<string, Texture2D>? fixtures)
    {
        Held.Clear();
        if (fixtures is not null) foreach (var pair in fixtures) Held[pair.Key] = pair.Value;
    }

    /// <summary>Draws the fixture <paramref name="id"/> with its corner at (<paramref name="x"/>, <paramref name="y"/>) units; false when the kit has none (the caller draws the stand-in). <paramref name="scale"/> is the device pixels of one unit.</summary>
    internal static bool Draw(CanvasItem canvas, float scale, string id, float x, float y)
    {
        if (!Held.TryGetValue(id, out var texture) || !SteamwovenKitTable.FixtureUnits.TryGetValue(id, out var units)) return false;
        canvas.DrawTextureRect(texture, new Rect2(x * scale, y * scale, units.Width * scale, units.Height * scale), false);
        Drawn.Add(id);
        return true;
    }
}
