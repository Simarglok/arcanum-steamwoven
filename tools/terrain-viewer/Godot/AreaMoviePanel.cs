using Arcanum.Presentation.Audio;
using Arcanum.Presentation.Movies;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-RENDER-1170: the screen a movie plays on (docs/research/bink-movies.md section 5): the whole window black, the frame centred and
/// scaled to fit, above every other panel. It is the movie player's output (<see cref="IMovieOutput"/>): a frame becomes the picture's
/// texture and the movie's audio plays through one stream player on the effects channel. The app drives the player and owns the input;
/// the panel takes the mouse so nothing under it is clicked.
/// </summary>
public partial class AreaMoviePanel : CanvasLayer, IMovieOutput
{
    private ColorRect _back = null!;
    private TextureRect _picture = null!;
    private AudioStreamPlayer _player = null!;
    private Image? _image;
    private ImageTexture? _texture;
    // ARC-UI-3501: the still picture on the screen; the panel frees it when it goes.
    private Texture2D? _still;

    public bool IsOpen => Visible;

    /// <summary>How many frames were drawn since the panel opened (for the smoke).</summary>
    public int FramesDrawn { get; private set; }

    /// <summary>True while the movie's audio plays.</summary>
    public bool AudioPlaying => _player.Playing;

    /// <summary>The size of the last frame drawn, or null.</summary>
    public Vector2I? FrameSize => _texture is { } texture ? new(texture.GetWidth(), texture.GetHeight()) : null;

    public override void _Ready()
    {
        Layer = 20;
        Visible = false;
        _back = new ColorRect { Name = "MovieBack", Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop };
        _back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_back);
        _picture = new TextureRect
        {
            Name = "MovieFrame", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        _picture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_picture);
        _player = new AudioStreamPlayer { Name = "MovieAudio" };
        var bus = ViewerAudioBackend.BusName(AudioChannel.Effects);
        if (AudioServer.GetBusIndex(bus) >= 0) _player.Bus = bus;
        AddChild(_player);
    }

    /// <summary>Opens the screen black; the first frame comes with the player's first step.</summary>
    public void Open()
    {
        FramesDrawn = 0;
        ClearStill();
        Visible = true;
    }

    /// <summary>ARC-UI-3501: a still picture on the black screen, centred and scaled to fit like a movie's frame. The panel owns it.</summary>
    public void ShowStill(Texture2D picture, CanvasItem.TextureFilterEnum filter)
    {
        ClearStill();
        _picture.TextureFilter = filter;
        _picture.Texture = _still = picture;
    }

    /// <summary>Takes the picture off; the screen stays black.</summary>
    public void ClearStill()
    {
        _picture.Texture = null; _texture = null; _image = null;
        _still?.Dispose(); _still = null;
    }

    /// <summary>The still or frame on the screen, for the smoke.</summary>
    public Texture2D? Shown => _picture.Texture;

    /// <summary>Closes the screen and stops the audio.</summary>
    public void Close()
    {
        StopAudio();
        Visible = false;
        ClearStill();
    }

    public void ShowFrame(byte[] rgba, int width, int height)
    {
        _picture.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
        if (_image is null || _texture is null || _image.GetWidth() != width || _image.GetHeight() != height)
        {
            _image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
            _texture = ImageTexture.CreateFromImage(_image);
            _picture.Texture = _texture;
        }
        else
        {
            _image.SetData(width, height, false, Image.Format.Rgba8, rgba);
            _texture.Update(_image);
        }
        FramesDrawn++;
    }

    public void StartAudio(byte[] pcm)
    {
        var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = MovieInfo.AudioSampleRate, Stereo = true, Data = pcm };
        _player.Stream = wav;
        _player.Play();
    }

    public void StopAudio()
    {
        if (_player.Playing) _player.Stop();
        _player.Stream = null;
    }
}
