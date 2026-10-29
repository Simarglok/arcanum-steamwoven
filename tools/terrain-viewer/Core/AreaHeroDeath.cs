using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

/// <summary>A slide's picture and voice: the two first words of its message in <c>rules\slide.mes</c> (ledger PD5).</summary>
/// <param name="Image">The picture's virtual path, <c>slide/&lt;word 1&gt;</c>.</param>
/// <param name="Voice">The voice's virtual path, <c>sound/speech/slide/&lt;word 2&gt;</c>.</param>
public sealed record AreaSlidePair(int Id, string Image, string Voice);

/// <summary>
/// ARC-UI-026: the original's slide table <c>rules\slide.mes</c>, read at module load (ledger PD5). A slide's message names
/// its picture and its voice, split as the C library's <c>strtok</c> splits at space, tab and line feed; a message with fewer
/// than two words, or none, is no slide. Godot-free.
/// </summary>
public sealed class AreaSlideTable
{
    public const string TablePath = "rules/slide.mes";
    public const string ImageFolder = "slide/";
    public const string VoiceFolder = "sound/speech/slide/";
    /// <summary>A word longer than this is refused (a path the content stack would refuse anyway).</summary>
    public const int MaxWord = 128;
    private static readonly char[] Separators = [' ', '\t', '\n'];
    private readonly Func<int, string?> _messages;

    private AreaSlideTable(Func<int, string?> messages, bool loaded, string detail)
    {
        _messages = messages; Loaded = loaded; Detail = detail;
    }

    /// <summary>True when the table was read (the original queues its default death slide only then, PD4).</summary>
    public bool Loaded { get; }
    public string Detail { get; }

    /// <summary>No table (no game folder, the self-test, or a failed read): no slide shows.</summary>
    public static AreaSlideTable None(string reason) => new(_ => null, false, reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>A table over a message lookup (tests).</summary>
    public static AreaSlideTable From(Func<int, string?> messages, string detail) =>
        new(messages ?? throw new ArgumentNullException(nameof(messages)), true, detail ?? throw new ArgumentNullException(nameof(detail)));

    /// <summary>Reads <c>rules\slide.mes</c> from the stack's module-load winner; any failure gives no table, with the reason.</summary>
    public static AreaSlideTable Load(ContentStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (AreaStartingHero.Table(stack, TablePath, ContentLoadPhase.ModuleLoaded, required: true, out var detail) is not { } table)
            return None($"{TablePath} not read ({detail})");
        return new(number => table.Find(number) is { } message
            && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } text } ? text : null,
            true, $"{TablePath} read");
    }

    /// <summary>The slide's picture and voice, or null when its message is missing or names fewer than two files.</summary>
    public AreaSlidePair? Pair(int id)
    {
        if (_messages(id) is not { } text) return null;
        // Words past the second are not read.
        var words = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words[0].Length > MaxWord || words[1].Length > MaxWord) return null;
        return new(id, ImageFolder + words[0], VoiceFolder + words[1]);
    }

    /// <summary>Reads a slide's picture from the stack's module-load winner (the original opens it while the game runs);
    /// null with the reason when no source holds it, it cannot be read or it is larger than <paramref name="maxBytes"/>.</summary>
    public static byte[]? ReadFile(ContentStack stack, string path, int maxBytes, out string detail)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        // A path that is no virtual path has no candidate either.
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.ModuleLoaded) is not { } winner) { detail = "no source holds it"; return null; }
        if (winner.Length > maxBytes) { detail = "over the size limit"; return null; }
        var read = stack.Read(winner);
        if (read.Status != ContentReadStatus.Read || read.Bytes is null) { detail = read.Status.ToString(); return null; }
        detail = "read";
        return read.Bytes;
    }
}

/// <summary>The stages of the hero's death (ledger PD1–PD9).</summary>
public enum AreaDeathStage
{
    /// <summary>The hero lives (or was dead already when the session came): nothing runs.</summary>
    Watching,
    /// <summary>The hero died: the death event waits (5 s; 0.3 s more while a menu was open).</summary>
    Waiting,
    /// <summary>The screen fades to black before the slides.</summary>
    FadeOut,
    /// <summary>A slide's picture fades in.</summary>
    SlideIn,
    /// <summary>A slide shows with its voice until the voice ends, a key or a mouse button.</summary>
    Slide,
    /// <summary>The slide fades out.</summary>
    SlideOut,
    /// <summary>ARC-UI-028 (Steamwoven only): the box asks whether to load the hero's newest save; ARC-WORLD-120: over the shown
    /// death slide (over the black screen when no slide showed).</summary>
    Asking,
    /// <summary>ARC-UI-028: the hero's newest save loads under the black screen (a save on another map, or beyond the shown window
    /// of this one, loads that map first); ARC-WORLD-120: the slide fades to black meanwhile.</summary>
    Loading,
    /// <summary>The game resets to the main menu; the screen stays black until the menu is there.</summary>
    Reset,
    /// <summary>The main menu fades in (after a load, the loaded game).</summary>
    MenuFadeIn,
}

