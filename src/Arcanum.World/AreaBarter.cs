using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Reaction;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation;

namespace Arcanum.World;

public enum AreaBarterDataStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, SourcesFailed }

/// <summary>
/// What the barter window reads from the game data (ARC-UI-050): the inventory sources (<c>rules\InvenSource.mes</c>, and
/// <c>rules\InvenSourceBuy.mes</c> when present) for the merchants' buy lists, and the item prototypes' stored fields for items without
/// an object row (kit, made and given items) and for the gold pile (prototype 9076, basic 9056).
/// </summary>
public sealed class AreaBarterData
{
    public const int GoldPrototype = 9076;
    private const int PrototypeOffset = 20, FirstItemBasic = 6029, EndItemBasic = 15213, FirstBasic = 1000, EndBasic = 28472;
    private readonly Dictionary<int, CombatFields?> _prototypes;

    private AreaBarterData(MesFile sources, MesFile? buy, Dictionary<int, CombatFields?> prototypes, HashSet<int> existing)
    {
        Sources = sources; Buy = buy; _prototypes = prototypes; Existing = existing;
    }

    public MesFile Sources { get; }
    public MesFile? Buy { get; }
    /// <summary>The basic prototype numbers whose prototype exists (the buy-line check).</summary>
    public IReadOnlySet<int> Existing { get; }

    /// <summary>The stored fields of prototype file <paramref name="prototype"/> (basic + 20), when it was read.</summary>
    public CombatFields? Prototype(int prototype) => _prototypes.GetValueOrDefault(prototype);

    /// <summary>A set's buy list (<see cref="InventorySourceBuyRules"/>); 0 or a negative set has none.</summary>
    public InventorySourceBuyList BuyList(int set) => InventorySourceBuyRules.Read(Sources, Buy, set, Existing.Contains);

    /// <summary>The kind of a gold pile (prototype 9076, its stored name and description when read).</summary>
    public SessionItemKind GoldKind() => Prototype(GoldPrototype) is { } gold
        ? new(SessionItemType.Gold, GoldPrototype, gold.Object.Name, gold.Object.Description)
        : new(SessionItemType.Gold, GoldPrototype, 0, 0);

    /// <summary>Synthetic data for tests: the sources, the buy file, and prototype fields by file number.</summary>
    public static AreaBarterData Create(MesFile sources, MesFile? buy, IReadOnlyDictionary<int, CombatFields> prototypes) =>
        new(sources, buy, prototypes.ToDictionary(pair => pair.Key, pair => (CombatFields?)pair.Value),
            prototypes.Keys.Select(number => number - PrototypeOffset).ToHashSet());

    /// <summary>Loads the data through the game folder's content stack: the two inventory-source files, which basic prototype numbers
    /// have a prototype (the loot and buy lines' check), and the stored fields of every item prototype (the basic ranges of weapons to
    /// generic items, 6029..15212), so kit, made and given items find theirs.</summary>
    public static (AreaBarterDataStatus Status, AreaBarterData? Data, string Detail) Load(string modulePath)
    {
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return (found.Status == AreaContentRootStatus.MountFailed ? AreaBarterDataStatus.MountFailed : AreaBarterDataStatus.ModuleOutsideGameRoot, null, found.Detail ?? "");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return (AreaBarterDataStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var sources = Mes(stack, "rules/InvenSource.mes");
        if (sources is null) return (AreaBarterDataStatus.SourcesFailed, null, "rules/InvenSource.mes");
        var buy = Mes(stack, "rules/InvenSourceBuy.mes");
        var reader = ContentPrototypes.Open(stack);
        var fields = new Dictionary<int, CombatFields?>();
        var existing = new HashSet<int>();
        // Every basic number of the original's ranges whose prototype exists: a loot or buy line may name any of them (0x4685A0).
        for (var basic = FirstBasic; basic < EndBasic; basic++)
            if (InventorySourceBuyRules.InBasicRange(basic) && CombatFieldReader.ReadPrototype(reader.Read, basic + PrototypeOffset).Fields is { } read)
            {
                existing.Add(basic);
                if (basic is >= FirstItemBasic and < EndItemBasic) fields[basic + PrototypeOffset] = read;
            }
        return (AreaBarterDataStatus.Ready, new(sources, buy, fields, existing),
            $"sets {sources.Count}, buy lines {buy?.Count ?? 0}, item prototypes {fields.Count}, known basic numbers {existing.Count}");
    }

    private static MesFile? Mes(ContentStack stack, string path)
    {
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } source) return null;
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read) return null;
        var parsed = MesFile.Parse(read.Bytes!);
        return parsed.Status == MesParseStatus.Parsed ? parsed.File : null;
    }
}

