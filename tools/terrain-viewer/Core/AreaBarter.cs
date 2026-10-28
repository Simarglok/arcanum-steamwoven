using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Reaction;

namespace Arcanum.TerrainViewer.Core;

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
        var opened = ContentStack.Open(found.Plan!);
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
/// and whether it follows the hero (a follower trades for nothing, ledger BT6).
/// </summary>
public sealed record AreaBarterMerchant(string Entity, string Name, int Multiplier, bool Fence, bool BuyScript, InventorySourceBuyList BuyList, bool Follower)
{
    public const int BuyObjectPoint = 17;

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
}

/// <summary>The window's state: both sides' gold and lines, the hero's haggle and the merchant's reaction.</summary>
public sealed record AreaBarterView(AreaBarterMerchant Merchant, int HeroGold, int MerchantGold, int Haggle, SkillTraining Training, int Reaction,
    IReadOnlyList<AreaBarterLine> Hero, IReadOnlyList<AreaBarterLine> Wares);

/// <summary>The outcome of one buy or sell: the session's result, the units, the price paid in all and the reaction change asked for.</summary>
public sealed record AreaBarterTrade(InventoryMutationResult Result, int Count, int Total, int ReactionChange, string Text)
{
    public bool Moved => Result.Disposition == GameSessionDisposition.Changed;
}

/// <summary>
/// ARC-UI-050: the barter window's model over the session (contract docs/world/barter-runtime.md, viewer docs/rendering/terrain-viewer.md):
/// the hero's items with their sale quotes, the merchant's items that are not worn and not coins with their buy quotes
/// (<see cref="TradeRules.Price"/>), and the trades through <see cref="GameSessionOwner.BarterBuy"/> and
/// <see cref="GameSessionOwner.BarterSell"/>, then the reaction change of a generous trade (0x4C11D0 through the session). A
/// quote of 0 cancels the trade outside a follower's window, a hero with less gold than a unit price is refused before the move
/// ("not enough money"), and a sale pays at most the merchant's coins. Not modelled: the substitute container, the identified flag,
/// the restock timer, the buy-object script run and the generated quote lines (ledger BT6–BT9).
/// </summary>
public sealed class AreaBarter
{
    private readonly GameSessionOwner _session;
    private readonly AreaInventoryHost _inventory;
    private readonly AreaBarterData _data;
    private readonly Func<int> _reaction;
    private readonly Func<int, bool> _adjustReaction;
    private readonly Func<string, SessionItemKind, string> _name;

    /// <param name="reaction">The merchant's standard effective reaction to the hero.</param>
    /// <param name="adjustReaction">Applies a reaction change to the merchant through the session; false when the session refused it.</param>
    /// <param name="name">An item's shown name by entity and kind (the window's own text).</param>
    public AreaBarter(GameSessionOwner session, AreaInventoryHost inventory, AreaBarterData data, AreaBarterMerchant merchant, Func<int> reaction,
        Func<int, bool> adjustReaction, Func<string, SessionItemKind, string> name)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        Merchant = merchant ?? throw new ArgumentNullException(nameof(merchant));
        _reaction = reaction ?? throw new ArgumentNullException(nameof(reaction));
        _adjustReaction = adjustReaction ?? throw new ArgumentNullException(nameof(adjustReaction));
        _name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public AreaBarterMerchant Merchant { get; }

