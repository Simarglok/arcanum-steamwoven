using Arcanum.Data.Magic;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SCRIPT-1592 (ledger WE4, WE5): the script action 120 (play script eye candy) in the AREA view. The dialog host and the use host (use, tile, sector, dying and combat-point scripts, item use) collect each run's requests; every frame
// they are taken here and played on their objects as overlay runs of the script list (rules\ScriptEyeCandy.mes, AreaEyeCandyHost.PlayScript), which the eye-candy draw shows like a spell's. ARC-SCRIPT-1829: any object of the shown map
// with a cell plays (scenery, portals, containers and ground items by their row or placed cell, created critters, summoned objects: EyeCandyCell); the entry's art is turned to the object's facing (the art modifier 0x502880 into 0x502930);
// the entry's sound plays at the object's cell once (a looping entry's until its run ends, the end node 0x431B20). Not ready: the tinting flags of an entry without art (0x43EF10 sets the object's translucent flag; none installed), the sound's size
// class of scenery (flags 0x40/0x80/0x100: large is used), a static entry (the original installs nothing for a script's play, so none shows) and the NPCs' own use scripts (the AI host) and the item host's moves of the hero (ARC-MAGIC-1744 and the
// equipment windows) play none yet. ARC-MAGIC-1830: the wield scripts of an NPC's rewield (the TraitIdx magick and tech points, the AI gate's re-wield) and of its best-items wield (regeneration, a created critter's stocking) play theirs, as
// do the hero's TraitIdx rewield scripts, queued by the host that applied them (QueueScriptEyeCandies).
public partial class TerrainViewerApp
{
    // Smoke seams and the log: the requests taken, and those the host started.
    private int _scriptEyeCandyTaken, _scriptEyeCandyPlayed, _scriptEyeCandySounds, _scriptEyeCandyStopTaken, _scriptEyeCandyStopped;
    // ARC-SCRIPT-1829: the rotation the last plays turned their art to, and the looping voices of the runs still shown (stopped when their run ends).
    private readonly List<(string Owner, int Number, int Rotation, int Sound)> _scriptEyeCandyTurnedForSelfTest = [];
    private readonly Dictionary<Arcanum.Simulation.Goals.GoalHandle, ViewerAudioHandle> _scriptEyeCandyVoices = [];
    private readonly List<(string Owner, int Number)> _scriptEyeCandyPlayedForSelfTest = [];
    private readonly List<(string Owner, int Number)> _scriptEyeCandyStoppedForSelfTest = [];
    // ARC-MAGIC-1830: the requests of the item event scripts a session move ran (an NPC's rewield or wield), queued by the code that applied the move and taken with the script hosts' above.
    private readonly List<AreaScriptEyeCandy> _queuedScriptEyeCandies = [];

    /// <summary>ARC-MAGIC-1830: queues the play requests among the eye candy requests of item event scripts a session move ran (<see cref="AreaScriptEyeCandy.Of(IEnumerable{ScrEyeCandy})"/>) for the next <see cref="SyncScriptEyeCandies"/>.</summary>
    private void QueueScriptEyeCandies(IEnumerable<ScrEyeCandy> candies) => _queuedScriptEyeCandies.AddRange(AreaScriptEyeCandy.Of(candies));

    /// <summary>The play requests the script hosts collected, played now on the shown map (the viewer asks for them every frame, before the eye candies are drawn); a request that cannot play (no cast host, another map, an entry without a run) is logged and dropped.</summary>
    private void SyncScriptEyeCandies()
    {
        StopEndedScriptEyeCandyVoices();
        var requests = new List<AreaScriptEyeCandy>();
        if (_conversationHost?.Scripts is { } talk) requests.AddRange(talk.TakeEyeCandies());
        if (_useScriptHost is { } use) requests.AddRange(use.Host.TakeEyeCandies());
        requests.AddRange(_queuedScriptEyeCandies);
        _queuedScriptEyeCandies.Clear();
        if (requests.Count == 0) return;
        if (_readyMode != TerrainViewerMode.Area || _readyArea is null) { GD.Print($"ARC-SCRIPT-1592 script-eye-candy dropped {requests.Count} request(s) (no area is shown)"); return; }
        var caster = SpellCasterNow();
        foreach (var request in requests)
        {
            if (request.Stop) { StopScriptEyeCandy(request, caster); continue; }
            _scriptEyeCandyTaken++;
            if (caster is null) { GD.Print($"ARC-SCRIPT-1592 script-eye-candy dropped number={request.Number} object={request.Object.Code} (no cast host)"); continue; }
            // The hero is the PC whatever map the run names; any other object must be of the shown map.
            string owner;
            if (request.Object.Code == AreaScriptHost.PcCode) owner = SessionSpellCaster.Hero;
            else if (request.Object.Map == CurrentAreaMap) owner = caster.CastIdOf(request.Object.Code);
            else { GD.Print($"ARC-SCRIPT-1592 script-eye-candy dropped number={request.Number} object={request.Object.Code} (map {request.Object.Map} is not the shown map)"); continue; }
            // An object the draw has no cell for (an item that is neither a map row nor on the ground, or an object an earlier action of the run destroyed) would hold a run nobody sees: not played (ledger VT9; a map item the hero carried keeps its row's cell, which is a recorded limit).
            if (_areaSession is not { } session || EyeCandyCell(session, owner) is not { } cell) { GD.Print($"ARC-SCRIPT-1592 script-eye-candy dropped number={request.Number} object={owner} (no cell to draw on)"); continue; }
            if (EyeCandyHostNow() is not { } host) continue;
            // ARC-SCRIPT-1829: the entry's art turns to the object's facing (0x502880 into 0x502930) and its sound plays at the object's cell (0x41B930; a looping one is held until the run ends).
            var rotation = ScriptEyeCandyRotation(owner);
            var play = host.PlayScriptSounded(owner, request.Number, rotation);
            if (play.Started) { _scriptEyeCandyPlayed++; _scriptEyeCandyPlayedForSelfTest.Add((owner, request.Number)); _scriptEyeCandyTurnedForSelfTest.Add((owner, request.Number, rotation, play.Sound)); }
            PlayScriptEyeCandySound(play, cell);
            GD.Print($"ARC-SCRIPT-1592 script-eye-candy object={owner} number={request.Number} played={play.Started} rotation={rotation} sound={play.Sound}{(play.Loops ? " loop" : "")}");
        }
    }

