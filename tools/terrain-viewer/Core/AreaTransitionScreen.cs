using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Where the transition screen's black veil stands.</summary>
public enum AreaTransitionShade
{
    /// <summary>The game shows.</summary>
    Clear,
    /// <summary>The game fades to black.</summary>
    FadingOut,
    /// <summary>The screen is black.</summary>
    Black,
    /// <summary>The black fades away.</summary>
    FadingIn,
}

/// <summary>ARC-UI-3503: what the load screen shows behind the clock.</summary>
public enum AreaLoadPicture
{
    /// <summary>Black.</summary>
    Black,
    /// <summary>Black with the line that says a save is loading ("Last Save", ledger LS5).</summary>
    SavedGameText,
    /// <summary>The frame on screen when the load was chosen, standing still: the original's load window stays while its save loads (LS6).</summary>
    FrozenWindow,
}

/// <summary>
/// ARC-DEV-204 (B16, MIG11 presentation): the black screen of a change of place, as the original's request flags and its map load make it
/// (ledger CP1, CP3, CP7, [CP17](../../../docs/architecture/change-of-place.md#the-b16-read-of-the-executable)). Godot-free; the panel
/// draws it and the viewer's hosts move it.
/// <list type="bullet">
/// <item>The fade-out (request flag 0x2) and the fade-in (0x4) take <see cref="FadeMilliseconds"/> each, black (decision PD9: the original's
/// gamma fade waits 0 ms per step and flashes by, a units slip fixed in both rule modes).</item>
/// <item>The blank-screen flag (0x20) blacks the screen at once (<see cref="Blank"/>); the ping gives the draw back after the change
/// (<see cref="Release"/>).</item>
/// <item>The map load (the old map is closed, the new one opened sector by sector) shows black with the load indicator (art 601,
/// <c>loadindicator.art</c>, the clock) until the destination is there (<see cref="BeginLoad"/>, <see cref="EndLoad"/>). The original draws one
/// frame of the clock per loading step, so the speed follows the loading; the viewer loads in the background and plays the art at its
/// own frame rate.</item>
/// </list>
/// </summary>
public sealed class AreaTransitionScreen
{
    /// <summary>The length of a fade-out or fade-in (PD9: 48 steps over 2.0 s).</summary>
    public const int FadeMilliseconds = AreaHeroDeath.FadeTime;

    /// <summary>The interface art number of <c>loadindicator.art</c> (<c>art\interface\interface.mes</c>, the clock of 30 frames the
    /// original pushes at 0x4BBB2E).</summary>
    public const int IndicatorArt = 601;

    /// <summary>The margin between the indicator and the top-right corner of the screen, in pixels (init 0x4BBB20: two pixels).</summary>
    public const int IndicatorMargin = 2;

    private double _fadeElapsed;

    /// <summary>Where the veil stands.</summary>
    public AreaTransitionShade Shade { get; private set; } = AreaTransitionShade.Clear;

    /// <summary>True while a map load is shown (black with the indicator).</summary>
    public bool Loading { get; private set; }

    /// <summary>ARC-UI-3503: what shows behind the clock, from the load's start until the screen is given back.</summary>
    public AreaLoadPicture Picture { get; private set; }

    /// <summary>The time the indicator has run, in milliseconds.</summary>
    public double IndicatorMilliseconds { get; private set; }

    /// <summary>How black the veil is, 0 (the game shows) to 1 (black).</summary>
    public double Alpha => Loading ? 1 : Shade switch
    {
        AreaTransitionShade.FadingOut => Math.Clamp(_fadeElapsed / FadeMilliseconds, 0, 1),
        AreaTransitionShade.Black => 1,
        AreaTransitionShade.FadingIn => 1 - Math.Clamp(_fadeElapsed / FadeMilliseconds, 0, 1),
        _ => 0,
    };

    /// <summary>True when anything of the screen shows over the game.</summary>
    public bool Covering => Loading || Shade != AreaTransitionShade.Clear;

    /// <summary>True when the clock shows.</summary>
    public bool IndicatorShown => Loading;

