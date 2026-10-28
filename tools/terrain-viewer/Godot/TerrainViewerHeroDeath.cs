using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-026: the hero's death in the AREA viewer (exterior, cave and map mode), as the original does it (AreaHeroDeath, ledger
// PD1–PD9). Five seconds after the session's hero dies the windows over play close (an open main-menu window closes and the death
// comes again 0.3 s later), the screen fades to black and the death slide of the installed rules\slide.mes shows with its voice
// and the slides' music; then the game ends as a quit does: the start scene loads from the launch arguments with a fresh session
// and the main menu reopens, fading in. There is no screen with a choice: a load is the main menu's. Automated runs (self-tests,
// native smokes) keep it off unless their own check turns it on, as many of them kill or wound the hero for other checks.
public partial class TerrainViewerApp
{
    /// <summary>The music the original plays under the slides (PD6).</summary>
    internal const string DeathSlidesMusic = "sound/music/DwarvenMusic.mp3";
    /// <summary>A slide's picture larger than this is not read (the original's are 800 × 600 at 8 bits, about 470 KiB).</summary>
    private const int MaxSlidePictureBytes = 8 << 20;
    private readonly AreaHeroDeath _heroDeath = new();
    private AreaDeathPanel? _heroDeathPanel;
    private DeathHost? _heroDeathHost;
    private object? _heroDeathSession;
    private (string Module, AreaSlideTable Table)? _deathSlides;
    private ViewerAudioHandle? _deathVoice, _deathMusic;
    private bool _heroDeathForSelfTest;
    private Func<AreaSlidePair, Image?>? _deathPictureForSelfTest;
    private Func<AreaSlidePair, bool>? _deathVoiceForSelfTest;
    private Func<bool>? _deathVoicePlayingForSelfTest;
    private AreaSlideTable? _deathSlidesForSelfTest;
    private string? _heroDeathStatus;

    /// <summary>The death screen covers the game: it owns the input and the world stands still under it.</summary>
    private bool HeroDeathCovering => _heroDeath.Covering;

    /// <summary>Per frame: follows the session's hero and moves the death on by the frame's real time.</summary>
    private void SyncHeroDeath(double delta)
    {
        // A death covering the game always runs on (a failed start scene leaves the AREA mode, and the screen must still clear).
        if (!_heroDeathForSelfTest && !HeroDeathCovering && (AutomatedVisualRun || _readyMode != TerrainViewerMode.CrashSiteArea)) return;
        TickHeroDeath(delta * 1000);
    }

    private void TickHeroDeath(double milliseconds)
    {
        _heroDeathHost ??= new(this);
        // A new session sets what counts as a death: its hero dead already starts nothing (PD1).
        if (!ReferenceEquals(_areaSession, _heroDeathSession))
        {
            _heroDeathSession = _areaSession;
            _heroDeath.Rebase(_heroDeathHost.HeroDead);
        }
        // The panel's fade moves first: a fade the death starts in this tick begins at its end, as the death's own time does.
        _heroDeathPanel?.Advance(milliseconds);
        var before = _heroDeath.Stage;
        _heroDeath.Tick(milliseconds, _heroDeathHost);
        if (_heroDeath.Stage != before)
        {
            _heroDeathStatus = $"{_heroDeath.Stage}{(_heroDeath.ResetRefusal is { } refused ? " (reset refused: " + refused + ")" : "")}{(_heroDeath.ResetAbandoned ? " (reset abandoned)" : "")}";
            GD.Print($"ARC-UI-026 hero death stage={_heroDeath.Stage} slide={_heroDeath.Slide?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}"
                + $" shown={_heroDeath.Shown}{(_heroDeath.ResetRefusal is { } why ? " refused=" + why : "")}");
        }
        if (!_heroDeath.Covering && _heroDeathPanel is { IsOpen: true } panel) panel.Close();
    }