/// <summary>
/// The merchant of an open barter window: its entity and name, its retail price multiplier (NPC field 0x125), whether it is a fence
/// (NPC flag 0x4000) and has a buy-object script (point 17, not run here: below Expert haggle it refuses every sale), its buy list,
/// and whether it follows the hero (a follower trades for nothing, ledger BT6). ARC-WORLD-090: its substitute container when the window
/// uses one (<see cref="Container"/>, with the container's own buy list, which the dealer test tries second, BT8).
/// </summary>
public sealed record AreaBarterMerchant(string Entity, string Name, int Multiplier, bool Fence, bool BuyScript, InventorySourceBuyList BuyList, bool Follower)
{
    public const int BuyObjectPoint = 17;
    /// <summary>The substitute container's reach in tiles (0x45F650, ledger BT4).</summary>
    public const int SubstituteRange = 20;

    /// <summary>ARC-WORLD-2119: whether any critter leads the merchant (its direct leader 0x45DE00 is not null, the hero or an NPC): the substitute test of 0x45F650. <see cref="Follower"/> is the
    /// narrower hero-led case, the window's follower barter (0x5727B0 compares the direct leader with the hero).</summary>
    public bool Led { get; init; }

    /// <summary>ARC-WORLD-2119: whether the merchant's chain of leaders reaches the PC (0x45DDA0), the test of the open's identify step (0x572640): such a merchant's items are not identified, a follower's
    /// and one led through another follower alike.</summary>
    public bool PcLeads { get; init; }

    /// <summary>ARC-WORLD-090: the substitute container the window shows and fills, or null.</summary>
    public string? Container { get; init; }
    /// <summary>ARC-WORLD-090: the container's buy list (its inventory source, field 0x3D), or null.</summary>
    public InventorySourceBuyList? ContainerBuyList { get; init; }

    /// <summary>The dealer test (0x464200): the merchant's own buy list, else its substitute container's.</summary>
    public bool Deals(int basic) => BuyList.Deals(basic) || ContainerBuyList?.Deals(basic) == true;

    /// <summary>ARC-WORLD-090: the merchant with its substitute container (0x45F650): an NPC with no leader (none at T0, not following the
    /// hero) whose container lies within <see cref="SubstituteRange"/> tiles of <paramref name="cell"/>, the merchant's (Chebyshev, 0x4B96F0).</summary>
    [OriginalFunction(0x45F650, OriginalFunctionScope.Full)]
    public AreaBarterMerchant WithSubstitute(AreaSubstituteContainer? substitute, CellCoord cell, AreaBarterData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (substitute is null || Follower || Led || Arcanum.Simulation.Ai.AiExecutorRules.Distance(cell, substitute.Cell) > SubstituteRange) return this;
        return this with { Container = substitute.Entity, ContainerBuyList = data.BuyList(substitute.InventorySource) };
    }

    /// <summary>The merchant facts of an NPC's stored fields with its leaders read from the session (ARC-WORLD-2119): follower of the hero (direct leader), led by anyone, chain to the PC; null for anything that is not an NPC.</summary>
    public static AreaBarterMerchant? From(string entity, string name, CombatFields fields, AreaBarterData data, GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return From(entity, name, fields, data, session.QueryCompanion(entity).LedByPc) is { } merchant
            ? merchant with { Led = session.QueryLeader(entity) is not null, PcLeads = session.QueryPcLeader(entity) is not null } : null;
    }

    /// <summary>The merchant facts of an NPC's stored fields; null for anything that is not an NPC.</summary>
    public static AreaBarterMerchant? From(string entity, string name, CombatFields fields, AreaBarterData data, bool follower)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(data);
        if (fields.Npc is not { } npc || fields.Critter is not { } critter) return null;
        return new(entity, name, npc.RetailPriceMultiplier, (npc.Flags & TradeRules.FenceFlag) != 0, fields.Object.ScriptPoints.Contains(BuyObjectPoint),
            data.BuyList(critter.InventorySource), follower);
    }
}

/// <summary>One line of either side: the item, its text, the units it trades (a stack's all), its unit quote and worth.</summary>
public sealed record AreaBarterLine(string Entity, string Text, int Count, TradeQuote Quote, int Worth, SessionItemKind Kind)
{
    public bool Stack => Kind.Type == SessionItemType.Ammo;

    /// <summary>ARC-UI-1740: the item's flags (field 0x57; the bet gate reads the "won't sell" bit, 0x2).</summary>
    public uint ItemFlags { get; init; }
}

