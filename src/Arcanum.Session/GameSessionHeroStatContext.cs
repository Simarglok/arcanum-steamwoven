using Arcanum.Simulation.Character;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-031 (FR-CHAR-020, the wiring of the ARC-MAGIC-028 inputs): what the world says about the place the hero stands on, which the stat getter 0x4B0490 reads under the critter: the aptitude
/// adjustment of the sector it stands in, and whether its tile is outdoor and whether its name carries the nature flag. All three are neutral (0, false, false) where the sector cannot be locked.
/// </summary>
public readonly record struct SessionHeroGround(int SectorAptitudeAdjustment = 0, bool OutdoorTile = false, bool NatureTile = false);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-MAGIC-031: the viewer's answer for the hero's ground (<see cref="SessionHeroGround"/>) at the hero's cell now; null (the default) is the neutral ground. Not saved: the viewer attaches it
    /// wherever it takes a session, as <see cref="DyingScript"/>.
    /// </summary>
    public Func<SessionHeroGround>? HeroGround { get; set; }

    /// <summary>
    /// ARC-MAGIC-031: the stat context a query of the hero's stats reads, from what the session and the world hold: the life state's poison level and crippled legs (<paramref name="poisonLevel"/>,
    /// <paramref name="crippledLegs"/>, which the caller reads from the life record it holds), the sector aptitude adjustment and the two tile facts of <see cref="HeroGround"/> (the adjustment is
    /// kept within the owner's bound ±100), the hero's background block, and the game hour from the session clock. The caller's stat query then moves aptitude by the sector and magick points by the
    /// Day, Night, Sky and Nature Mage backgrounds, as the original's getter does (ledger AQ1–AQ8).
    /// </summary>
    public CharacterStatContext HeroStatContext(int poisonLevel = 0, bool crippledLegs = false)
    {
        ThrowIfDisposed();
        var ground = HeroGround?.Invoke() ?? default;
        return new(Math.Clamp(ground.SectorAptitudeAdjustment, -100, 100), poisonLevel, crippledLegs, _hero?.State.Background ?? SessionBackgrounds.None, _clock.QueryClockSnapshot().Hour,
            ground.OutdoorTile, ground.NatureTile);
    }
}