/// <summary>What the death needs from the game around it (the viewer, or a test).</summary>
public interface IAreaDeathHost
{
    /// <summary>The hero of the current session is dead (the original's death predicate on the PC, PD1).</summary>
    bool HeroDead { get; }
    /// <summary>A main-menu window (the in-play menu, its save and load windows, the options) is open (PD3).</summary>
    bool MenuOpen { get; }
    AreaSlideTable Slides { get; }
    /// <summary>Closes the windows open over play before the slides (the original closes its inventory window, PD3).</summary>
    void CloseWindows();
    /// <summary>Closes the open main-menu windows (PD3).</summary>
    void CloseMenu();
    /// <summary>Fades the screen to black (<paramref name="toBlack"/>) or from black over <paramref name="milliseconds"/> (PD7).</summary>
    void Fade(bool toBlack, int milliseconds);
    /// <summary>The slides begin: the music stops and the slides' music loops (PD6).</summary>
    void BeginSlides();
    /// <summary>Shows a slide's picture (still black until it fades in); false when it cannot be read.</summary>
    bool ShowSlide(AreaSlidePair slide);
    /// <summary>Starts the slide's voice; false when nothing plays (missing, unreadable or refused).</summary>
    bool StartVoice(AreaSlidePair slide);
    bool VoicePlaying { get; }
    /// <summary>The slide ends: its voice stops.</summary>
    void EndSlide();
    /// <summary>The slides end: their music stops and the picture goes.</summary>
    void EndSlides();
    /// <summary>Ends the game and opens the main menu (PD8); null when that began, else why it could not, with
    /// <paramref name="lasting"/> true when trying again cannot help (no start scene is known).</summary>
    string? Reset(out bool lasting);
    /// <summary>ARC-UI-028: the session's rule setting (the settings' one when no session is there).</summary>
    LifecycleRuleProfile Rules { get; }
    /// <summary>ARC-WORLD-120: the name of the newest save of the dead hero (the owner's rule: only this hero's saves are offered),
    /// null when there is none.</summary>
    string? HeroSave { get; }
    /// <summary>ARC-UI-028: shows the box asking whether to load <paramref name="save"/>, with the <paramref name="notice"/> of a
    /// refused load (null: none); a box shown already is shown again. ARC-WORLD-120: over the shown slide, else the black screen.</summary>
    void Ask(string save, string? notice);
    /// <summary>ARC-UI-028: the box goes.</summary>
    void EndAsk();
    /// <summary>ARC-UI-028: loads the save as the newest-save key does (the game as it is shown is replaced); null when the
    /// load began, else why nothing changed.</summary>
    string? LoadLastSave(string save);
    /// <summary>ARC-UI-028: no load is still under way (a save on another map has had its map loaded, or failed to).</summary>
    bool LoadSettled { get; }
}

