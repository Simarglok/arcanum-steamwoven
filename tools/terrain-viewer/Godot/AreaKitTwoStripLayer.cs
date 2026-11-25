using Arcanum.Presentation.Steamwoven;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3038, ARC-UI-3039, ARC-UI-3041: the kit's three parts of a two-strip screen (<see cref="SteamwovenKitScreen"/>: the top strip, the body background and the bottom strip) on the screen's plate, loaded and drawn by the view that owns the
/// layer. One layer per view: it holds the composed draws, the sampled copies of the parts and the set whose body part it keeps resident (the body only while the screen shows it, SWI-RUN-5). A part the set lacks, holds at another
/// size or does not load gives no kit screen (the view then draws its own arts whole, SWI-RUN-3).
/// </summary>
internal sealed class AreaKitTwoStripLayer(SteamwovenKitScreen screen, string owner)
{
    private List<(Texture2D Texture, SteamwovenKitDraw Draw)>? _parts;
    private SteamwovenKitSet? _held;
    private AreaKitBackdrop? _backdrop;
    private readonly Dictionary<Texture2D, (CanvasTexture Texture, bool Exact)> _sampled = [];

    /// <summary>The kit's draws of the screen as of the last <see cref="Compose"/> (top strip, body, bottom strip, then the hardware layers over the strips, ARC-UI-3260); null while the screen draws its own art.</summary>
    internal IReadOnlyList<SteamwovenKitDraw>? DrawsForSelfTest => _parts?.Select(item => item.Draw).ToList();

    /// <summary>True when the part drawn for <paramref name="index"/> (0 top, 1 body, 2 bottom) is sampled nearest (its draw is exact).</summary>
    internal bool SampledNearestForSelfTest(int index) => _parts is { } parts && index < parts.Count && _sampled.TryGetValue(parts[index].Texture, out var sampled) && sampled.Texture.TextureFilter == CanvasItem.TextureFilterEnum.Nearest;

    /// <summary>True while the kit's three parts are the screen's backdrop.</summary>
    internal bool Active => _parts is not null;

    /// <summary>ARC-UI-3041: the overlays of the body window's content (the opening shop's barter window: the merchant's doll view and the hero's doll cover) that the set holds and that loaded, for the window frame drawn over the body; null while the
    /// kit is not the screen's backdrop or the body window has no overlays (a screen with a body part of its own).</summary>
    internal AreaKitBackdrop? Backdrop => _parts is not null ? _backdrop : null;

    /// <summary>The screen is closed or draws its own art: the body part is no longer held. <paramref name="loaded"/> is the set a failed composition had already loaded parts of (a part that did not load after the body did leaves the body
    /// unowned unless it is released here).</summary>
    internal void Release(SteamwovenKitSet? loaded = null)
    {
        _parts = null; _backdrop = null;
        _sampled.Clear();
        if ((loaded ?? _held) is { } held) SteamwovenKitStore.Retain(owner, held, []);
        _held = null;
    }

    /// <summary>
    /// The kit's three parts in the plate's rectangles, loaded: <paramref name="zoned"/> is true while the view is placed by the column plan (<c>AreaGameBarPanel.ColumnMenuPlan(<paramref name="viewport"/>)</c>); false when the layout is off, the set
    /// does not read, or any part is missing, the wrong size or does not load. The body part stays resident while the screen shows it. Returns whether the kit is the screen's backdrop. <paramref name="bodyWindow"/> (ARC-UI-3041) picks the variant
    /// of a window-bodied screen's body (the barter window of a follower merchant); the body's pieces and the window content's overlays stay resident with it.
    /// </summary>
    internal bool Compose(bool zoned, Vector2 viewport, SteamwovenKitWindow? bodyWindow = null)
    {
        if (!zoned || AreaGameBarPanel.ColumnMenuPlan(viewport) is not { } plan || SteamwovenKitStore.SetFor(plan.Layout.Step) is not { } set) { Release(); return false; }
        if (SteamwovenKitComposer.Screen(plan, set, screen, null, bodyWindow).Value is not { } composed) { Release(); return false; }
        var loaded = new List<(Texture2D, SteamwovenKitDraw)>(3);
        foreach (var draw in composed.AllDraws)
        {
            if (SteamwovenKitStore.Texture(set, draw.PartId) is not { } texture) { Release(set); return false; }
            loaded.Add((texture, draw));
        }
        // ARC-UI-3041: a window body's content draws overlays over it (the doll view, the doll cover); one the set lacks or that does not load is left out and the frame keeps its own art for it.
        var held = composed.BodyPieces.Select(piece => piece.PartId).ToList();
        AreaKitBackdrop? backdrop = null;
        if (SteamwovenKitTable.Screens[screen].BodyWindow is { } window && SteamwovenKitTable.OverlaysOf(bodyWindow ?? window) is { Count: > 0 } overlays)
        {
            var loadedOverlays = new Dictionary<string, AreaKitOverlay>();
            foreach (var overlay in overlays)
                if (SteamwovenKitComposer.Overlay(set, overlay) is { } part && SteamwovenKitStore.Texture(set, part.Id) is { } texture) { loadedOverlays[overlay.Part] = new(texture, overlay.UnitWidth, overlay.UnitHeight); held.Add(part.Id); }
            backdrop = new(loadedOverlays.GetValueOrDefault(SteamwovenKitTable.BarterDoll.Part), loadedOverlays.GetValueOrDefault(SteamwovenKitTable.HeroDollCover.Part));
        }
        SteamwovenKitStore.Retain(owner, set, held);
        _held = set; _backdrop = backdrop;
        // The sampled copies follow the textures drawn now (a rebaked set or another step gives new ones).
        foreach (var stale in _sampled.Keys.Where(key => loaded.All(item => !ReferenceEquals(item.Item1, key))).ToList()) _sampled.Remove(stale);
        _parts = loaded;
        return true;
    }

    /// <summary>Draws the three parts, each into its device rectangle: the view is placed at the plate's corner and scale, so the rectangle is taken back into the view's own units; a part is sampled nearest only where its draw is exact (the
    /// baked pixels are the device pixels, SWI-LAY-5), else linear.</summary>
    internal void Draw(CanvasItem canvas, Vector2 position, Vector2 scale)
    {
        if (_parts is not { } parts) return;
        foreach (var (texture, draw) in parts)
        {
            if (!_sampled.TryGetValue(texture, out var sampled) || sampled.Exact != draw.Exact)
            {
                sampled = (new CanvasTexture { DiffuseTexture = texture, TextureFilter = draw.Exact ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear }, draw.Exact);
                _sampled[texture] = sampled;
            }
            var rect = draw.Rect;
            canvas.DrawTextureRect(sampled.Texture, new Rect2((rect.X - position.X) / scale.X, (rect.Y - position.Y) / scale.Y, rect.Width / scale.X, rect.Height / scale.Y), false);
        }
    }
}