/// <summary>The window's state: both sides' gold and lines, the hero's haggle and the merchant's reaction.</summary>
public sealed record AreaBarterView(AreaBarterMerchant Merchant, int HeroGold, int MerchantGold, int Haggle, SkillTraining Training, int Reaction,
    IReadOnlyList<AreaBarterLine> Hero, IReadOnlyList<AreaBarterLine> Wares)
{
    /// <summary>ARC-UI-1926 (0x575200 on a location of 1000 and up): the merchant's own worn items, the ones its doll view shows; they are not in <see cref="Wares"/> (the diagnostic list and the
    /// bet leave them out). Their buy quote is 0 below Master haggle (0x462230).</summary>
    public IReadOnlyList<AreaBarterLine> WornWares { get; init; } = [];
}

/// <summary>ARC-UI-1731: an item's facts for a quote: its stored item fields (null when none were read), whether it counts as identified, its stored
/// worth, the worth a quote starts from, its condition (the default when its fields give none) and its stored condition (null when the fields give none).</summary>
internal sealed record AreaItemFacts(CombatItemFields? Item, bool Identified, int Stored, int Worth, ItemCondition Condition, ItemCondition? Baseline);

/// <summary>The outcome of one buy or sell: the session's result, the units, the price paid in all and the reaction change asked for.</summary>
public sealed record AreaBarterTrade(InventoryMutationResult Result, int Count, int Total, int ReactionChange, string Text)
{
    public bool Moved => Result.Disposition == GameSessionDisposition.Changed;
}

/// <summary>ARC-UI-1740: what the bet gate (0x579840) says of an item held over the bet box: the gate, the unit price it read and the line the quote area
/// shows (the refusal, or the confirm line with the price).</summary>
public sealed record AreaBarterGambleCheck(GambleGate Gate, int Price, string Text)
{
    public bool Allowed => Gate.Allowed;
}

/// <summary>ARC-UI-1740: one bet's outcome: the gate (a refused one ran nothing and says nothing, as the release's silent gate), the session's result, whether
/// the hero won, the coins paid, the reaction change asked of the merchant, and the line the quote area shows.</summary>
public sealed record AreaBarterGambleResult(GambleGate Gate, SessionGambleResult? Bet, bool Won, int Paid, int ReactionChange, string Text)
{
    public bool Settled => Bet?.Settled == true;
}

/// <summary>
/// ARC-UI-050: the barter window's model over the session (contract docs/world/barter-runtime.md, viewer docs/rendering/terrain-viewer.md):
/// the hero's items with their sale quotes, the merchant's items that are not worn and not coins with their buy quotes
/// (<see cref="TradeRules.Price"/>), and the trades through <see cref="GameSessionOwner.BarterBuy"/> and
/// <see cref="GameSessionOwner.BarterSell"/>, then the reaction change of a generous trade (0x4C11D0 through the session). A
/// quote of 0 cancels the trade outside a follower's window, a hero with less gold than a unit price is refused before the move
/// ("not enough money"), and a sale pays at most the merchant's coins. ARC-WORLD-090: with a substitute container the window shows and
/// fills the container (<see cref="Open"/> moves its coins onto the merchant and identifies both lists, BT4); an item counts as identified
/// by its stored flag or the session's. Not modelled: the restock timer, the buy-object script run and the generated quote lines.
/// </summary>
public sealed class AreaBarter
{
    private readonly GameSessionOwner _session;
    private readonly AreaInventoryHost _inventory;
    private readonly AreaBarterData _data;
    private readonly Func<int> _reaction;
    private readonly Func<int, bool> _adjustReaction;
    private readonly Func<string, SessionItemKind, string> _name;
    private readonly Func<SessionSkill?>? _merchantGambling;
    private readonly Func<int, string?>? _npcLine;

