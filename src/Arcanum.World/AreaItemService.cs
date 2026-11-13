using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>One line of an item service window: the hero's item, its text, and the quote the window would make for it now.</summary>
public sealed record AreaItemServiceLine(string Entity, string Text, ItemServiceQuote Quote);

/// <summary>What one click on an item did (ledger IS4): it selected the item and quoted it, or (the item already selected) confirmed it.</summary>
public enum AreaItemServiceAction
{
    /// <summary>A different item than the selected one: it is selected and quoted (the quote may be a refusal).</summary>
    Selected,
    /// <summary>The selected item again: its quote was refused or no price is kept, nothing is paid.</summary>
    Refused,
    /// <summary>The selected item again with a kept price above the hero's gold: the "no money" line, nothing is done.</summary>
    NoMoney,
    /// <summary>The service ran and was paid.</summary>
    Done,
    /// <summary>The service ran but the payment was refused (full records), or the service itself was refused (a stale view).</summary>
    Failed,
    /// <summary>Nothing happened (not one of the hero's items, or no skills of the NPC for a repair).</summary>
    None,
}

/// <summary>The outcome of one click: the action, the quote the click made, the gold paid, the item's condition after a repair (null otherwise) and
/// the window's message (PROJECT text: the generated NPC lines are not loaded; the line range is in the quote).</summary>
public sealed record AreaItemServiceStep(AreaItemServiceAction Action, ItemServiceQuote? Quote, int Paid, ItemCondition? Condition, string Text);

/// <summary>The window's state: the service, the NPC's name, the hero's gold, its items with their quotes, the selected item, the kept price and the message.</summary>
public sealed record AreaItemServiceView(ItemServiceKind Kind, string Npc, int HeroGold, IReadOnlyList<AreaItemServiceLine> Items, string? Selected,
    int KeptPrice, string Message);

/// <summary>
/// ARC-UI-1731 (contract docs/world/item-service-runtime.md, ledger IS1–IS6): the model of the item identify window (the inventory window in
/// mode 5, opened by the dialog action <c>ii</c>) and the item repair window (mode 6, <c>ri</c>): the hero's items over the session, the
/// original's two-click flow (0x573850: the first click selects the item and quotes it, the next click on the same item pays and does it),
/// the identify price 100 (0x5786C0) and the repair quote of the NPC repairer (0x578760, <see cref="ItemDurabilityRules.RepairQuote"/>), the
/// forced repair run of the NPC (0x4C8E60, <see cref="GameSessionOwner.RepairForService"/>) and the payment to the NPC
/// (<see cref="GameSessionOwner.GiveGoldToNpc"/>). The price the window keeps between clicks follows <see cref="ItemRuleProfile.ServiceKeepsStalePrice"/>.
/// Not modelled: the original look of the window (a diagnostic list in the viewer), the generated NPC lines' text, the round view, the
/// network branches (a client sends a request).
/// </summary>
public sealed class AreaItemService
{
    private readonly AreaBarter _barter;
    private readonly Func<(SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)?> _npc;
    private string? _selected;
    private int _price = ItemServiceRules.NoPrice;
    private string _message = string.Empty;

    /// <param name="barter">The item model of the NPC's own trade window (the hero's items, their facts and the NPC as merchant).</param>
    /// <param name="npc">The NPC's levelled sheet and effect modifiers for its Repair skill (null: unknown, a repair is not offered).</param>
    public AreaItemService(AreaBarter barter, ItemServiceKind kind, Func<(SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)?> npc)
    {
        _barter = barter ?? throw new ArgumentNullException(nameof(barter));
        _npc = npc ?? throw new ArgumentNullException(nameof(npc));
        Kind = kind;
    }

    public ItemServiceKind Kind { get; }
    public AreaBarterMerchant Merchant => _barter.Merchant;

    /// <summary>Whether the window opens: a living hero, as the original's open (only a null, dead or non-PC player refuses).</summary>
    public static bool HeroCanUse(GameSessionOwner session) => AreaBarter.HeroCanTrade(session);

    /// <summary>The window's lines now.</summary>
    public AreaItemServiceView View()
    {
        var items = _barter.View().Hero.Select(line => new AreaItemServiceLine(line.Entity, line.Text, QuoteOf(line.Entity, line.Kind, out _) ?? default)).ToArray();
        return new(Kind, Merchant.Name, _barter.Session.QueryGold(), items, _selected, _price, _message);
    }

