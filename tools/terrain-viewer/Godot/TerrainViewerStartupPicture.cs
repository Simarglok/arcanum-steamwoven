using Arcanum.Session.Settings;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-3501 (docs/research/startup-loading-picture.md): the "Loading Arcanum..." picture between the logos and movie 8, an entry of the
// start-up queue of the movie screen (TerrainViewerMovies.cs). The original look draws the original's 800 x 600 screen with the picture in
// its play view; with HD interface art on, the pack's 16:9 redraw of the same picture, centred on black (16:10 pads above and below).
public partial class TerrainViewerApp
{
    private StartupPictureStore? _startupPictureStore;
    // The next splash number, saved when the app closes (as the original saves its configuration at exit); null when no picture was picked.
    private int? _startupPictureNext;
    // The time the shown picture still has; null while none shows.
    private double? _pictureLeft;
    private (IReadOnlyList<string> Paths, Func<string, byte[]?> Read, uint Counter)? _startupPictureForSelfTest;
    private string? _startupPictureShown;

    private StartupPictureStore StartupPictures => _startupPictureStore ??= new(AutomatedVisualRun);

    /// <summary>Picks this start's picture and puts it on the movie screen; false, and nothing shows, without game content or a picture the
    /// original could draw.</summary>
    private bool ShowStartupPicture()
    {
        IReadOnlyList<string> paths; Func<string, byte[]?> read; uint counter;
        if (_startupPictureForSelfTest is { } synthetic) (paths, read, counter) = synthetic;
        else if (AreaSoundCatalog() is not null && _soundContent?.Stack is { } stack)
            (paths, read, counter) = (AreaStartupPicture.Paths(stack), path => AreaStartupPicture.Read(stack, path), AreaStartupPicture.Counter(StartupPictures.Read()));
        else { GD.Print("ARC-UI-3501 start-up picture not shown: no game content"); return false; }
        var choice = AreaStartupPicture.Choose(paths, counter, read);
        if (choice.NextCounter is { } next) _startupPictureNext = next;
        if (choice.Path is not { } shown || choice.Screen is not { } screen)
        {
            GD.Print($"ARC-UI-3501 start-up picture not shown: no picture can be drawn (files={paths.Count})");
            return false;
        }
        // The look in force decides the art and the sampling; the first processed frame may not have set it yet.
        SyncInterfaceHd();
        var hd = HdStartupPicture(System.IO.Path.GetFileNameWithoutExtension(shown));
        Texture2D texture;
        if (hd is not null) texture = hd;
        else
        {
            using var image = Image.CreateFromData(AreaStartupPicture.ScreenWidth, AreaStartupPicture.ScreenHeight, false, Image.Format.Rgba8, screen);
            texture = ImageTexture.CreateFromImage(image);
        }
        // The original look draws its art at a whole factor (nearest) and the window reduces it; every other size is sampled linear.
        var filter = hd is not null ? CanvasItem.TextureFilterEnum.LinearWithMipmaps
            : OriginalCompositionFactor != 0 ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.Linear;
        _moviePanel!.ShowStill(texture, filter);
        _pictureLeft = ViewerStartupMovies.PictureMilliseconds;
        _startupPictureShown = shown;
        GD.Print($"ARC-UI-3501 start-up picture {shown} art={(hd is null ? "original" : "hd")} number={counter} next={choice.NextCounter}");
        return true;
    }

    /// <summary>The active pack's 16:9 redraw of the picture <paramref name="stem"/> while HD interface art is on, or null.</summary>
    private static Texture2D? HdStartupPicture(string stem)
    {
        if (!InterfaceHd.Enabled || InterfaceHd.Pack is not { } pack) return null;
        var read = pack.ReadStartupPictureReport(stem);
        // The file is named by the pack's own relative name (no local path).
        GD.Print($"ARC-UI-3501 start-up picture hd file={Arcanum.Data.Hd.HdPack.StartupPictureFile(stem) ?? "none"} status={read.Status} sha12={read.Sha12 ?? "none"} size={read.Width}x{read.Height} pack={pack.Id}");
        if (read.Image is not { } picture) return null;
        using var image = Image.CreateFromData(picture.Width, picture.Height, false, Image.Format.Rgba8, picture.Rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Writes the splash number of the picture this run picked, once, when the app closes.</summary>
    private void SaveStartupPictureCounter()
    {
        if (_startupPictureNext is not { } next) return;
        _startupPictureNext = null;
        if (StartupPictures.Persistent && !StartupPictures.Save(next)) GD.PrintErr("ARC-UI-3501 splash number not saved");
    }
}

/// <summary>The <c>splash</c> line of the player's settings file; an automated run has none unless the test variable names one.</summary>
public sealed class StartupPictureStore
{
    public const string TestVariable = "STARTUP_PICTURE_TEST_PATH";
    private readonly string? _path;

    public StartupPictureStore(bool automated) => _path = ViewerSettingsFile.Choose(TestVariable, automated);

    public bool Persistent => _path is not null;

    public string? Read() => _path is null ? null : ArcanumConfig.Read(_path).Get(ViewerSettingsConfig.StartupPicture);

    public bool Save(int next) => _path is not null
        && ArcanumConfig.Save(_path, [new ArcanumConfigChange(ViewerSettingsConfig.StartupPicture, next.ToString(System.Globalization.CultureInfo.InvariantCulture))]) == ArcanumConfigSaveStatus.Saved;
}
