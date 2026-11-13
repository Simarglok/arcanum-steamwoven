using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SCRIPT-1592 (ledger WE4, WE5): the script action 120 (play script eye candy) in the AREA view. The dialog host and the use host (use, tile, sector, dying and combat-point scripts, item use) collect each run's requests; every frame
// they are taken here and played on their objects as overlay runs of the script list (rules\ScriptEyeCandy.mes, AreaEyeCandyHost.PlayScript), which the eye-candy draw shows like a spell's. Not ready: an object that is not the hero
// or an NPC, created critter or summoned object of the shown map (scenery, items, portals have no cell the draw knows), the sound, the tinting, the turn of the art to the object's facing, the static (non-animating) entries; the wield scripts of
// the item host (ARC-MAGIC-1745) and the NPCs' own use scripts (the AI host) play none yet.
public partial class TerrainViewerApp
{
    // Smoke seams and the log: the requests taken, and those the host started.
    private int _scriptEyeCandyTaken, _scriptEyeCandyPlayed;
    private readonly List<(string Owner, int Number)> _scriptEyeCandyPlayedForSelfTest = [];

    /// <summary>The play requests the script hosts collected, played now on the shown map (the viewer asks for them every frame, before the eye candies are drawn); a request that cannot play (no cast host, another map, an entry without a run) is logged and dropped.</summary>
    private void SyncScriptEyeCandies()
    {
        var requests = new List<AreaScriptEyeCandy>();
        if (_conversationHost?.Scripts is { } talk) requests.AddRange(talk.TakeEyeCandies());
        if (_useScriptHost is { } use) requests.AddRange(use.Host.TakeEyeCandies());
        if (requests.Count == 0) return;
        if (_readyMode != TerrainViewerMode.Area || _readyArea is null) { GD.Print($"ARC-SCRIPT-1592 script-eye-candy dropped {requests.Count} request(s) (no area is shown)"); return; }
        var caster = SpellCasterNow();
        foreach (var request in requests)
        {
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
}