    /// <summary>
    /// One click on an item (ledger IS3). A click on another item than the selected one selects it and quotes it
    /// (<see cref="ItemServiceRules.KeptPrice"/> keeps the price). A click on the selected item quotes it again and, when the kept price is above 0,
    /// pays for the service: a price above the hero's gold is the "no money" line (0x413910); else the service runs on the item (identify
    /// marks it, repair is the NPC's forced run) and the hero pays the price to the NPC. The kept price is then cleared whatever happened.
    /// </summary>
    /// <remarks>Executable: the click handler of the inventory window 0x573850 in its modes 5 and 6 (registered as <c>AreaInventoryDrag.Release</c> for the own window).</remarks>
    public AreaItemServiceStep Click(string entity)
    {
        var line = _barter.View().Hero.FirstOrDefault(item => item.Entity == entity);
        if (line is null) return Report(new(AreaItemServiceAction.None, null, 0, null, "not one of the hero's items"));
        if (QuoteOf(entity, line.Kind, out var failure) is not { } quote) return Report(new(AreaItemServiceAction.None, null, 0, null, failure ?? "no quote"));
        var profile = _barter.Session.Rules.Items;
        var selecting = entity != _selected;
        _selected = entity;
        _price = ItemServiceRules.KeptPrice(_price, quote, profile);
        if (selecting) return Report(new(AreaItemServiceAction.Selected, quote, 0, null, QuoteText(quote)));
        var price = _price;
        _price = ItemServiceRules.NoPrice;
        if (price <= 0) return Report(new(AreaItemServiceAction.Refused, quote, 0, null, QuoteText(quote)));
        if (_barter.Session.QueryGold() < price) return Report(new(AreaItemServiceAction.NoMoney, quote, 0, null,
            $"not enough money (class line {ItemServiceRules.NoMoneyGroup}): {price} gold wanted"));
        return Report(Perform(entity, line, quote, price));
    }

    private AreaItemServiceStep Perform(string entity, AreaBarterLine line, ItemServiceQuote quote, int price)
    {
        var session = _barter.Session;
        ItemCondition? condition = null;
        if (Kind == ItemServiceKind.Identify)
        {
            var marked = session.MarkItemIdentified(session.Version, entity);
            if (marked.Disposition == GameSessionDisposition.Rejected) return new(AreaItemServiceAction.Failed, quote, 0, null, $"identify refused: {marked.Failure}");
        }
        else
        {
            if (_npc() is not { } npc || _barter.ItemFacts(entity, line.Kind).Baseline is not { } baseline)
                return new(AreaItemServiceAction.Failed, quote, 0, null, "repair refused: no data of the repairer or the item");
            var repaired = session.RepairForService(session.Version, Merchant.Entity, npc.Loaded, npc.Modifiers, entity, baseline);
            if (repaired.Disposition == GameSessionDisposition.Rejected) return new(AreaItemServiceAction.Failed, quote, 0, null, $"repair refused: {repaired.Failure}");
            condition = repaired.Condition;
        }
        var paid = session.GiveGoldToNpc(session.Version, Merchant.Entity, price, _barter.GoldPileKind, AreaInventoryHost.DiagnosticInsertCheck);
        var verb = Kind == ItemServiceKind.Identify ? "identified" : condition is null ? "repair run" : "repaired";
        if (paid.Disposition == GameSessionDisposition.Rejected)
            return new(AreaItemServiceAction.Failed, quote, 0, condition, $"{verb}, but the payment was refused: {paid.Failure}");
        return new(AreaItemServiceAction.Done, quote, price, condition, $"{verb} {line.Text} for {price} gold");
    }

    // The quote of an item now, or null with the reason (a repair without the NPC's skills).
    private ItemServiceQuote? QuoteOf(string entity, SessionItemKind kind, out string? failure)
    {
        failure = null;
        var facts = _barter.ItemFacts(entity, kind);
        if (Kind == ItemServiceKind.Identify) return ItemServiceRules.Identify(facts.Identified);
        if (_npc() is not { } npc) { failure = "no skills of the repairer"; return null; }
        var session = _barter.Session;
        var training = (RepairTraining)(int)session.QueryNpcSkills(npc.Loaded, npc.Modifiers)[Skill.Repair].Training;
        var (haggle, haggleTraining) = _barter.HeroHaggle();
        // 0x461F80 with the unchecked flag: the PC's sale price to the repairer, before the damage scaling the quote applies itself.
        var price = TradeRules.UncheckedSalePrice(new(facts.Worth, facts.Stored, facts.Item?.Flags ?? 0, facts.Condition, false, kind.Type == SessionItemType.Gold),
            new(Merchant.Multiplier, haggle, haggleTraining));
        return ItemServiceRules.Repair(ItemDurabilityRules.RepairQuote(facts.Condition, price, training, session.Rules.Items));
    }

    private AreaItemServiceStep Report(AreaItemServiceStep step)
    {
        _message = step.Text;
        return step;
    }

    private string QuoteText(ItemServiceQuote quote) => quote.Status switch
    {
        ItemServiceStatus.Quoted => $"{(Kind == ItemServiceKind.Identify ? "identify" : "repair")} for {quote.Price} gold: click the item again to pay",
        ItemServiceStatus.AlreadyIdentified => "already identified",
        ItemServiceStatus.NothingToRepair => "nothing to repair",
        _ => "a broken item: only a master of Repair mends it",
    };
}