    /// <param name="reaction">The merchant's standard effective reaction to the hero.</param>
    /// <param name="adjustReaction">Applies a reaction change to the merchant through the session; false when the session refused it.</param>
    /// <param name="name">An item's shown name by entity and kind (the window's own text).</param>
    /// <param name="merchantGambling">ARC-UI-1740: the merchant's Gambling skill (its points open the bet, its level opposes the hero's check); null
    /// when the host cannot read the merchant's sheet (then the merchant has no Gambling points and no bet is accepted).</param>
    /// <param name="npcLine">ARC-UI-1740: a generated NPC line of the merchant for a line category (<see cref="GamblingRules.LineRange"/>), or null
    /// when none can be read (the window then prints its own text).</param>
    public AreaBarter(GameSessionOwner session, AreaInventoryHost inventory, AreaBarterData data, AreaBarterMerchant merchant, Func<int> reaction,
        Func<int, bool> adjustReaction, Func<string, SessionItemKind, string> name, Func<SessionSkill?>? merchantGambling = null, Func<int, string?>? npcLine = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        Merchant = merchant ?? throw new ArgumentNullException(nameof(merchant));
        _reaction = reaction ?? throw new ArgumentNullException(nameof(reaction));
        _adjustReaction = adjustReaction ?? throw new ArgumentNullException(nameof(adjustReaction));
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _merchantGambling = merchantGambling;
        _npcLine = npcLine;
    }

    public AreaBarterMerchant Merchant { get; }

    /// <summary>ARC-WORLD-090: the same window for the merchant's own list (its substitute container gone).</summary>
    public AreaBarter WithoutContainer() => new(_session, _inventory, _data, Merchant with { Container = null, ContainerBuyList = null }, _reaction,
        _adjustReaction, _name, _merchantGambling, _npcLine);

    /// <summary>Whether the window opens: a hero that is alive (the original's open refuses only a dead or missing PC, 0x572370).</summary>
    public static bool HeroCanTrade(GameSessionOwner session) =>
        session.QueryHero() is not null && session.QueryHeroLife() is { Status.IsDead: false };

    /// <summary>ARC-WORLD-090: the open's session part (0x572640): the container's coins onto the merchant and, outside a follower's
    /// window, every item of the merchant and of the container identified.</summary>
    public SessionBarterOpenResult Open()
    {
        var opened = _session.OpenBarter(_session.Version, Merchant.Entity, Merchant.Container, identify: !Merchant.Follower && !Merchant.PcLeads, GoldKind(),
            AreaInventoryHost.DiagnosticInsertCheck);
        // ARC-UI-1740: the skill module's gambling roll is thrown before anyone can bet and saved with the session (SR12).
        if (opened.Disposition != GameSessionDisposition.Rejected) _session.EnsureGamblingRoll(_session.Version);
        return opened with { Version = _session.Version };
    }

    /// <summary>The window's lines now.</summary>
    public AreaBarterView View()
    {
        var (haggle, training) = _session.QueryHeroHaggle() ?? (0, SkillTraining.None);
        var reaction = _reaction();
        var parties = Parties(haggle, training, reaction);
        var worn = _session.QueryEquipment().Worn.Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        var hero = _session.QueryHeroKit().Select(item => (item.Entity, item.Kind))
            .Concat(_session.QueryPcInventory().Select(item => (item.Entity, item.Kind)))
            .Concat(_session.QueryMadeItems().Select(item => (item.Entity, item.Kind)))
            .Where(item => !_session.IsItemDestroyed(item.Entity))
            .Select(item => Line(item.Entity, item.Kind, TradeDirection.PcSells, parties, worn.Contains(item.Entity), Units(item.Entity) ?? 1))
            .ToArray();
        var shown = Merchant.Container ?? Merchant.Entity;
        var holder = _session.QueryMapInventory(_session.QueryActor().Map)?.Holders.FirstOrDefault(h => h.Entity == shown);
        var wares = (holder?.Items ?? [])
            .Where(item => !item.Facts.Worn && item.Facts.Kind.Type != SessionItemType.Gold)
            .Select(item => Line(item.Entity, item.Facts.Kind, TradeDirection.PcBuys, parties, false,
                item.Facts.AmmoType is not null ? item.Facts.Quantity ?? 1 : 1))
            .ToArray();
        // ARC-UI-1926: the doll view's items are the real merchant's, even when a substitute container is the shown grid (0x575CB0 returns the merchant as owner).
        var merchantHolder = Merchant.Container is null ? holder : _session.QueryMapInventory(_session.QueryActor().Map)?.Holders.FirstOrDefault(h => h.Entity == Merchant.Entity);
        var wornWares = (merchantHolder?.Items ?? [])
            .Where(item => item.Facts.Worn && item.Facts.Kind.Type != SessionItemType.Gold)
            .Select(item => Line(item.Entity, item.Facts.Kind, TradeDirection.PcBuys, parties, true, 1))
            .ToArray();
        return new(Merchant, _session.QueryGold(), _session.QueryNpcGold(Merchant.Entity) ?? 0, haggle, training, reaction, hero, wares) { WornWares = wornWares };
    }