    /// <summary>Whether the window opens: a hero that is alive (the original's open refuses only a dead or missing PC, 0x572370).</summary>
    public static bool HeroCanTrade(GameSessionOwner session) =>
        session.QueryHero() is not null && session.QueryHeroLife() is { Status.IsDead: false };

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
        var holder = _session.QueryMapInventory(_session.QueryActor().Map)?.Holders.FirstOrDefault(h => h.Entity == Merchant.Entity);
        var wares = (holder?.Items ?? [])
            .Where(item => !item.Facts.Worn && item.Facts.Kind.Type != SessionItemType.Gold)
            .Select(item => Line(item.Entity, item.Facts.Kind, TradeDirection.PcBuys, parties, false,
                item.Facts.AmmoType is not null ? item.Facts.Quantity ?? 1 : 1))
            .ToArray();
        return new(Merchant, _session.QueryGold(), _session.QueryNpcGold(Merchant.Entity) ?? 0, haggle, training, reaction, hero, wares);
    }

    /// <summary>The hero buys a merchant's item (all units of a stack) at its quote.</summary>
    public AreaBarterTrade Buy(string entity)
    {
        var line = View().Wares.FirstOrDefault(item => item.Entity == entity);
        if (line is null) return Refused("not one of the merchant's wares");
        if (line.Quote.Price == 0 && !Merchant.Follower) return Refused($"won't sell ({line.Quote.Refusal})");
        if (_session.QueryGold() < line.Quote.Price) return Refused("not enough money");
        var result = _session.BarterBuy(_session.Version, Merchant.Entity, entity, line.Count, Merchant.Follower ? 0 : line.Quote.Price, line.Kind,
            GoldKind(), _inventory.InsertCheck, AreaInventoryHost.DiagnosticInsertCheck, _inventory.ItemScripts(_session));
        return Settle(TradeDirection.PcBuys, line, result);
    }

    /// <summary>The hero sells one of its items (all units of a stack) at its quote, at most the merchant's coins.</summary>
    public AreaBarterTrade Sell(string entity)
    {
        var line = View().Hero.FirstOrDefault(item => item.Entity == entity);
        if (line is null) return Refused("not one of the hero's items");
        if (line.Quote.Price == 0 && !Merchant.Follower) return Refused($"won't buy ({line.Quote.Refusal})");
        var price = Merchant.Follower ? 0 : TradeRules.SalePayment(line.Quote.Price, _session.QueryNpcGold(Merchant.Entity) ?? 0);
        var result = _session.BarterSell(_session.Version, Merchant.Entity, entity, line.Count, price, line.Kind, AreaInventoryHost.DiagnosticInsertCheck);
        return Settle(TradeDirection.PcSells, line with { Quote = line.Quote with { Price = price } }, result);
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

    private AreaBarterLine Line(string entity, SessionItemKind kind, TradeDirection direction, TradeParties parties, bool worn, int count)
    {
        var fields = Fields(entity, kind);
        var item = fields?.Item;
        // The identified test of 0x464370 (EquipmentChoice.Identified); the merchant's goods are all identified when the window opens
        // (0x572640), a follower's are not.
        var identified = item is not null && (item.MagickTechComplexity <= 0 || (item.Flags & 0x1) != 0 ||
            direction == TradeDirection.PcBuys && !Merchant.Follower);
        var stored = item?.Worth ?? 0;
        var worth = identified ? Math.Max(Arcanum.Simulation.Equipment.EquipmentChoice.MinimumWorth, stored)
            : Arcanum.Simulation.Equipment.EquipmentChoice.UnidentifiedWorth;
        var condition = fields is null ? default : SessionItemConditions.FromCombatFields(fields) is { } baseline
            ? _session.QueryItemCondition(entity, baseline) ?? baseline : default;
        var trade = new TradeItem(worth, stored, item?.Flags ?? 0, condition, worn, kind.Type == SessionItemType.Gold);
        var quote = Merchant.Follower ? new TradeQuote(0, TradeRefusal.None)
            : TradeRules.Price(direction, trade, parties with { Deals = Merchant.BuyList.Deals(kind.Prototype - 20) });
        var text = _name(entity, kind) + (count > 1 ? $" ×{count}" : "") + (worn ? " (worn)" : "");
        return new(entity, text, count, quote, worth, kind);
    }

    // An item's stored fields: its object row's (instance over prototype), else its kind's prototype.
    private CombatFields? Fields(string entity, SessionItemKind kind)
    {
        foreach (var map in _inventory.Maps)
            if (map.TryGetRow(entity, out var row)) return CombatFieldReader.Read(row).Fields;
        return _data.Prototype(kind.Prototype);
    }

    private int? Units(string entity) => _session.QueryAmmo(entity) is { } stack ? stack.Units : null;

    private SessionItemKind GoldKind() => _data.Prototype(AreaBarterData.GoldPrototype) is { } gold
        ? new(SessionItemType.Gold, AreaBarterData.GoldPrototype, gold.Object.Name, gold.Object.Description)
        : new(SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0);
}
