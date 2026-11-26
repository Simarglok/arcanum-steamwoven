using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3347: the kit faces of the small brass-ringed buttons (<see cref="SteamwovenKitTable.RingFaces"/>) for the views that show them: the inventory, loot and barter frame, the dialog box, the new character screen and the strip's note
/// entry and quantity pane. Unlike the sheet pages' faces (<see cref="AreaKitSheets"/>) they need no kit background: a face is the kit's while the Steamwoven look is on and the set of the window's step holds the button's idle face (read
/// from the set at each draw, loaded once). Null otherwise, and the view draws the art's own frame as before.
/// </summary>
internal static class AreaKitRingFaces
{
    /// <summary>The face of <paramref name="art"/> for the original's frame <paramref name="frame"/> (0 idle, 1 pressed, 2 hover; a state the set lacks is the idle face) in a window of <paramref name="viewport"/> pixels, or null.</summary>
    internal static Texture2D? Face(int art, int frame, Vector2 viewport)
    {
        if (!SteamwovenKitTable.RingFaces.ContainsKey(art) || AreaGameBarPanel.ColumnMenuPlan(viewport) is not { } plan || SteamwovenKitStore.SetFor(plan.Layout.Step) is not { } set
            || SteamwovenKitComposer.RoundFace(set, art) is not { } face || SteamwovenKitStore.Texture(set, face.Idle.Id) is not { } idle) return null;
        var state = frame switch { 1 => face.Pressed, 2 => face.Hover, _ => null };
        return state is { } held && SteamwovenKitStore.Texture(set, held.Id) is { } texture ? texture : idle;
    }
}

/// <summary>
/// ARC-UI-3347: a button that is a <see cref="TextureRect"/> of its own (the strip's panes) with its kit face over it: the face goes to the view's round-part layer and the rect keeps its texture (the hit area is its size) but draws nothing.
/// </summary>
internal static class AreaKitRingButtonRect
{
    /// <summary>Gives <paramref name="rect"/>, which shows frame <paramref name="frame"/> of <paramref name="art"/>, the kit face when there is one (an item added to <paramref name="items"/>), else its own drawing back.</summary>
    internal static void Apply(TextureRect rect, int art, int frame, List<AreaKitRoundPartsLayer.Item> items)
    {
        var face = rect.Visible && rect.IsInsideTree() ? AreaKitRingFaces.Face(art, frame, rect.GetViewportRect().Size) : null;
        var item = face is null ? null : AreaKitRoundPartsLayer.KitItem(art, face, (rect.Position.X, rect.Position.Y), 1f);
        rect.SelfModulate = item is null ? Colors.White : new Color(1, 1, 1, 0);
        if (item is { } shown) items.Add(shown);
    }
}
