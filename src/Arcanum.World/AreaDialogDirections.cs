using Arcanum.Data.Maps;
using Arcanum.Scripting.Dialog;
using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Travel;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-1533 (ledger DIR3): the directions seam of one AREA speaker. <see cref="AreaName"/> is the name the module's area table (<c>mes\gamearea.mes</c>,
/// <see cref="WorldAreaTable"/>) holds for the area (0x4CAE90); <see cref="Bearing"/> is the direction (0x4B8D50) and Chebyshev distance (0x4B96F0) from the speaker's
/// location (where <paramref name="at"/> says it stands now, else its T0 cell) toward the area's location in the table (0x4CAED0). The original compares the two locations
/// as they are, whichever map the speaker stands on; so does this. A number outside the table, a speaker other than this one, or a location outside the
/// 32-bit range the original's coordinates hold has no answer.
/// </summary>
public sealed class AreaDialogDirections(WorldAreaTable areas, AreaSpeaker speaker, Func<AreaSpeaker, (long X, long Y)>? at) : IDialogDirectionsPort
{
    private readonly WorldAreaTable _areas = areas ?? throw new ArgumentNullException(nameof(areas));
    private readonly AreaSpeaker _speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));

    [OriginalFunction(0x4CAE90, OriginalFunctionScope.Partial)]
    public string? AreaName(int area) => area >= 0 && area < _areas.Areas.Count ? _areas.Areas[area].Name : null;

    [OriginalFunction(0x4CAED0, OriginalFunctionScope.Partial)]
    public DialogBearing? Bearing(DialogParticipantId npc, int area)
    {
        if (npc != AreaConversationState.Npc(_speaker.Index) || area < 0 || area >= _areas.Areas.Count) return null;
        var (x, y) = at?.Invoke(_speaker) ?? (_speaker.WorldX, _speaker.WorldY);
        var target = _areas.Areas[area].Location;
        if (!Fits(x) || !Fits(y) || !Fits(target.X) || !Fits(target.Y)) return null;
        var rotation = LineOfFireRules.Rotation(new CellCoord((int)x, (int)y), new CellCoord((int)target.X, (int)target.Y));
        return new DialogBearing(rotation, WorldTravelGeometry.Distance(new TravelLocation(x, y), new TravelLocation(target.X, target.Y)));
    }

    private static bool Fits(long value) => value is >= int.MinValue and <= int.MaxValue;
}
