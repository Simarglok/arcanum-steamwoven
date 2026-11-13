using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Skills;

namespace Arcanum.Simulation.Items;

/// <summary>Why the bet gate (0x579840) refuses an item, in the original's order; each has the NPC line category the gate writes (<see cref="GamblingRules.LineCategory"/>).</summary>
public enum GambleRefusal
{
    None,
    /// <summary>The held item's source is the hero's own side (category 2): only the merchant's items can be staked.</summary>
    HeroSide,
    /// <summary>The merchant has no Gambling points (category 9).</summary>
    NoGambler,
    /// <summary>The item's price is above the hero's price limit (category 3).</summary>
    TooValuable,
    /// <summary>The hero has fewer coins than the price (category 4; the line carries the price).</summary>
    TooPoor,
    /// <summary>A worn item below Expert Gambling (category 5).</summary>
    NeedsExpert,
    /// <summary>A "won't sell" item below Master Gambling (category 6).</summary>
    NeedsMaster,
}

/// <summary>
/// What the bet gate reads (0x579840): whether the item is the merchant's, the merchant's Gambling points, the item's unit price as the hero
/// would pay it (<see cref="TradeRules.Price"/> for a buy, which is 0 when the buy check refuses), the hero's Gambling level and training, its
/// coins, and whether the item is worn (location 1000..1008) or has the "won't sell" flag (0x2).
/// </summary>
public sealed record GambleFacts(bool MerchantsItem, int MerchantPoints, int Price, int HeroLevel, SkillTraining HeroTraining, int HeroGold, bool Worn,
    bool WontSell);

/// <summary>The gate's answer: <see cref="Refusal"/> (none when the bet may go on) and <see cref="Warned"/>, whether the confirm line gets the
/// training warning (category 7) before the price line: the item is worn or has the "won't sell" flag and the hero's training allows the bet.</summary>
public readonly record struct GambleGate(GambleRefusal Refusal, bool Warned)
{
    public bool Allowed => Refusal == GambleRefusal.None;
}

/// <summary>
/// ARC-UI-1740 (FR-ITEM-019, ledger BT6, BW3, SK5, SR12): the gambling of the barter window, read in the executable. The gate 0x579840, the price
/// limit 0x4C6520 and the NPC line ranges 0x414020; the bet 0x579B60 is the session's (<c>GameSessionOwner.Gamble</c>: the skill check on the saved
/// roll, then the item moves free on a win and for its price on a loss). Pure functions over explicit values.
/// </summary>
public static class GamblingRules
{
    /// <summary>The hero's price limit by Gambling level 0..20 (the table at 0x5B7090), doubled from Apprentice (0x4C6520).</summary>
    private static readonly int[] LimitTable = [0, 25, 50, 75, 100, 150, 200, 250, 300, 400, 500, 600, 700, 900, 1100, 1300, 1500, 2000, 3000, 4000, 5000];

    /// <summary>The Gambling skill number (the basic skill 8) the gate and the check read.</summary>
    public const int GamblingSkill = 8;

    /// <summary>The highest price a hero of <paramref name="level"/> (0..20) and <paramref name="training"/> may stake: the table's value, doubled with training of one or more.</summary>
    [OriginalFunction(0x4C6520, OriginalFunctionScope.Full)]
    public static int PriceLimit(int level, SkillTraining training)
    {
        if (level is < 0 or > SkillRules.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
        if (!Enum.IsDefined(training)) throw new ArgumentOutOfRangeException(nameof(training));
        var limit = LimitTable[level];
        return training >= SkillTraining.Apprentice ? limit * 2 : limit;
    }

    /// <summary>
    /// The bet gate (0x579840), in the original's order: an item of the hero's side; a merchant with no Gambling points; a price above the hero's limit;
    /// a price above the hero's coins; a worn item below Expert Gambling; a "won't sell" item below Master Gambling. Both training tiers read the
    /// <b>hero's</b> training (ledger SK5). A price of 0 passes the limit and the coins tests, as the original's does (a merchant's worn or won't-sell
    /// item quotes 0 below Master haggle).
    /// </summary>
    [OriginalFunction(0x579840, OriginalFunctionScope.Full)]
    public static GambleGate Gate(GambleFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!facts.MerchantsItem) return new(GambleRefusal.HeroSide, false);
        if (facts.MerchantPoints <= 0) return new(GambleRefusal.NoGambler, false);
        if (facts.Price > PriceLimit(facts.HeroLevel, facts.HeroTraining)) return new(GambleRefusal.TooValuable, false);
        if (facts.HeroGold < facts.Price) return new(GambleRefusal.TooPoor, false);
        var warned = false;
        if (facts.Worn)
        {
            if (facts.HeroTraining < SkillTraining.Expert) return new(GambleRefusal.NeedsExpert, false);
            warned = true;
        }
        if (facts.WontSell)
        {
            if (facts.HeroTraining < SkillTraining.Master) return new(GambleRefusal.NeedsMaster, false);
            warned = true;
        }
        return new(GambleRefusal.None, warned);
    }

    /// <summary>The NPC line category (0x414020 case) the gate writes for a refusal: 2 hero side, 9 no gambler, 3 too valuable, 4 too poor, 5 expert, 6 master; −1 for none.</summary>
    public static int LineCategory(GambleRefusal refusal) => refusal switch
    {
        GambleRefusal.HeroSide => 2,
        GambleRefusal.NoGambler => 9,
        GambleRefusal.TooValuable => 3,
        GambleRefusal.TooPoor => 4,
        GambleRefusal.NeedsExpert => 5,
        GambleRefusal.NeedsMaster => 6,
        _ => -1,
    };

    /// <summary>The category of the line a won bet prints.</summary>
    public const int WonCategory = 0;
    /// <summary>The category of the line a lost bet prints (it carries the total paid).</summary>
    public const int LostCategory = 1;
    /// <summary>The category of the training warning of the confirm line.</summary>
    public const int WarningCategory = 7;
    /// <summary>The category of the confirm line (it carries the price).</summary>
    public const int ConfirmCategory = 8;

    /// <summary>The NPC line numbers of a category (0x414020): 100 lines from 3100 for 0..8, and 5600..5699 for 9.</summary>
    public static (int First, int Last) LineRange(int category) => category switch
    {
        >= 0 and <= 7 => (0xC1C + category * 0x64, 0xC7F + category * 0x64),
        8 => (0xF3C, 3999),
        9 => (0x15E0, 0x1643),
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary>
    /// A generated line with its price: the original formats the line with the price (0x589BD0, a C <c>%d</c>). The first <c>%d</c> takes the
    /// price; a line without one stays as it is.
    /// </summary>
    public static string WithPrice(string line, int price)
    {
        ArgumentNullException.ThrowIfNull(line);
        var at = line.IndexOf("%d", StringComparison.Ordinal);
        return at < 0 ? line : string.Concat(line.AsSpan(0, at), price.ToString(System.Globalization.CultureInfo.InvariantCulture), line.AsSpan(at + 2));
    }

    /// <summary>The reaction a won bet costs the merchant (0x4C11D0 with the negative amount): the item's worth × count taken from it, which
    /// <see cref="TradeRules.GiftReaction"/> turns into at least −2 and at most −30 steps.</summary>
    public static int WonReaction(int worth, int count, ReactionCategory category) =>
        TradeRules.GiftReaction(-unchecked(worth * count), category);
}