    /// <summary>Any input while the death screen covers the game; true when it took it (it takes all).</summary>
    private bool HandleHeroDeathInput(InputEvent input)
    {
        if (!HeroDeathCovering) return false;
        // The original ends a slide on a key's release and on the left, right and middle button releases (its mouse events 1, 3
        // and 5); presses do nothing (PD7). A press is taken all the same, so the world never sees it.
        var acted = input switch
        {
            InputEventKey { Pressed: false } key => _heroDeath.Input((key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode) == Key.Escape, _heroDeathHost!),
            InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle } => _heroDeath.Input(false, _heroDeathHost!),
            _ => false,
        };
        if (acted) GD.Print($"ARC-UI-026 hero death input stage={_heroDeath.Stage}");
        return true;
    }

    /// <summary>Controller: confirm ends the slide, cancel the show (as a key and Escape).</summary>
    private void HandleHeroDeathPulse(IntentFrame intent)
    {
        if (intent.Cancel) _heroDeath.Input(true, _heroDeathHost!);
        else if (intent.Confirm) _heroDeath.Input(false, _heroDeathHost!);
    }

    /// <summary>The scene flow's start scene is there (the main menu opened, or the scene failed and shows why): the death's
    /// black screen fades in over it.</summary>
    private void HeroDeathSceneReady()
    {
        if (_heroDeathHost is null || _heroDeath.Stage != AreaDeathStage.Reset) return;
        _heroDeath.MenuReady(_heroDeathHost);
    }

    /// <summary>The slide table of the shown module, read once a module (none in the self-test or without a game folder).</summary>
    private AreaSlideTable DeathSlides()
    {
        if (_deathSlidesForSelfTest is { } synthetic) return synthetic;
        if (_arguments?.ModulePath is not { } module || SelfTestBootstrap) return AreaSlideTable.None("no game folder");
        if (_deathSlides is { } cached && cached.Module == module) return cached.Table;
        var table = AreaSoundCatalog() is not null && _soundContent?.Stack is { } stack ? AreaSlideTable.Load(stack)
            : AreaSlideTable.None("no content stack: " + (_soundContent?.Detail ?? "not opened"));
        GD.Print("ARC-UI-026 slide table " + table.Detail);
        _deathSlides = (module, table);
        return table;
    }

    private AreaDeathPanel DeathPanel()
    {
        if (_heroDeathPanel is null) { _heroDeathPanel = new AreaDeathPanel(); AddChild(_heroDeathPanel); }
        return _heroDeathPanel;
    }

    /// <summary>A test's or smoke's cleanup: drops a death in any stage with its screen and sounds.</summary>
    private void StopHeroDeath()
    {
        StopDeathSound(ref _deathVoice); StopDeathSound(ref _deathMusic);
        _heroDeathPanel?.Close();
        _heroDeath.Stop(_heroDeathHost?.HeroDead ?? false);
    }

    private void StopDeathSound(ref ViewerAudioHandle? handle)
    {
        if (handle is { } playing) _audio?.Stop(playing);
        handle = null;
    }

    /// <summary>The viewer as the death's host.</summary>
    private sealed class DeathHost(TerrainViewerApp app) : IAreaDeathHost
    {
        public bool HeroDead => app._areaSession?.QueryHeroLife()?.Status.IsDead == true;
        public bool MenuOpen => app.MainMenuOpen || app.SaveListOpen || app._mainMenu is not null;
        public AreaSlideTable Slides => app.DeathSlides();
        public bool VoicePlaying => app._deathVoicePlayingForSelfTest is { } synthetic ? synthetic()
            : app._deathVoice is { } voice && app._audio?.IsPlaying(voice) == true;

        public void CloseWindows()
        {
            // The original closes its inventory window (with the loot and barter modes) before the slides (PD3); the rest closes
            // with the reset.
            app.EndInventory(); app.EndBarter();
        }

        public void CloseMenu()
        {
            app.EndSaveList(finished: true);
            app.EndMainMenu("the hero died");
        }

        public void Fade(bool toBlack, int milliseconds)
        {
            var panel = app.DeathPanel();
            if (toBlack && !panel.IsOpen)
            {
                panel.Open();
                // The world stands still under the screen: no walk, no held key, no world sound (the original's slide loop is modal).
                app.QuarantineWorld(); app._quickKeys.Reset(); app.StopWorldRoute("HERO_DEATH");
            }
            panel.Fade(toBlack, milliseconds);
        }

        public void BeginSlides()
        {
            // The original stops the music and loops the slides' music (PD6); the viewer stops every sound of the scene, whose
            // players stand still under the screen.
            app.StopSceneSound();
            if (app._audio is not { } audio || app.AreaSoundCatalog() is not { } catalog) return;
            var result = audio.Play(catalog.OpenPayload(catalog.ResolveFile(DeathSlidesMusic)), AudioChannel.Music,
                AudioVolumeRules.ChannelVolume(app._audioVolumes, AudioChannel.Music), loop: true);
            app._deathMusic = result.Handle;
            GD.Print($"ARC-UI-026 slides music {DeathSlidesMusic}: {result.Status}");
        }

        public bool ShowSlide(AreaSlidePair slide)
        {
            Image? image;
            if (app._deathPictureForSelfTest is { } synthetic) image = synthetic(slide);
            else
            {
                image = null;
                var bytes = app._soundContent?.Stack is { } stack ? AreaSlideTable.ReadFile(stack, slide.Image, MaxSlidePictureBytes, out var detail) : null;
                if (bytes is not null)
                {
                    image = new Image();
                    if (image.LoadBmpFromBuffer(bytes) != Error.Ok || image.IsEmpty()) image = null;
                }
                else GD.Print($"ARC-UI-026 slide {slide.Id} picture {slide.Image}: not read");
            }
            if (image is null) return false;
            app.DeathPanel().ShowPicture(ImageTexture.CreateFromImage(image));
            GD.Print($"ARC-UI-026 slide {slide.Id} picture {slide.Image} {image.GetWidth()}x{image.GetHeight()}");
            return true;
        }

        public bool StartVoice(AreaSlidePair slide)
        {
            app.StopDeathSound(ref app._deathVoice);
            if (app._deathVoiceForSelfTest is { } synthetic) return synthetic(slide);
            if (app._audio is not { } audio || app.AreaSoundCatalog() is not { } catalog) return false;
            var result = audio.Play(catalog.OpenPayload(catalog.ResolveFile(slide.Voice)), AudioChannel.Voice,
                AudioVolumeRules.ChannelVolume(app._audioVolumes, AudioChannel.Voice));
            app._deathVoice = result.Handle;
            GD.Print($"ARC-UI-026 slide {slide.Id} voice {slide.Voice}: {result.Status}");
            return result.Started;
        }

        public void EndSlide() => app.StopDeathSound(ref app._deathVoice);

        public void EndSlides()
        {
            app.StopDeathSound(ref app._deathVoice);
            app.StopDeathSound(ref app._deathMusic);
            app.DeathPanel().ShowPicture(null);
        }

        public string? Reset()
        {
            // The original prints "DEATH: Resetting game!", resets the game and the interface and starts the main menu (PD8).
            GD.Print("ARC-UI-026 hero death: resetting the game");
            return app.BeginSceneFlow(SceneFlowKind.Menu, -1, "the hero died");
        }
    }
}
