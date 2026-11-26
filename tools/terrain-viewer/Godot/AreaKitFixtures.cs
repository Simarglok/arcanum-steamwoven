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
    private static readonly Dictionary<string, Texture2D> Held = [], HeldPremultiplied = [];

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
        HeldPremultiplied.Clear();
        if (fixtures is not null) foreach (var pair in fixtures) Held[pair.Key] = pair.Value;
    }

    /// <summary>Draws the fixture <paramref name="id"/> with its corner at (<paramref name="x"/>, <paramref name="y"/>) units; false when the kit has none (the caller draws the stand-in). <paramref name="scale"/> is the device pixels of one unit.</summary>
    internal static bool Draw(CanvasItem canvas, float scale, string id, float x, float y)
    {
        if (!Held.TryGetValue(id, out var texture) || SteamwovenKitTable.PartUnits(id) is not { } units) return false;
        // ARC-UI-3299: a canvas item with the premultiplied-alpha material (the sheet's, for its text, <see cref="AreaArtTextDraw"/>) blends every texture as premultiplied: a straight-alpha part with soft edges (the lettering) would come out bold and bright, so it is drawn from a premultiplied copy there.
        if (canvas.Material is CanvasItemMaterial { BlendMode: CanvasItemMaterial.BlendModeEnum.PremultAlpha }) texture = Premultiplied(id, texture);
        canvas.DrawTextureRect(texture, new Rect2(x * scale, y * scale, units.Width * scale, units.Height * scale), false);
        Drawn.Add(id);
        return true;
    }

    /// <summary>
    /// ARC-UI-3321: draws the region of the fixture <paramref name="id"/> that starts at (<paramref name="fromX"/>, <paramref name="fromY"/>) and is <paramref name="width"/> x <paramref name="height"/> units, with its corner at
    /// (<paramref name="x"/>, <paramref name="y"/>) units; false when the kit has none.
    /// </summary>
    internal static bool DrawRegion(CanvasItem canvas, float scale, string id, float x, float y, float fromX, float fromY, float width, float height)
    {
        if (!Held.TryGetValue(id, out var texture) || SteamwovenKitTable.PartUnits(id) is not { } units) return false;
        if (canvas.Material is CanvasItemMaterial { BlendMode: CanvasItemMaterial.BlendModeEnum.PremultAlpha }) texture = Premultiplied(id, texture);
        float across = texture.GetWidth() / (float)units.Width, down = texture.GetHeight() / (float)units.Height;
        canvas.DrawTextureRectRegion(texture, new Rect2(x * scale, y * scale, width * scale, height * scale), new Rect2(fromX * across, fromY * down, width * across, height * down));
        Drawn.Add(id);
        return true;
    }

    /// <summary>A premultiplied copy of the part's texture (made once per published set).</summary>
    private static Texture2D Premultiplied(string id, Texture2D texture)
    {
        if (HeldPremultiplied.TryGetValue(id, out var held)) return held;
        var image = texture.GetImage();
        if (image is null) return texture;
        image.PremultiplyAlpha();
        return HeldPremultiplied[id] = ImageTexture.CreateFromImage(image);
    }
}
