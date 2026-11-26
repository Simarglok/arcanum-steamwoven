using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3035 (census G06 to G10): the kit sheets the character sheet's pages lay over the kit's background (<c>backdrop.char_maint</c> of ARC-UI-2461) in place of their own panel arts 29, 30, 31 and 567. The bar panel publishes
/// the sheets of the set (the table's size at the step) exactly while it draws the character window's kit background under the open window; the pages read them as they read the window zone (one bar panel lives at a time) and redraw on
/// <see cref="Changed"/>. ARC-UI-3320: with the sheets go the faces of the pages' round buttons (<see cref="RoundFace"/>). A page whose sheet is not here draws its own art as before; nothing is published without a complete kit background, with the HD option off or after the window closes.
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

    private static readonly Dictionary<int, Texture2D[]> HeldRound = [];

    /// <summary>
    /// ARC-UI-3320: the kit face of the round button the page shows with <paramref name="art"/> in the look of <paramref name="frame"/> (the original's frame order: 0 idle, 1 pressed, 2 hover; a state the set lacks is the idle face), or null (the button
    /// keeps the art's own frame). The faces are straight-alpha textures of the master's size, cut to the ring's circle; they are drawn through the round-part layer.
    /// </summary>
    internal static Texture2D? RoundFace(int art, int frame) =>
        HeldRound.TryGetValue(art, out var faces) ? faces[Math.Clamp(frame, 0, faces.Length - 1)] : null;

    /// <summary>The arts whose round button draws from the kit now, for the self-test.</summary>
    internal static IReadOnlyCollection<int> RoundShownForSelfTest => [.. HeldRound.Keys.Order()];

    private static readonly Dictionary<int, Texture2D[]> HeldFaces = [];

    /// <summary>
    /// ARC-UI-3341: the kit face of the flat control the window shows with <paramref name="art"/> (<see cref="SteamwovenKitTable.WindowFaces"/>) in the look of <paramref name="frame"/> (as <see cref="RoundFace"/>), or null (the window draws the art's own
    /// frame). The faces are straight-alpha textures of the master's size, drawn through an <see cref="AreaKitFaceLayer"/> over the art's rectangle.
    /// </summary>
    internal static Texture2D? Face(int art, int frame) => HeldFaces.TryGetValue(art, out var faces) ? faces[Math.Clamp(frame, 0, faces.Length - 1)] : null;

    /// <summary>The arts whose flat control draws from the kit now, for the self-test.</summary>
    internal static IReadOnlyCollection<int> FacesShownForSelfTest => [.. HeldFaces.Keys.Order()];

    /// <summary>Publishes the sheets and, with them, the round buttons' and the flat controls' faces (three textures each, idle, pressed, hover); null clears them.</summary>
    internal static void Publish(IReadOnlyDictionary<int, Texture2D>? sheets, IReadOnlyDictionary<int, Texture2D[]>? rounds = null, IReadOnlyDictionary<int, Texture2D[]>? faces = null)
    {
        var same = Held.Count == (sheets?.Count ?? 0) && (sheets is null || sheets.All(pair => Held.TryGetValue(pair.Key, out var held) && ReferenceEquals(held, pair.Value)))
            && SameFaces(HeldRound, rounds) && SameFaces(HeldFaces, faces);
        if (same) return;
        Held.Clear(); HeldRound.Clear(); HeldFaces.Clear();
        if (sheets is not null) foreach (var pair in sheets) Held[pair.Key] = pair.Value;
        if (rounds is not null) foreach (var pair in rounds) HeldRound[pair.Key] = pair.Value;
        if (faces is not null) foreach (var pair in faces) HeldFaces[pair.Key] = pair.Value;
        Changed?.Invoke();
    }

    private static bool SameFaces(Dictionary<int, Texture2D[]> held, IReadOnlyDictionary<int, Texture2D[]>? next) =>
        held.Count == (next?.Count ?? 0) && (next is null || next.All(pair => held.TryGetValue(pair.Key, out var known) && known.SequenceEqual(pair.Value)));
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
