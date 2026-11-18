namespace Arcanum.Presentation.Steamwoven;

/// <summary>The four baked sets of the Steamwoven kit, as a fraction of the masters (SWI-LAY-4).</summary>
public enum SteamwovenStep
{
    /// <summary>×1/3: a 6 px master length is 2 px.</summary>
    Third,
    /// <summary>×1/2: 3 px.</summary>
    Half,
    /// <summary>×2/3: 4 px.</summary>
    TwoThirds,
    /// <summary>×1: 6 px.</summary>
    One,
}

/// <summary>
/// ARC-UI-2450 (SWI-LAY-1, -4, -7, -11; TRD "Terms"): the master grid of the Steamwoven interface. A master is a part as authored for the 3840 × 2160
/// reference screen; one UI unit of the original 800 × 600 layout is 3.6 master pixels, and every master length and offset is a multiple of 6 so
/// that it is a whole number of pixels in every baked step. Pure numbers, no Godot.
/// </summary>
public static class SteamwovenGrid
{
    public const int ReferenceWidth = 3840, ReferenceHeight = 2160;
    /// <summary>The master height of a 16:10 frame at ×1 (3840 × 2400): the tallest frame laid out natively.</summary>
    public const int TallReferenceHeight = 2400;
    /// <summary>Master pixels per original UI unit is 18/5 = 3.6.</summary>
    public const int UnitNumerator = 18, UnitDenominator = 5;
    public const int GridPx = 6;
    /// <summary>The centre block that keeps the original layout: 800 units.</summary>
    public const int CentreWidth = 2880;
    /// <summary>The original window zone 800 × 400 units (SWI-BDR-2).</summary>
    public const int WindowZoneHeight = 1440;
    /// <summary>The full-screen menu master, 3840 × 2400, and its 800 × 600 unit zone, 2880 × 2160 (SWI-BDR-6).</summary>
    public const int FullScreenHeight = 2400, FullScreenZoneHeight = 2160;
    /// <summary>The split of a two-part window (358 units, snapped to the grid) from the centre block's left edge (SWI-BDR-5).</summary>
    public const int SplitFromCentreLeft = 1290;
    /// <summary>The panel heights the owner chose (decision 2026-11-17, #2453, SWI-LAY-10): the thinner pair, 120 and 456 master px.</summary>
    public const int DefaultTopPanel = 120, DefaultBottomPanel = 456;
    /// <summary>The heights of the original's two strips at 3.6 master px per unit, snapped to the grid (41 and 159 units: 147.6 and 572.4): the panels the prototype (#2452) offered first, and the reference the strips' scale is measured against.</summary>
    public const int StripTopPanel = 150, StripBottomPanel = 570;
    /// <summary>A black bar under this many pixels per side is dropped (SWI-LAY-3).</summary>
    public const int MinBarPx = 4;
    /// <summary>The largest window side the model takes; a bound for the exact arithmetic.</summary>
    public const int MaxWindowSide = 32768;

    /// <summary>The smallest scale `f` the Steamwoven interface supports (SWI-LAY-4); below it the Original look is used.</summary>
    public static Ratio MinSupportedScale => new(1, 4);

    /// <summary>The fraction of the masters a baked step holds.</summary>
    public static Ratio StepScale(SteamwovenStep step) => step switch
    {
        SteamwovenStep.Third => new Ratio(1, 3),
        SteamwovenStep.Half => new Ratio(1, 2),
        SteamwovenStep.TwoThirds => new Ratio(2, 3),
        SteamwovenStep.One => Ratio.One,
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    /// <summary>The smallest step whose scale is at least <paramref name="scale"/>; above ×1 the ×1 set (scaled up, a recorded limit).</summary>
    public static SteamwovenStep StepFor(Ratio scale)
    {
        foreach (var step in new[] { SteamwovenStep.Third, SteamwovenStep.Half, SteamwovenStep.TwoThirds })
            if (StepScale(step) >= scale) return step;
        return SteamwovenStep.One;
    }

    /// <summary>A master length in a baked step: exact, because a master length is a multiple of 6.</summary>
    public static int BakedLength(int masterLength, SteamwovenStep step)
    {
        if (masterLength % GridPx != 0) throw new ArgumentOutOfRangeException(nameof(masterLength), "a master length is a multiple of 6 px");
        var scale = StepScale(step);
        return checked((int)(masterLength * scale.Numerator / scale.Denominator));
    }

    /// <summary>
    /// A control's offset or length in original UI units as master pixels, snapped to the 6 px grid (SWI-LAY-11): units × 3.6, halves up; the shift is
    /// at most 3 px, under one unit. Negative units mirror (a half goes towards the larger number).
    /// </summary>
    public static int UnitsToMaster(int units) => new Ratio(units * 3L, 5).RoundHalfUp() * GridPx;
}
