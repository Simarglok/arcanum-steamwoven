using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1877: the reading window's original look (docs/ui/original-windows.md; ledger WR17–WR23). The window's interface art and the fonts the written texts need are read once a module in the
// background; without a game folder (the synthetic self-tests) or when a required art or font does not read the window keeps its diagnostic layout. A picture's art (the written item's own number)
// is read when the window first shows it.
public partial class TerrainViewerApp
{
    private Task<AreaReadingArtContent>? _readingArtTask;
    private AreaReadingArtContent? _readingArt;
    private (Dictionary<int, Texture2D[]> Frames, Dictionary<int, AreaArtFontFace> Faces)? _readingArtLook;
    private AreaReadingPanel? _readingArtPanel;
    private string? _readingArtModule;
    private readonly Dictionary<int, Texture2D[]?> _readingPictures = [];

    /// <summary>Starts reading the window's art for this module (once; a reload, the cave or a map transfer reuses it).</summary>
    private void BindAreaReadingArt(TerrainViewerArguments arguments)
    {
        _readingArt = null; _readingArtLook = null; _readingArtPanel = null; _readingPictures.Clear();
        _readingPanel?.ArtView.ClearArt();
        var module = arguments.ModulePath;
        _readingArtModule = module;
        // ARC-UI-2765: a local capture asks for candidate faces of the note (ARC_AREA_READING_NOTE_FONTS=478,840,...); in play the list is empty.
        AreaReadingArtLayout.ExtraFontArts = System.Environment.GetEnvironmentVariable("ARC_AREA_READING_NOTE_FONTS") is { Length: > 0 } fonts
            ? [.. fonts.Split(',').Select(part => int.TryParse(part, out var number) ? number : 0).Where(number => number > 0)] : [];
        _readingArtTask = SelfTestBootstrap || module is null ? Task.FromResult(AreaReadingArtContent.Empty("self-test: no game folder"))
            : Task.Run(() => AreaReadingArtContent.Load(module));
    }

    private AreaReadingArtContent? AdoptReadingArt()
    {
        if (_readingArt is null && _readingArtTask is { IsCompleted: true } task)
        {
            _readingArt = task.IsCompletedSuccessfully ? task.Result : AreaReadingArtContent.Empty("load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
            GD.Print($"ARC-UI-1877 reading window art ready={_readingArt.Ready} {_readingArt.Detail}");
            // ARC-UI-1201: the arts' files and sizes (the Generated recipes are keyed by file name and laid out in the frames' own pixels).
            GD.Print("ARC-UI-1201 reading art files " + string.Join(", ", _readingArt.Art.Values.Select(load =>
                $"{load.Number}={load.Name} {load.Frames.Count}x({(load.Frames.Count > 0 ? load.Frames[0].Width + "x" + load.Frames[0].Height : "-")})")));
        }
        return _readingArt;
    }

    /// <summary>Gives the window's panel the original look once its art has read; nothing before that (or when it did not read): the diagnostic layout.</summary>
    private void AttachReadingArt(AreaReadingPanel panel)
    {
        if (AdoptReadingArt() is not { Ready: true } content) return;
        if (_readingArtLook is null)
        {
            var frames = content.Art.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0)
                .ToDictionary(load => load.Number, load => load.Frames.Select(frame => (Texture2D)InterfaceHd.Texture(frame, premultipliedAlpha: true)).ToArray());
            var faces = content.Fonts.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0)
                .ToDictionary(load => load.Number, load => new AreaArtFontFace(load.Frames));
            _readingArtLook = (frames, faces);
        }
        if (ReferenceEquals(panel, _readingArtPanel)) return;
        _readingArtPanel = panel;
        panel.ArtView.SetArt(_readingArtLook.Value.Frames, _readingArtLook.Value.Faces);
        panel.ArtView.Picture = ReadingPicture;
    }

    /// <summary>A picture's art (the written item's own interface art number), read from the game folder the first time and kept; null when it does not read.</summary>
    private Texture2D[]? ReadingPicture(int number)
    {
        if (_readingPictures.TryGetValue(number, out var known)) return known;
        Texture2D[]? frames = null;
        if (!SelfTestBootstrap && _readingArtModule is { } module && AreaReadingArtContent.LoadPicture(module, number) is { } load)
            frames = load.Frames.Select(frame => (Texture2D)InterfaceHd.Texture(frame, premultipliedAlpha: true)).ToArray();
        _readingPictures[number] = frames;
        return frames;
    }
}