/// <summary>
/// ARC-UI-026: what follows the hero's death, as the original 1.0.7.4 does it in single player (ledger PD1–PD9; FR-MEDIA-002 in
/// part, FR-CHAR-026). The original starts a 5 s real-time event when the PC's dying animation ends; Steamwoven starts it when
/// the session's hero becomes dead, as the viewer plays no dying animation for the hero. When it fires and the hero is still
/// dead, the windows over play close; an open main-menu window closes and the event comes again 0.3 s later. Then the screen
/// fades to black (2 s) and the death slides show: the slides a script queued (none here: the installed data has no death
/// slide script, and Steamwoven runs none), else the default slide 0 when <c>rules\slide.mes</c> was read. Each slide fades in
/// (2 s), its voice plays, and it ends when the voice ends (at once without one), on a key's or a mouse button's release; Escape
/// also ends the show. Input during a fade does nothing. The original's fades are meant to last 2 s but its gamma fade waits
/// 2.0 / 48 truncated to 0 ms per step, so they flash by (a technical bug, PD9); Steamwoven fades over 2 s in both rule modes. After its fade out (2 s) the next slide comes; a slide whose message or picture
/// is missing is skipped. Then the game resets to the main menu (whose Escape exits, MM6), which fades in (2 s). There is no
/// window with a choice in the original: a load is the main menu's.
/// ARC-UI-028 (a Steamwoven addition the owner asked for; <see cref="LifecycleRuleProfile.DeathResetsToMainMenu"/> keeps the
/// original under the Original setting), as the owner settled it in ARC-WORLD-120: when the dead hero has a save, the box asks
/// whether to load the newest of them as soon as the death slide has faded in (its voice and the slides' music go on under it;
/// the slide no longer ends on a key or the voice's end); when no slide showed, at the point of the reset over the black screen.
/// Yes loads it as the newest-save key does while the slide fades to black, and the loaded game fades in (2 s) once its map is
/// there; a refused load, or one that brought no living hero, keeps the box with the reason; No ends the slide as the original
/// does (it fades out) and resets. Without a save of this hero the original's show and reset follow. Caller-serialized; the host
/// ticks it every frame.
/// </summary>
public sealed class AreaHeroDeath
{
    public const int DeathDelay = 5000;
    public const int MenuRetryDelay = 300;
    public const int FadeTime = 2000;
    public const int DefaultSlide = 0;
    /// <summary>ARC-UI-028: the box's question, the project's text (the game's message files hold none that asks this; its buttons
    /// are the quit box's Yes and No). ARC-WORLD-120: only the dead hero's saves are offered.</summary>
    public const string LoadQuestion = "You have died. Load this hero's last save?";
    /// <summary>ARC-UI-028: the box's notice when a load ended without a living hero.</summary>
    public const string NoLivingHero = "the load brought no living hero (the save's map did not load, or its hero is dead)";
    /// <summary>The original's slide queue holds up to 100 slides.</summary>
    public const int MaxQueued = 100;
    /// <summary>A reset whose main menu has not come this long after its first try (refused all along, or begun and never
    /// finished) gives up (ms); a lasting refusal gives up at once. The original's reset cannot be refused: giving up fades the
    /// screen in over the game as it is, with the refusal kept.</summary>
    public const int ResetTimeout = 120_000;
    private readonly List<int> _queue = [];
    private bool _wasDead;
    private bool _stopShow;
    private AreaSlidePair? _slide;
    private double _resetElapsed;
    private string? _save;
    // ARC-WORLD-120: the box asked during this death (over the slide or the black screen), so the end of the show resets.
    private bool _asked;

    public AreaDeathStage Stage { get; private set; }
    /// <summary>The time left in a waiting or fading stage (ms).</summary>
    public double Remaining { get; private set; }
    public IReadOnlyList<int> Queue => _queue;
    /// <summary>The index in <see cref="Queue"/> of the slide being shown; -1 before the first.</summary>
    public int Index { get; private set; } = -1;
    public AreaSlidePair? Slide => _slide;
    /// <summary>The slides shown so far in this death.</summary>
    public int Shown { get; private set; }
    /// <summary>Why the reset did not begin (it is tried again 0.3 s later, until <see cref="ResetTimeout"/>); null when it did.</summary>
    public string? ResetRefusal { get; private set; }
    /// <summary>True when the reset gave up (refused too often, or no menu came): the game stays as it was.</summary>
    public bool ResetAbandoned { get; private set; }
    /// <summary>ARC-UI-028: the save the box offers or the load is loading; null otherwise.</summary>
    public string? OfferedSave => Stage is AreaDeathStage.Asking or AreaDeathStage.Loading ? _save : null;
    /// <summary>ARC-UI-028: why the last Yes did not load (the box stays); null when it did or none was given.</summary>
    public string? LoadRefusal { get; private set; }
    /// <summary>ARC-UI-028: true when the death ended with the newest save loaded (no reset).</summary>
    public bool Loaded { get; private set; }
    /// <summary>ARC-UI-028: true when the load's map had not come after <see cref="ResetTimeout"/>: the screen faded in over
    /// whatever is shown.</summary>
    public bool LoadAbandoned { get; private set; }
    /// <summary>True while the death screen covers the game (from the fade to black until the menu has faded in).</summary>
    public bool Covering => Stage is not (AreaDeathStage.Watching or AreaDeathStage.Waiting);

    /// <summary>A new session: a hero already dead in it starts nothing (only a death does, PD1). A death waiting already keeps
    /// waiting, as the original's event fires whatever the session (it checks the hero again then).</summary>
    public void Rebase(bool heroDead)
    {
        if (Stage == AreaDeathStage.Watching) _wasDead = heroDead;
    }