    /// <summary>The hero buys a merchant's item (all units of a stack) at its quote.</summary>
    [OriginalFunction(0x575200, OriginalFunctionScope.Partial)]
    public AreaBarterTrade Buy(string entity)
    {
        var view = View();
        // ARC-UI-1926: a worn item of the merchant (its doll view) is taken from the merchant itself, never from the substitute container.
        var worn = view.WornWares.FirstOrDefault(item => item.Entity == entity);
        var line = worn ?? view.Wares.FirstOrDefault(item => item.Entity == entity);
        if (line is null) return Refused("not one of the merchant's wares");
        if (line.Quote.Price == 0 && !Merchant.Follower) return Refused($"won't sell ({line.Quote.Refusal})");
        if (_session.QueryGold() < line.Quote.Price) return Refused("not enough money");
        var result = _session.BarterBuy(_session.Version, Merchant.Entity, entity, line.Count, Merchant.Follower ? 0 : line.Quote.Price, line.Kind,
            GoldKind(), _inventory.InsertCheck, AreaInventoryHost.DiagnosticInsertCheck, _inventory.ItemScripts(_session), worn is null ? Merchant.Container : null,
            wornItem: worn is not null, npcWearer: worn is null ? null : _inventory.NpcWearer?.Invoke(_session.QueryActor().Map, Merchant.Entity));
        return Settle(TradeDirection.PcBuys, line, result);
    }

    /// <summary>The hero sells one of its items (all units of a stack) at its quote, at most the merchant's coins.</summary>
    public AreaBarterTrade Sell(string entity)
    {
        var line = View().Hero.FirstOrDefault(item => item.Entity == entity);
        if (line is null) return Refused("not one of the hero's items");
        if (line.Quote.Price == 0 && !Merchant.Follower) return Refused($"won't buy ({line.Quote.Refusal})");
        var price = Merchant.Follower ? 0 : TradeRules.SalePayment(line.Quote.Price, _session.QueryNpcGold(Merchant.Entity) ?? 0);
        var result = _session.BarterSell(_session.Version, Merchant.Entity, entity, line.Count, price, line.Kind, AreaInventoryHost.DiagnosticInsertCheck,
            // The sale's identify step is not read separately from the window's follower flag here: it keeps the follower test (the open's chain read, 0x572640, is the only one read).
            Merchant.Container, identify: !Merchant.Follower, scripts: _inventory.ItemScripts(_session));
        return Settle(TradeDirection.PcSells, line with { Quote = line.Quote with { Price = price } }, result);
    }

    /// <summary>
    /// ARC-UI-2138 (the release on a slot of the merchant's doll, 0x574BC1..0x574BF7, ledger IW35): the wear test 0x464D20 for the merchant as the wearer over what it wears now, <c>WrongType</c> for a slot the item cannot take (its own wear slot, a ring in
    /// either ring slot), <c>null</c> for a merchant the host cannot describe (NOT READY, the item goes back with a text); a refusal of the test, or a slot the merchant already fills, puts the item back silently (0x4673F0 prints only for the player, and an NPC's slot is never swapped).
    /// </summary>
    public EquipmentRefusal? MerchantWearRefusal(SessionWearableItem wearable, EquipmentSlot slot)
    {
        ArgumentNullException.ThrowIfNull(wearable);
        if (!AreaInventoryHost.WearSlots(wearable).Contains(slot)) return EquipmentRefusal.WrongType;
        if (_inventory.NpcWearerFacts?.Invoke(_session.QueryActor().Map, Merchant.Entity) is not { } wearer) return null;
        var worn = new Dictionary<EquipmentSlot, EquipmentItem>();
        var holder = _session.QueryMapInventory(_session.QueryActor().Map)?.Holders.FirstOrDefault(h => h.Entity == Merchant.Entity);
        foreach (var listed in (holder?.Items ?? []).Where(item => item.Facts.Worn && item.Facts.Kind.Type != SessionItemType.Gold))
        {
            if (_inventory.HolderItemWearable(_session, listed.Entity, listed.Facts.Kind) is not { } facts) return null;
            worn[(EquipmentSlot)listed.Facts.InventoryLocation] = facts.Item;
        }
        return EquipmentRules.CheckWear(wearable.Item, slot, wearer, worn);
    }

