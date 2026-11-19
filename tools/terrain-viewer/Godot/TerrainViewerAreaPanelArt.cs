using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-024: the follower frames' art and portraits (docs/rendering/terrain-viewer.md#panel-art-arc-ui-024; ledger CH9, PA6) and
// the hovered follower's portrait in the message window. The frame art, the portrait catalogue and each portrait image are read
// from the game folder once a run (a portrait when a follower first needs it); the self-test has none and draws the fallback.
public partial class TerrainViewerApp
{
    private AreaPortraitContent? _portraitContent;
    private string? _portraitModule;
    private readonly Dictionary<int, Texture2D?> _portraitTextures = [];

    /// <summary>Reads the frame art and the portraits <paramref name="followers"/> need that are not read yet (one content stack
    /// opening for both), and binds the art.</summary>
    private void LoadFollowerPortraits(IReadOnlyList<AreaCompanion> followers)
    {
        LoadPortraits(followers.Select(c => ShownPortrait(c.Speaker.Row)).OfType<int>().Distinct().ToArray());
        // ARC-UI-2284: content read before the panel existed (a save window's portraits, a warm-up) has the panel bound the first time the panel meets it.
        BindPanelArt();
        // ARC-UI-1786: the order menu's words come from the same load (mes\follower_ui.mes).
        if (_followerPortraits is { } panel) { panel.MenuTexts = _portraitContent?.Menu; panel.MenuFont = TextFonts()?.Text; }
    }

    /// <summary>Reads the frame art (on the first use) and the portraits of <paramref name="ids"/> not read yet.</summary>
    private void LoadPortraits(IReadOnlyList<int> ids)
    {
        var module = _arguments?.ModulePath;
        if (_portraitContent is null || module != _portraitModule)
        {
            _portraitModule = module; _portraitTextures.Clear();
            _portraitContent = SelfTestBootstrap || module is null ? AreaPortraitContent.Empty("self-test: no game folder") : AreaPortraitContent.Load(module, ids);
            BindPanelArt();
            GD.Print($"ARC-UI-024 follower frames art ready={_portraitContent.ArtReady} {_portraitContent.Detail}");
            return;
        }
        if (SelfTestBootstrap || module is null) return;
        var wanted = ids.Where(id => !_portraitContent.Portraits.ContainsKey(id)).ToArray();
        if (wanted.Length == 0) return;
        _portraitContent = AreaPortraitContent.Load(module, wanted, _portraitContent);
        GD.Print($"ARC-UI-024 portraits {string.Join(",", wanted)}: {string.Join("; ", wanted.Select(id => _portraitContent.Portraits.TryGetValue(id, out var file) ? file.Status + " " + file.Detail : "not read"))}");
    }

    private IReadOnlyDictionary<int, AreaInterfaceArtLoad>? _panelArtBound;

    /// <summary>ARC-UI-2284 (ARC-DEV-369 before it): gives the follower panel the frame and button art of the content read, once per content, whenever both exist (the content may be read
    /// first by a save window's portraits or a warm-up, before any follower made the panel; a panel that missed it drew the dark fallback plates with their words).</summary>
    private void BindPanelArt()
    {
        if (_followerPortraits is not { } panel || _portraitContent is not { } content || ReferenceEquals(_panelArtBound, content.Art)) return;
        panel.Bind(InterfaceTextures(content.Art));
        _panelArtBound = content.Art;
    }

    /// <summary>A follower's portrait texture (its stored portrait's small image), or null when it has none or it did not read.</summary>
    private Texture2D? FollowerPortrait(AreaCompanion companion) => ShownPortrait(companion.Speaker.Row) is { } id ? PortraitTexture(id) : null;

    /// <summary>ARC-UI-2444 (ledger PO10, 0x4CEB80): the portrait number a draw shows for a critter row: its stored one, but an NPC that stores the
    /// local PC's own portrait shows the catalogue's next or previous one. Reads the catalogue on first use.</summary>
    private int? ShownPortrait(AreaObjectRecord row)
    {
        var player = _areaSession?.QueryHero()?.Portrait;
        // The catalogue is read only when a clash is possible (an NPC storing the PC's own portrait).
        return AreaPortraits.Shown(row, AreaPortraits.Stored(row) is { } stored && stored == player ? PortraitCatalogueNow() : null, player, _areaSession);
    }

    /// <summary>ARC-UI-2444: the portrait catalogue (read with the panel content on first use); null with no game folder.</summary>
    private PortraitCatalogue? PortraitCatalogueNow()
    {
        if (_portraitContent is null) LoadPortraits([]);
        return _portraitContent?.Catalogue;
    }

    /// <summary>ARC-UI-067: a save's portrait for the save window's info display: its small image (PO3's size 0 draws the image at its
    /// own size from the catalogue's path, the small file), read on first use.</summary>
    private Texture2D? SavePortrait(int id)
    {
        LoadPortraits([id]);
        return PortraitTexture(id);
    }

    /// <summary>ARC-UI-1908: a holder's stored portrait for the item windows (the small image at its own size, read on first use).</summary>
    private Texture2D? HolderPortraitTexture(int id) => SavePortrait(id);

    /// <summary>A read portrait's texture, made once; null when it did not read or decode.</summary>
    private Texture2D? PortraitTexture(int id)
    {
        if (_portraitContent?.Portraits.GetValueOrDefault(id) is not { Status: AreaPortraitStatus.Ready, Bytes: { } bytes }) return null;
        if (_portraitTextures.TryGetValue(id, out var known)) return known;
        var image = new Image();
        Texture2D? texture = image.LoadBmpFromBuffer(bytes) == Error.Ok && !image.IsEmpty() ? ImageTexture.CreateFromImage(image) : null;
        if (texture is null) GD.Print($"ARC-UI-024 portrait {id}: the image did not decode");
        _portraitTextures[id] = texture;
        return texture;
    }

    private static IReadOnlyDictionary<int, Texture2D> InterfaceTextures(IReadOnlyDictionary<int, AreaInterfaceArtLoad> art) =>
        art.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0).ToDictionary(load => load.Number,
            load => InterfaceHd.Texture(load.Frames[0]));

    /// <summary>The message window's hovered-critter portrait while the bars are drawn: a hovered follower the PC leads that has one (the
    /// original's 0x553D10 gives the PC's followers their portrait); null otherwise. NOT READY: the PC itself (the viewer cannot hover the
    /// PC, and its portrait is not session state).</summary>
    private Texture2D? HoverPortrait(AreaObjectKey key) =>
        _areaSession is { } session && _areaCompanions?.Companions.FirstOrDefault(c => c.Speaker.Row.Key == key) is { } companion
            && session.QueryCompanion(companion.Entity).LedByPc ? FollowerPortrait(companion) : null;
}