    /// <summary>Advances by <paramref name="milliseconds"/> of real time.</summary>
    public void Tick(double milliseconds, IAreaDeathHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!double.IsFinite(milliseconds) || milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        switch (Stage)
        {
            case AreaDeathStage.Watching:
                var dead = host.HeroDead;
                if (dead && !_wasDead) { Stage = AreaDeathStage.Waiting; Remaining = DeathDelay; }
                _wasDead = dead;
                return;
            case AreaDeathStage.Waiting:
                if ((Remaining -= milliseconds) > 0) return;
                Fire(host);
                return;
            case AreaDeathStage.FadeOut:
                if ((Remaining -= milliseconds) > 0) return;
                if (_queue.Count == 0) { EndOfShow(host); return; }
                host.BeginSlides();
                Next(host);
                return;
            case AreaDeathStage.SlideIn:
                if ((Remaining -= milliseconds) > 0) return;
                Stage = AreaDeathStage.Slide;
                var voiced = host.StartVoice(_slide!);
                // ARC-WORLD-120: the box asks over the slide, which stays until the answer.
                if (Offer(host)) return;
                // Without a voice the slide ends at once (PD7).
                if (!voiced) EndSlide(host);
                return;
            case AreaDeathStage.Slide:
                if (!host.VoicePlaying) EndSlide(host);
                return;
            case AreaDeathStage.SlideOut:
                if ((Remaining -= milliseconds) > 0) return;
                if (_stopShow) { EndShow(host); return; }
                Next(host);
                return;
            case AreaDeathStage.Reset:
                // Waiting for the main menu (MenuReady), or for the next try of a refused reset; neither waits forever.
                if ((_resetElapsed += milliseconds) >= ResetTimeout) { Abandon(host); return; }
                if (ResetRefusal is null || (Remaining -= milliseconds) > 0) return;
                BeginReset(host);
                return;
            case AreaDeathStage.Loading:
                // The slide's fade to black first; then the loaded game's map. A load that never settles gives up as a reset does.
                Remaining -= milliseconds;
                if (!host.LoadSettled && (_resetElapsed += milliseconds) < ResetTimeout) return;
                if (Remaining > 0) return;
                // A load that brought no living hero (the other map failed and the old game came back, or the save's hero is
                // dead) asks again, with why.
                if (host.LoadSettled && host.HeroDead)
                {
                    // The slide faded out meanwhile: the box asks again over the black screen.
                    if (_slide is not null) { host.EndSlides(); _slide = null; }
                    Loaded = false; LoadRefusal = NoLivingHero;
                    Stage = AreaDeathStage.Asking;
                    host.Ask(_save!, NoLivingHero);
                    return;
                }
                LoadAbandoned = !host.LoadSettled;
                if (_slide is not null) { host.EndSlides(); _slide = null; }
                host.Fade(false, FadeTime);
                Stage = AreaDeathStage.MenuFadeIn; Remaining = FadeTime;
                return;
            case AreaDeathStage.MenuFadeIn:
                if ((Remaining -= milliseconds) > 0) return;
                Stage = AreaDeathStage.Watching;
                _wasDead = host.HeroDead;
                return;
        }
    }

