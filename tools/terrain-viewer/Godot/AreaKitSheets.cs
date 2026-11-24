using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3035 (census G06 to G10): the kit sheets the character sheet's pages lay over the kit's background (<c>backdrop.char_maint</c> of ARC-UI-2461) in place of their own panel arts 29, 30, 31 and 567. The bar panel publishes
/// the sheets of the set (the table's size at the step) exactly while it draws the character window's kit background under the open window; the pages read them as they read the window zone (one bar panel lives at a time) and redraw on
/// <see cref="Changed"/>. A page whose sheet is not here draws its own art as before; nothing is published without a complete kit background, with the HD option off or after the window closes.
/// </summary>
internal static class AreaKitSheets
{
    private static readonly Dictionary<int, Texture2D> Held = [];

    /// <summary>The kit sheet that replaces the page art <paramref name="art"/>, or null (the page draws its own art).</summary>
    internal static Texture2D? For(int art) => Held.GetValueOrDefault(art);

    /// <summary>The arts whose page draws its sheet from the kit now, for the self-test.</summary>
    internal static IReadOnlyCollection<int> ShownForSelfTest => [.. Held.Keys.Order()];

    /// <summary>The sheets shown by the kit changed; the pages redraw.</summary>
    internal static event Action? Changed;

    internal static void Publish(IReadOnlyDictionary<int, Texture2D>? sheets)
    {
        var same = Held.Count == (sheets?.Count ?? 0) && (sheets is null || sheets.All(pair => Held.TryGetValue(pair.Key, out var held) && ReferenceEquals(held, pair.Value)));
        if (same) return;
        Held.Clear();
        if (sheets is not null) foreach (var pair in sheets) Held[pair.Key] = pair.Value;
        Changed?.Invoke();
    }
}

/// <summary>
/// ARC-UI-3035: the page's kit sheet, a node behind its own drawing (the page's rectangle in its own units, sampled linearly because the baked sheet is not at the page's unit size). The page asks <see cref="Set"/> after each
/// change of the kit sheets or of its own page and draws its own panel art only while <see cref="Shown"/> is false.
/// </summary>
internal sealed partial class AreaKitSheetView : TextureRect
{
    internal AreaKitSheetView()
    {
        Name = "KitSheet";
        ExpandMode = ExpandModeEnum.IgnoreSize; StretchMode = StretchModeEnum.Scale;
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        ShowBehindParent = true;
        Visible = false;
    }

    /// <summary>True while the sheet of the kit shows for this page (the page draws no panel art of its own).</summary>
    internal bool Shown => Visible;

    /// <summary>Takes the published sheet for the page art <paramref name="art"/> (or none) and fits it to the page's size.</summary>
    internal void Set(int art, Vector2 size)
    {
        var texture = AreaKitSheets.For(art);
        Texture = texture; Position = Vector2.Zero; Size = size; Visible = texture is not null;
    }
}