    /// <summary>
    /// ARC-UI-2138 (0x575360 with the location 1000 + slot, executor 0x578EA0 → 0x461CA0 → 0x466640; ledger IW35, BW5): the hero sells one of its items, worn or in its pack, onto a slot of the merchant's doll and the merchant wears it: the sale rules of
    /// <see cref="Sell"/> (price 0 puts it back outside a follower's window, the price is capped at the merchant's coins; the quote and its refusal phrase are the hover's), then <see cref="GameSessionOwner.BarterSellWorn"/>. The wear test comes first
    /// (<see cref="MerchantWearRefusal"/>): a refusal is silent (empty text, nothing changes). The item goes to the merchant itself even when a substitute container is the shown object.
    /// </summary>
    public AreaBarterTrade SellOnSlot(string entity, EquipmentSlot slot, SessionWearableItem wearable)
    {
        var line = View().Hero.FirstOrDefault(item => item.Entity == entity);
        if (line is null) return Refused("not one of the hero's items");
        var test = MerchantWearRefusal(wearable, slot);
        // NOT READY: a merchant the host cannot describe (no wearer, a worn item without wear facts) takes nothing, and says so (the original's wear test refusal alone is silent).
        if (test is null) return Refused("the merchant's wear values are not known here");
        if (test != EquipmentRefusal.None) return Refused("");
        if (line.Quote.Price == 0 && !Merchant.Follower) return Refused($"won't buy ({line.Quote.Refusal})");
        var price = Merchant.Follower ? 0 : TradeRules.SalePayment(line.Quote.Price, _session.QueryNpcGold(Merchant.Entity) ?? 0);
        var result = _session.BarterSellWorn(_session.Version, Merchant.Entity, entity, slot, wearable, price, line.Kind, AreaInventoryHost.DiagnosticInsertCheck,
            identify: !Merchant.Follower, scripts: _inventory.ItemScripts(_session));
        // A slot the merchant's list shows filled goes back silently (the original never swaps an NPC's slot); the other refusals print as a grid sale's do.
        if (result.Disposition != GameSessionDisposition.Changed && result.Item is null && result.Failure == GameSessionFailure.InvalidEquipment) return Refused("");
        return Settle(TradeDirection.PcSells, line with { Quote = line.Quote with { Price = price } }, result);
    }

    // ARC-UI-1740: the fallback texts of the bet's lines when no generated NPC line can be read (the window's own text, not the original's).
    private static readonly Dictionary<int, string> BetFallbacks = new()
    {
        [2] = "Only the merchant's wares can be staked.",
        [9] = "The merchant does not gamble.",
        [3] = "That is worth more than your skill can stake.",
        [4] = "You cannot cover %d coins if you lose.",
        [5] = "Staking a worn item needs Expert gambling.",
        [6] = "Staking an item that is not for sale needs Master gambling.",
        [7] = "A risky stake.",
        [8] = "Stake it? A loss costs %d coins.",
        [0] = "You win it.",
        [1] = "You lose and pay %d coins.",
    };

    /// <summary>ARC-UI-1806: whether <paramref name="text"/> is one of the window's own fallback lines (no generated NPC line was read), with or without a price filled in.</summary>
    public static bool IsBetFallback(string text) => BetFallbacks.Values.Any(fallback => text == fallback
        || fallback.Contains("%d", StringComparison.Ordinal) && System.Text.RegularExpressions.Regex.IsMatch(text, "^" + System.Text.RegularExpressions.Regex.Escape(fallback).Replace("%d", @"-?\d+") + "$"));

    private string BetLine(int category, int? price = null)
    {
        var text = _npcLine?.Invoke(category);
        if (string.IsNullOrEmpty(text)) text = BetFallbacks[category];
        return price is { } value ? GamblingRules.WithPrice(text, value) : text;
    }

    private (GambleGate Gate, AreaBarterLine? Line, int Price) BetGate(AreaBarterView view, string entity)
    {
        // ARC-UI-2139 (0x579840): an item lifted from the merchant's doll is still the merchant's (the gate reads the source holder, not the location) and its location is
        // still 1000..1008, so the gate's Worn fact is true and its price is the buy quote of a worn item (0 below Master haggle, 0x462230).
        var worn = view.WornWares.FirstOrDefault(item => item.Entity == entity);
        var line = worn ?? view.Wares.FirstOrDefault(item => item.Entity == entity);
        // ARC-UI-1808 (0x579840, 0x579B60 → 0x461F80): the 0-price rule of a follower's window lives in the hover quote (0x578330) alone; the gate and a lost bet price the item
        // with the ordinary buy quote, as for any merchant, so a follower's item is staked at its real price.
        if (line is not null && Merchant.Follower)
            line = Line(entity, line.Kind, TradeDirection.PcBuys, Parties(view.Haggle, view.Training, view.Reaction), worn is not null, line.Count, ignoreFollower: true);
        var hero = _session.QueryHeroSkills() is { } skills ? skills[Skill.Gambling] : default;
        var price = line?.Quote.Price ?? 0;
        var facts = new GambleFacts(MerchantsItem: line is not null, _merchantGambling?.Invoke()?.Points ?? 0, price, hero.Level, hero.Training, view.HeroGold,
            Worn: worn is not null, WontSell: line is not null && (line.ItemFlags & TradeRules.WontSellFlag) != 0);
        return (GamblingRules.Gate(facts), line, price);
    }

