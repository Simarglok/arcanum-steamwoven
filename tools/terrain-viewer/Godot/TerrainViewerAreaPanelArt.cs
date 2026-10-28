using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
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

    /// <summary>Reads the frame art and the portraits <paramref name="followers"/> need that are not read yet, and binds the art.</summary>
    private void LoadFollowerPortraits(IReadOnlyList<AreaCompanion> followers)
    {
        var module = _arguments?.ModulePath;
        if (_portraitContent is null || module != _portraitModule)
        {
            _portraitModule = module; _portraitTextures.Clear();
            _portraitContent = SelfTestBootstrap || module is null ? AreaPortraitContent.Empty("self-test: no game folder") : AreaPortraitContent.Load(module, []);
            _followerPortraits?.Bind(InterfaceTextures(_portraitContent.Art));
            GD.Print($"ARC-UI-024 follower frames art ready={_portraitContent.ArtReady} {_portraitContent.Detail}");
        }
        if (SelfTestBootstrap || module is null) return;
        var wanted = followers.Select(c => AreaPortraits.Stored(c.Speaker.Row)).OfType<int>().Where(id => !_portraitContent.Portraits.ContainsKey(id)).ToArray();
        if (wanted.Length == 0) return;
        _portraitContent = AreaPortraitContent.Load(module, wanted, _portraitContent);
        GD.Print($"ARC-UI-024 portraits {string.Join(",", wanted)}: {string.Join("; ", wanted.Select(id => _portraitContent.Portraits.TryGetValue(id, out var file) ? file.Status + " " + file.Detail : "not read"))}");
    }

    /// <summary>A follower's portrait texture (its stored portrait's small image), or null when it has none or it did not read.</summary>
    private Texture2D? FollowerPortrait(AreaCompanion companion)
    {
        if (AreaPortraits.Stored(companion.Speaker.Row) is not { } id || _portraitContent?.Portraits.GetValueOrDefault(id) is not { Status: AreaPortraitStatus.Ready, Bytes: { } bytes })
            return null;
        if (_portraitTextures.TryGetValue(id, out var known)) return known;
        var image = new Image();
        Texture2D? texture = image.LoadBmpFromBuffer(bytes) == Error.Ok && !image.IsEmpty() ? ImageTexture.CreateFromImage(image) : null;
        if (texture is null) GD.Print($"ARC-UI-024 portrait {id}: the image did not decode");
        _portraitTextures[id] = texture;
        return texture;
    }

    private static IReadOnlyDictionary<int, Texture2D> InterfaceTextures(IReadOnlyDictionary<int, AreaInterfaceArtLoad> art) =>
        art.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0).ToDictionary(load => load.Number,
            load => (Texture2D)ImageTexture.CreateFromImage(Image.CreateFromData(load.Frames[0].Width, load.Frames[0].Height, false, Image.Format.Rgba8, load.Frames[0].Rgba)));

    /// <summary>The message window's hovered-critter image while the bars are drawn: the portrait and name of a hovered follower the
    /// PC leads that has one (the original's 0x553D10 gives the PC's followers their portrait); nothing otherwise. NOT READY: the PC
    /// itself (the viewer cannot hover the PC, and its portrait is not session state) and the critter art of anyone else.</summary>
    private void SyncHoverPortrait(AreaObjectKey? key)
    {
        if (_gameBarPanel is not { Visible: true } bars) return;
        Texture2D? portrait = null; string? name = null;
        if (key is { } hit && _areaSession is { } session && _areaCompanions?.Companions.FirstOrDefault(c => c.Speaker.Row.Key == hit) is { } companion
            && session.QueryCompanion(companion.Entity).LedByPc && FollowerPortrait(companion) is { } texture)
            (portrait, name) = (texture, CompanionName(companion));
        bars.ShowHoverPortrait(portrait, name);
    }
}
