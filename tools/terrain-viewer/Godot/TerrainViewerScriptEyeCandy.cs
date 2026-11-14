using Arcanum.Data.Magic;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SCRIPT-1592 (ledger WE4, WE5): the script action 120 (play script eye candy) in the AREA view (ARC-SCRIPT-1828, WE12: and the action 124, stop script eye candy, which takes the run of the entry off its object). The dialog host and the use host (use, tile, sector, dying and combat-point scripts, item use) collect each run's requests; every frame
// they are taken here and played on their objects as overlay runs of the script list (rules\ScriptEyeCandy.mes, AreaEyeCandyHost.PlayScript), which the eye-candy draw shows like a spell's. Not ready: an object that is not the hero
// or an NPC, created critter or summoned object of the shown map (scenery, items, portals have no cell the draw knows), the sound, the tinting, the turn of the art to the object's facing, the static (non-animating) entries; the NPCs' own use scripts (the AI host) and the item host's moves of the hero (ARC-MAGIC-1744 and the
// equipment windows) play none yet. ARC-MAGIC-1830: the wield scripts of an NPC's rewield (the TraitIdx magick and tech points, the AI gate's re-wield) and of its best-items wield (regeneration, a created critter's stocking) play theirs, as
// do the hero's TraitIdx rewield scripts, queued by the host that applied them (QueueScriptEyeCandies).
public partial class TerrainViewerApp
{
    // Smoke seams and the log: the requests taken, and those the host started.
    private int _scriptEyeCandyTaken, _scriptEyeCandyPlayed, _scriptEyeCandyStopTaken, _scriptEyeCandyStopped;
    private readonly List<(string Owner, int Number)> _scriptEyeCandyPlayedForSelfTest = [];
    private readonly List<(string Owner, int Number)> _scriptEyeCandyStoppedForSelfTest = [];
    // ARC-MAGIC-1830: the requests of the item event scripts a session move ran (an NPC's rewield or wield), queued by the code that applied the move and taken with the script hosts' above.
    private readonly List<AreaScriptEyeCandy> _queuedScriptEyeCandies = [];

    /// <summary>ARC-MAGIC-1830: queues the play requests among the eye candy requests of item event scripts a session move ran (<see cref="AreaScriptEyeCandy.Of(IEnumerable{ScrEyeCandy})"/>) for the next <see cref="SyncScriptEyeCandies"/>.</summary>
    private void QueueScriptEyeCandies(IEnumerable<ScrEyeCandy> candies) => _queuedScriptEyeCandies.AddRange(AreaScriptEyeCandy.Of(candies));

    /// <summary>The play requests the script hosts collected, played now on the shown map (the viewer asks for them every frame, before the eye candies are drawn); a request that cannot play (no cast host, another map, an entry without a run) is logged and dropped.</summary>
    private void SyncScriptEyeCandies()
    {
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
            // An object the draw has no cell for (scenery, an item, a portal, or an object an earlier action of the run destroyed) would hold a run nobody sees: not played (ledger VT9).
            if (_areaSession is not { } session || EyeCandyCell(session, owner) is null) { GD.Print($"ARC-SCRIPT-1592 script-eye-candy dropped number={request.Number} object={owner} (no cell to draw on)"); continue; }
            var played = EyeCandyHostNow()?.PlayScript(owner, request.Number) ?? false;
            if (played) { _scriptEyeCandyPlayed++; _scriptEyeCandyPlayedForSelfTest.Add((owner, request.Number)); }
            GD.Print($"ARC-SCRIPT-1592 script-eye-candy object={owner} number={request.Number} played={played}");
        }
    }

    /// <summary>
    /// ARC-SCRIPT-1828 (WE12): a stop request (action 124) takes the owner's run of the script-list entry off at once, in the order the requests came (<see cref="AreaEyeCandyHost.StopScript"/>; the first matching run only, as the original's walk 0x4243E0 does).
    /// An object that is not the hero or an NPC, created critter or summoned object of the shown map has no run here (its play was dropped), so its stop ends nothing and says nothing: the main map's heartbeats stop the entry on every scenery around an NPC each time they run.
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
