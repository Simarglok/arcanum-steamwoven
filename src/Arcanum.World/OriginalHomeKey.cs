using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>What the Home key of the game view centres the view on (ARC-INPUT-1122, SA44, K6).</summary>
public enum HomeKeyTarget
{
    /// <summary>The key does nothing: the world has no player character, or the view is already centred on it and no round runs.</summary>
    Nothing,
    /// <summary>The player character's location.</summary>
    Pc,
    /// <summary>The participant whose turn it is (the round's list head).</summary>
    RoundParticipant,
}

/// <summary>
/// The Home key's handler of the original (0x54EB60, read in the exports of ARC-INPUT-1122): with a player character (0x40DA50), it asks the
/// view's offset function (0x4B8940) how far the view's centre is from the character's location. A distance of zero in both axes means the
/// view is already centred; then, when a round runs (0x4B6D70) and has a current participant (0x4B6D80), the view is centred on that
/// participant (0x4B8CE0, then the redraw 0x558110); with no round nothing happens. Any other distance centres the view on the character the
/// same way. Reached only through the game view's input handler (0x54DE50, key 199 on the release, in the modes of
/// <see cref="InterfaceModes.FollowerOrderKeys"/>), which returns before it when a round runs and its current participant is not the
/// player: so the participant the second case centres on is always the player character itself, and the case only repeats the first centring.
/// The centring (0x4B8CE0 through 0x4B8B30) refuses a move that would leave the map's rectangle (the view stays), so a character near an edge is never "already centred".
/// </summary>
public static class OriginalHomeKey
{
    [OriginalFunction(0x54EB60, OriginalFunctionScope.Partial)]
    public static HomeKeyTarget Target(bool pcExists, bool viewCentredOnPc, bool roundRuns, bool roundHasParticipant)
        => !pcExists ? HomeKeyTarget.Nothing
        : !viewCentredOnPc ? HomeKeyTarget.Pc
        : roundRuns && roundHasParticipant ? HomeKeyTarget.RoundParticipant
        : HomeKeyTarget.Nothing;
}