    /// <summary>ARC-SCRIPT-1829 (the art modifier): the rotation an object's art carries, which a script's eye candy turns to: the hero's facing, an NPC's mover facing, a static object's stored art's rotation (0 for an art class without one, an item).</summary>
    private int ScriptEyeCandyRotation(string owner)
    {
        if (owner == SessionSpellCaster.Hero) return _worldActor.Session is AreaGridSession ? _worldActor.State.Facing & 7 : 0;
        if (AreaAiHostNow() is { } ai && ReferenceEquals(ai.Session, _areaSession) && ai.Movers.Of(owner) is { } mover) return (int)mover.Facing;
        return _spellCastMap?.Row(owner) is { } row && row.EffectiveFields.FirstOrDefault(f => f.Slot == 0)?.Number is { } art ? AreaScenePlan.ArtRotation(unchecked((uint)art)) : 0;
    }

    /// <summary>ARC-SCRIPT-1829: the entry's sound at the object's cell, the size class of any object but scenery (large); a looping one keeps its voice for the run.</summary>
    private void PlayScriptEyeCandySound(ScriptEyeCandyPlay play, CellCoord cell)
    {
        if (play.Sound < 0) return;
        _scriptEyeCandySounds++;
        var played = PlayCueAt(new Arcanum.Simulation.Sound.SoundCue(play.Sound), cell.X, cell.Y, Arcanum.Presentation.Audio.PositionalSize.Large, "script eye candy", loop: play.Loops);
        if (play.Loops && play.Run is { } run && played.Result is { Started: true, Handle: { } voice }) _scriptEyeCandyVoices[run] = voice;
    }

    /// <summary>ARC-SCRIPT-1829 (the run's end node stops the sound handle): a looping eye candy's voice stops when its run is over (a run a map change or a load carries on keeps drawing without it: a recorded limit, ledger VT9) (ended, taken off, or the map changed: the goal is done or gone).</summary>
    private void StopEndedScriptEyeCandyVoices()
    {
        if (_scriptEyeCandyVoices.Count == 0) return;
        // The run shows only after its begin, so it is alive while the goal table holds it undone (not while it is shown).
        var goals = ShownEyeCandyHost is { } host ? host.Session.QueryGoals() : null;
        foreach (var run in _scriptEyeCandyVoices.Keys.Where(r => goals is null || !goals.TryGet(r, out var held) || held.Done).ToArray())
        {
            var voice = _scriptEyeCandyVoices[run];
            _scriptEyeCandyVoices.Remove(run);
            _audio?.Stop(voice);
            _positionalVoices.RemoveAll(v => v.Handle == voice);
        }
    }

    /// <summary>
    /// ARC-SCRIPT-1828 (WE12): a stop request (action 124) takes the owner's run of the script-list entry off at once, in the order the requests came (<see cref="AreaEyeCandyHost.StopScript"/>; the first matching run only, as the original's walk 0x4243E0 does).
    /// An object nothing played on (a play with no cell to draw on is dropped) has no run, so its stop ends nothing and says nothing: the main map's heartbeats stop the entry on every scenery around an NPC each time they run. The voice of a looping entry stops when its run is over (<see cref="StopEndedScriptEyeCandyVoices"/>), a stopped one included.
    /// </summary>
    private void StopScriptEyeCandy(AreaScriptEyeCandy request, SessionSpellCaster? caster)
    {
        if (caster is null) return;
        string owner;
        if (request.Object.Code == AreaScriptHost.PcCode) owner = SessionSpellCaster.Hero;
        else if (request.Object.Map == CurrentAreaMap) owner = caster.CastIdOf(request.Object.Code);
        else return;
        _scriptEyeCandyStopTaken++;
        // The shown session's eye-candy host only (a play builds it): with none built nothing was played that a stop could take off, and the heartbeat's stops over a map's scenery cost nothing there.
        var stopped = ShownEyeCandyHost?.StopScript(owner, request.Number) ?? 0;
        if (stopped == 0) return;
        _scriptEyeCandyStopped += stopped;
        _scriptEyeCandyStoppedForSelfTest.Add((owner, request.Number));
        GD.Print($"ARC-SCRIPT-1828 script-eye-candy-stop object={owner} number={request.Number} stopped={stopped}");
    }
}