    /// <summary>True when a fade-out is on its way to black: the change waits for <see cref="Shade"/> to be black.</summary>
    public bool FadingOut => Shade == AreaTransitionShade.FadingOut;

    /// <summary>True when the screen is black and no fade moves it.</summary>
    public bool IsBlack => Shade == AreaTransitionShade.Black;

    /// <summary>Starts the fade-out (request flag 0x2 of the local player's request).</summary>
    public void BeginFadeOut()
    {
        if (Shade == AreaTransitionShade.Black) return;
        Picture = AreaLoadPicture.Black;
        // From a fade-in in progress the veil goes on from its own darkness, not from clear.
        _fadeElapsed = Shade == AreaTransitionShade.FadingIn ? Math.Clamp(1 - Alpha, 0, 1) * FadeMilliseconds : 0;
        Shade = AreaTransitionShade.FadingOut;
    }

    /// <summary>Blacks the screen at once (request flag 0x20 of the local player's request, CP1).</summary>
    public void Blank() => Shade = AreaTransitionShade.Black;

    /// <summary>Starts showing a map load: the clock over <paramref name="picture"/>, from the first frame of the clock. The picture lasts until
    /// the screen is given back; a load that goes on while one shows (the saved map opening) and asks for plain black keeps it.</summary>
    public void BeginLoad(AreaLoadPicture picture = AreaLoadPicture.Black)
    {
        if (!Loading) { IndicatorMilliseconds = 0; Picture = AreaLoadPicture.Black; }
        if (picture != AreaLoadPicture.Black) Picture = picture;
        Loading = true;
        if (Shade != AreaTransitionShade.FadingOut) Shade = AreaTransitionShade.Black;
    }

    /// <summary>The destination is there (or the load failed): the clock goes, the screen stays black until it is released or fades in.</summary>
    public void EndLoad() => Loading = false;

    /// <summary>The fade-in at the end of a change that ran to its end (request flag 0x4). From clear it has nothing to fade.</summary>
    public void BeginFadeIn()
    {
        Loading = false;
        Picture = AreaLoadPicture.Black;
        if (Shade == AreaTransitionShade.Clear) return;
        _fadeElapsed = 0;
        Shade = AreaTransitionShade.FadingIn;
    }

    /// <summary>Gives the draw back (the ping's end, CP2): a black screen clears; a fade in progress runs on.</summary>
    public void Release()
    {
        Loading = false;
        Picture = AreaLoadPicture.Black;
        if (Shade == AreaTransitionShade.Black) Shade = AreaTransitionShade.Clear;
    }

    /// <summary>Clears everything at once (a refused or failed change, a reset).</summary>
    public void Clear()
    {
        Loading = false;
        Picture = AreaLoadPicture.Black;
        Shade = AreaTransitionShade.Clear;
        _fadeElapsed = 0;
        IndicatorMilliseconds = 0;
    }

    /// <summary>Moves the screen on by <paramref name="milliseconds"/> of real time.</summary>
    public void Advance(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0) return;
        if (Loading) IndicatorMilliseconds += milliseconds;
        if (Shade is not (AreaTransitionShade.FadingOut or AreaTransitionShade.FadingIn)) return;
        _fadeElapsed += milliseconds;
        if (_fadeElapsed < FadeMilliseconds) return;
        Shade = Shade == AreaTransitionShade.FadingOut ? AreaTransitionShade.Black : AreaTransitionShade.Clear;
        _fadeElapsed = 0;
    }

    /// <summary>The frame of the clock to draw, looping over <paramref name="frameCount"/> frames at <paramref name="framesPerSecond"/>
    /// (a missing frame rate runs at <see cref="DefaultFramesPerSecond"/>).</summary>
    public int IndicatorFrame(int frameCount, int framesPerSecond)
    {
        if (frameCount <= 0) return 0;
        var rate = framesPerSecond > 0 ? framesPerSecond : DefaultFramesPerSecond;
        return (int)(IndicatorMilliseconds * rate / 1000 % frameCount);
    }

    /// <summary>The frame rate of the clock when the art header gives none.</summary>
    public const int DefaultFramesPerSecond = 15;
}