    /// <summary>
    /// ARC-UI-1740 (0x579840 with its text on, called as an item first enters the bet box, BW3): the gate on <paramref name="entity"/>, an item held over the
    /// box. A refusal's text is the merchant's line of the refusal's category (the price in the poor line); an allowed bet's text is the training warning line
    /// when the item needs one, then the confirm line with the price. The generated lines are drawn through <c>npcLine</c> (one draw each), so the caller asks
    /// once per entry into the box, as the original's box art change does.
    /// </summary>
    public AreaBarterGambleCheck CheckGamble(string entity)
    {
        var view = View();
        var (gate, _, price) = BetGate(view, entity);
        if (!gate.Allowed) return new(gate, price, BetLine(GamblingRules.LineCategory(gate.Refusal), gate.Refusal == GambleRefusal.TooPoor ? price : null));
        var warning = gate.Warned ? BetLine(GamblingRules.WarningCategory) + " " : "";
        return new(gate, price, warning + BetLine(GamblingRules.ConfirmCategory, price));
    }

    /// <summary>
    /// ARC-UI-1740 (0x579B60, ledger BT6): the hero bets <paramref name="entity"/>, a merchant's item. The gate runs again with its text off, so a refused bet
    /// is silent (the text the hover showed stays). Then the session's bet (<see cref="GameSessionOwner.Gamble"/>): a win moves the item for nothing and costs the
    /// merchant reaction (<see cref="GamblingRules.WonReaction"/>), a loss only charges its price × count (ARC-UI-2215: the move block of 0x578EA0 is skipped, the item stays in the merchant's list; the surplus
    /// gift of a generous price still lifts the reaction of a merchant in love, as a buy's does). A stack goes whole (the window's rule). The roll for the next bet is thrown in the same step.
    /// </summary>
    public AreaBarterGambleResult Gamble(string entity)
    {
        var view = View();
        var (gate, line, price) = BetGate(view, entity);
        if (!gate.Allowed || line is null) return new(gate, null, false, 0, 0, "");
        // ARC-UI-2139: a worn item is taken off the merchant's slot (0x461CA0 → 0x467860 → 0x467CB0), from the merchant itself, never from a substitute container.
        var worn = view.WornWares.Any(item => item.Entity == entity);
        var bet = new SessionGambleBet(Merchant.Entity, _merchantGambling?.Invoke()?.Level ?? 0, entity, line.Count, price, line.Kind, GoldKind(), worn ? null : Merchant.Container,
            WornItem: worn, NpcWearer: worn ? _inventory.NpcWearer?.Invoke(_session.QueryActor().Map, Merchant.Entity) : null);
        var result = _session.Gamble(_session.Version, bet, _inventory.InsertCheck, AreaInventoryHost.DiagnosticInsertCheck, _inventory.ItemScripts(_session));
        if (!result.Settled)
        {
            var text = result.Move is { } move
                ? move.Failure == GameSessionFailure.GoldShort ? "not enough money" : $"refused: {AreaInventoryHost.Refusal(move.Failure, move.Verdict)}"
                : $"refused: {result.Failure}";
            return new(gate, result, result.Won, 0, 0, text);
        }
        var change = 0;
        string said;
        if (result.Won)
        {
            change = GamblingRules.WonReaction(line.Worth, line.Count, ReactionRules.Category(_reaction()));
            said = BetLine(GamblingRules.WonCategory);
        }
        else
        {
            if (TradeRules.GiftAmount(TradeDirection.PcBuys, line.Worth, price, line.Count) is { } amount)
                change = TradeRules.GiftReaction(amount, ReactionRules.Category(_reaction()));
            said = BetLine(GamblingRules.LostCategory, result.Paid);
        }
        if (change != 0 && !_adjustReaction(change)) change = 0;
        return new(gate, result, result.Won, result.Paid, change, said);
    }

