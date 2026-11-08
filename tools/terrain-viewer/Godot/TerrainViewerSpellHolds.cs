using Arcanum.Session;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-302 (MT13, GT145–GT153, D8): the holds a spell's ObjFlag component writes. Setting entangled or paralysed interrupts the critter's walk runs (goal 3 at the bottom, goal 61 on top) at
// level 6 through its own host; the hero's walk then reads the hold at each step start (TerrainViewerHeroRuns), an NPC's in the AI host.
public partial class TerrainViewerApp
{
    // The hold starts the smoke counts: the critter and how many runs it ended.
    private readonly List<(string Critter, int Ended)> _holdStartsForSelfTest = [];
    private readonly HashSet<SpellCastEvent> _holdTaken = new(ReferenceEqualityComparer.Instance);

    /// <summary>A cast host log entry of a hold's start: the critter's walk runs end (the hero's through the hero runs host, an NPC's through the AI host).</summary>
    private void TakeHoldEvent(SpellCastEvent entry)
    {
        if (entry.Kind != SpellCastEventKind.HoldStarted || entry.Object is not { } critter || _areaSession is null) return;
        if (_holdTaken.Count >= 256) _holdTaken.Clear();
        if (!_holdTaken.Add(entry)) return;
        var ended = critter == SessionSpellCaster.Hero ? HeroRunsHost()?.InterruptForHold() ?? 0 : _aiHost?.InterruptForHold(critter) ?? 0;
        _holdStartsForSelfTest.Add((critter, ended));
        if (_holdStartsForSelfTest.Count > 16) _holdStartsForSelfTest.RemoveAt(0);
        GD.Print($"ARC-DEV-302 hold-start run={entry.Run} critter={critter} flag=0x{entry.Value:X} ended={ended}");
    }

    /// <summary>ARC-DEV-302: the hero's holds now (the hero has no data flags, so they are what the spells wrote).</summary>
    private (bool Entangled, bool Stoned, bool Paralysed) HeroHolds()
    {
        if (_areaSession is not { } session) return default;
        var written = session.QueryObjectFlags(SessionSpellCaster.Hero);
        var spell = written.Spell(0);
        return ((spell & SessionSpellCaster.EntangledSpellFlag) != 0, (spell & SessionSpellCaster.StonedSpellFlag) != 0,
            (written.Critter(0) & SessionSpellCaster.ParalysedCritterFlag) != 0);
    }

    /// <summary>ARC-DEV-302 (GT148, D8): the hero's step start is refused: paralysed or stoned in both settings, entangled in Steamwoven.</summary>
    private bool HeroStepsHeld()
    {
        var (entangled, stoned, paralysed) = HeroHolds();
        return stoned || paralysed || entangled && _areaSession?.Rules.Navigation.EntangledPcKeepsMoving != true;
    }
}