    /// <summary>A key's release (<paramref name="escape"/> for Escape) or a mouse button's release while the death screen covers
    /// the game: it ends the shown slide, Escape the whole show (PD7). True when it acted; input in any other stage does nothing.</summary>
    public bool Input(bool escape, IAreaDeathHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Stage != AreaDeathStage.Slide) return false;
        _stopShow |= escape;
        EndSlide(host);
        return true;
    }

    /// <summary>ARC-UI-028: the box's answer (Yes, Return or Y; No, Escape or N). Yes loads the offered save, or keeps the box with
    /// the refusal; No resets as the original. ARC-WORLD-120: a slide still shown fades out first, on Yes while the save loads, on No
    /// before the reset. True when it acted; in any other stage it does nothing.</summary>
    public bool Answer(bool yes, IAreaDeathHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Stage != AreaDeathStage.Asking) return false;
        if (!yes)
        {
            LoadRefusal = null; host.EndAsk();
            if (_slide is not null) { _stopShow = true; EndSlide(host); }
            else BeginReset(host);
            return true;
        }
        if (host.LoadLastSave(_save!) is { } refused) { LoadRefusal = refused; host.Ask(_save!, refused); return true; }
        LoadRefusal = null; Loaded = true;
        host.EndAsk();
        Remaining = 0; _resetElapsed = 0;
        if (_slide is not null) { host.EndSlide(); host.Fade(true, FadeTime); Remaining = FadeTime; }
        Stage = AreaDeathStage.Loading;
        return true;
    }

    /// <summary>The main menu is there after the reset (or the start scene failed and shows its failure): it fades in.</summary>
    public void MenuReady(IAreaDeathHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Stage != AreaDeathStage.Reset || ResetRefusal is not null) return;
        host.Fade(false, FadeTime);
        Stage = AreaDeathStage.MenuFadeIn; Remaining = FadeTime;
    }

    private void Fire(IAreaDeathHost host)
    {
        // The event checks the hero again: one brought back to life meanwhile is not dead (PD3).
        if (!host.HeroDead) { Stage = AreaDeathStage.Watching; _wasDead = false; return; }
        host.CloseWindows();
        if (host.MenuOpen) { host.CloseMenu(); Remaining = MenuRetryDelay; return; }
        _queue.Clear(); Index = -1; Shown = 0; _stopShow = false; _slide = null; ResetRefusal = null; ResetAbandoned = false; _resetElapsed = 0;
        _save = null; _asked = false; LoadRefusal = null; Loaded = false; LoadAbandoned = false;
        if (host.Slides.Loaded) _queue.Add(DefaultSlide);
        host.Fade(true, FadeTime);
        Stage = AreaDeathStage.FadeOut; Remaining = FadeTime;
    }

    private void Next(IAreaDeathHost host)
    {
        while (++Index < _queue.Count)
        {
            // A slide whose message names no picture and voice is skipped (PD5).
            if (host.Slides.Pair(_queue[Index]) is not { } slide) continue;
            _slide = slide;
            // A picture that cannot be read shows nothing: no fade in, no voice; the slide fades out (PD7).
            if (!host.ShowSlide(slide)) { Stage = AreaDeathStage.SlideOut; Remaining = FadeTime; host.Fade(true, FadeTime); return; }
            Shown++;
            host.Fade(false, FadeTime);
            Stage = AreaDeathStage.SlideIn; Remaining = FadeTime;
            return;
        }
        EndShow(host);
    }

    private void EndSlide(IAreaDeathHost host)
    {
        host.EndSlide();
        host.Fade(true, FadeTime);
        Stage = AreaDeathStage.SlideOut; Remaining = FadeTime;
    }

    private void EndShow(IAreaDeathHost host)
    {
        host.EndSlides();
        _slide = null;
        EndOfShow(host);
    }

    /// <summary>The point of the original's reset: under the Steamwoven setting, with a save of this hero there and no box shown
    /// over a slide, the box asks first (ARC-UI-028).</summary>
    private void EndOfShow(IAreaDeathHost host)
    {
        if (!_asked && Offer(host)) return;
        BeginReset(host);
    }

    /// <summary>ARC-UI-028 / ARC-WORLD-120: under the Steamwoven setting, once per death, the box asks to load the hero's newest save
    /// when there is one; true when it asks.</summary>
    private bool Offer(IAreaDeathHost host)
    {
        if (_asked || host.Rules.DeathResetsToMainMenu || host.HeroSave is not { } save) return false;
        _save = save; _asked = true;
        Stage = AreaDeathStage.Asking; Remaining = 0;
        host.Ask(save, null);
        return true;
    }

    private void BeginReset(IAreaDeathHost host)
    {
        Stage = AreaDeathStage.Reset;
        ResetRefusal = null; Remaining = 0;
        // A host whose menu opens at once calls MenuReady from inside Reset.
        if (host.Reset(out var lasting) is not { } refusal) return;
        ResetRefusal = refusal; Remaining = MenuRetryDelay;
        if (lasting) Abandon(host);
    }

    /// <summary>The reset gave up: the screen fades in over the game as it is (the refusal stays readable).</summary>
    private void Abandon(IAreaDeathHost host)
    {
        ResetAbandoned = true;
        host.Fade(false, FadeTime);
        Stage = AreaDeathStage.MenuFadeIn; Remaining = FadeTime;
    }

    /// <summary>Drops a death in any stage (a test's or smoke's cleanup): back to watching, the host's screen and sounds being the
    /// caller's to clear.</summary>
    public void Stop(bool heroDead)
    {
        Stage = AreaDeathStage.Watching; Remaining = 0; _wasDead = heroDead; _slide = null; _stopShow = false; _save = null; _asked = false;
    }
}