    private AreaBarterTrade Settle(TradeDirection direction, AreaBarterLine line, InventoryMutationResult result)
    {
        var total = line.Quote.Price * line.Count;
        if (result.Disposition != GameSessionDisposition.Changed)
            return new(result, line.Count, 0, 0, result.Failure == GameSessionFailure.GoldShort
                ? direction == TradeDirection.PcBuys ? "not enough money" : "the merchant cannot pay"
                : $"refused: {AreaInventoryHost.Refusal(result.Failure, result.Verdict)}");
        var change = 0;
        if (TradeRules.GiftAmount(direction, line.Worth, line.Quote.Price, line.Count) is { } amount)
        {
            change = TradeRules.GiftReaction(amount, ReactionRules.Category(_reaction()));
            if (change != 0 && !_adjustReaction(change)) change = 0;
        }
        var verb = direction == TradeDirection.PcBuys ? "bought" : "sold";
        return new(result, line.Count, total, change, $"{verb} {line.Text} for {total}" + (change == 0 ? "" : $" (reaction {change:+0;-0})"));
    }

    private AreaBarterTrade Refused(string text) =>
        new(new(GameSessionDisposition.Rejected, GameSessionFailure.None, null, null, _session.Version), 0, 0, 0, text);

    private TradeParties Parties(int haggle, SkillTraining training, int reaction) => new(Merchant.Multiplier, haggle, training, reaction,
        BuyScriptAccepts: !Merchant.BuyScript, Fence: Merchant.Fence);

    private AreaBarterLine Line(string entity, SessionItemKind kind, TradeDirection direction, TradeParties parties, bool worn, int count, bool ignoreFollower = false)
    {
        var facts = ItemFacts(entity, kind);
        var (identified, stored, worth, condition) = (facts.Identified, facts.Stored, facts.Worth, facts.Condition);
        var item = facts.Item;
        var trade = new TradeItem(worth, stored, item?.Flags ?? 0, condition, worn, kind.Type == SessionItemType.Gold);
        var quote = Merchant.Follower && !ignoreFollower ? new TradeQuote(0, TradeRefusal.None)
            : TradeRules.Price(direction, trade, parties with { Deals = Merchant.Deals(kind.Prototype - 20) });
        var text = _name(entity, kind) + (count > 1 ? $" ×{count}" : "") + (worn ? " (worn)" : "");
        return new(entity, text, count, quote, worth, kind) { ItemFlags = item?.Flags ?? 0 };
    }

    /// <summary>ARC-UI-1731: what the trade and the item services read of an item: its stored item fields, whether it counts as identified (the
    /// identified test with the stored flag or the session's: ARC-WORLD-090, the open identifies a merchant's goods, a sale the sold
    /// item), its stored worth, the worth the quote starts from (the unidentified worth for an unidentified item) and its condition.</summary>
    internal AreaItemFacts ItemFacts(string entity, SessionItemKind kind)
    {
        var fields = Fields(entity, kind);
        var item = fields?.Item;
        var identified = item is not null && (item.MagickTechComplexity <= 0 || (item.Flags & 0x1) != 0 || _session.QueryItemIdentified(entity));
        var stored = item?.Worth ?? 0;
        var worth = identified ? Math.Max(Arcanum.Simulation.Equipment.EquipmentChoice.MinimumWorth, stored)
            : Arcanum.Simulation.Equipment.EquipmentChoice.UnidentifiedWorth;
        ItemCondition? stock = fields is null ? null : SessionItemConditions.FromCombatFields(fields);
        var condition = stock is { } baseline ? _session.QueryItemCondition(entity, baseline) ?? baseline : default;
        return new(item, identified, stored, worth, condition, stock);
    }

    /// <summary>ARC-UI-1731: the session the window runs over, the item services' own trades.</summary>
    internal GameSessionOwner Session => _session;

    /// <summary>ARC-UI-1731: the gold pile kind a payment to the merchant uses (the data's prototype 9076).</summary>
    internal SessionItemKind GoldPileKind => GoldKind();

    /// <summary>ARC-UI-1731: the haggle the hero's sale prices use (the hero's Haggle effectiveness and training).</summary>
    internal (int Haggle, SkillTraining Training) HeroHaggle() => _session.QueryHeroHaggle() ?? (0, SkillTraining.None);

    // An item's stored fields: its object row's (instance over prototype), else its kind's prototype.
    private CombatFields? Fields(string entity, SessionItemKind kind)
    {
        foreach (var map in _inventory.Maps)
            if (map.TryGetRow(entity, out var row)) return CombatFieldReader.Read(row).Fields;
        return _data.Prototype(kind.Prototype);
    }

    private int? Units(string entity) => _session.QueryAmmo(entity) is { } stack ? stack.Units : null;

    private SessionItemKind GoldKind() => _data.GoldKind();
}
