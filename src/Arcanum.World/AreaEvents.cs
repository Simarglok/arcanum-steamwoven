using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Travel;
using Arcanum.Simulation;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-039: the AREA viewer's world for event passes. An NPC's life facts come from the area's combat fields
/// (<see cref="AreaCombatHost"/>, the same reader the hero's attack uses); an object a deferred call names exists when it is
/// the PC or an object the stored objects of a reachable map hold. A follower the session moved to another map (its timers and call
/// roles travel with it) is asked by identity (ARC-DEV-076, MIG3: the resolver's record), so its events are not dropped as missing. A call runs through the shown area's conversation host and its SCR dispatcher
/// (<see cref="AreaConversationHost.RunCall"/>). The providers are read at each question, so the world follows the
/// viewer's current hosts. ARC-WORLD-045: a wait timeout's reaction change reads the shown area's conversation state (its
/// diagnostic NPC facts, and the hero's or the diagnostic PC's facts); without a conversation host it has none. ARC-WORLD-096: a
/// restock event runs through the viewer's restock host (<see cref="AreaRestockHost"/>); without one it waits a minute.
/// </summary>
public sealed class AreaEventWorld : ISessionScriptWorld, ISessionPartyEventWorld, ISessionLootWindowWorld, ISessionAiWorld, ISessionRestockWorld, ISessionBodyWorld, ISessionEncounterWorld, ISessionNewspaperWorld, ISessionNpcStatsWorld
{
    private readonly Func<IAreaEventObjects?> _combat;
    private readonly Func<AreaConversationHost?> _talk;
    private readonly Func<bool>? _lootShowsContainer;
    private readonly Func<ISessionAiWorld?>? _ai;
    private readonly Func<ISessionCombatWorld?>? _round;

    /// <param name="prepareCall">ARC-DEV-081 (MIG5): runs before a deferred call does, so the host lists the call's attachee when it is a
    /// companion carried to the shown map (<see cref="AreaConversationHost.AdmitVisitor"/>); the call runs through <paramref name="talk"/>.</param>
    /// <param name="lootShowsContainer">ARC-WORLD-063: whether the viewer's loot window shows a container (a junk pile); an item's
    /// decay then waits a minute (ledger ID3, ID6). Null: never.</param>
    /// <param name="ai">ARC-WORLD-092: the shown map's AI host, which an AI tick of the pass runs through; null or a null answer: no AI
    /// runs (every NPC counts as gone and its sector as not loaded, so its tick schedules no next one).</param>
    /// <param name="round">ARC-WORLD-092: the turn-based round's world of the shown map, for the AI's subturn ends; null: none.</param>
    /// <param name="encounter">ARC-WORLD-1159: the viewer's encounter host; null or a null answer: no encounter roll.</param>
    /// <param name="newspaper">ARC-UI-1739: the module's written texts (the newspaper file's headlines), which arm the Newspapers chain and draw the default issue; null or a null answer: no chain is armed.</param>
    /// <param name="restock">ARC-WORLD-096: the restock host of the pass's session; null or a null answer: a restock event waits.</param>
    /// <param name="itemScripts">ARC-MAGIC-1684: the shown map's item script host, which an NPC's best-items wield runs its items' move scripts through; null or a null answer: such an item is not moved.</param>
    /// <param name="callDialog">ARC-WORLD-2358: told the conversation an applied deferred call's run started (its dialog action, the original's opener 0x567460 inside the run), which the viewer opens at its next frame; null: such a conversation is not opened.</param>
    public AreaEventWorld(Func<IAreaEventObjects?> combat, Func<AreaConversationHost?> talk, Action<SessionScriptCall>? prepareCall = null,
        Func<bool>? lootShowsContainer = null, Func<ISessionAiWorld?>? ai = null, Func<ISessionCombatWorld?>? round = null,
        Func<AreaRestockHost?>? restock = null, Func<AreaEncounterHost?>? encounter = null, Func<Arcanum.Data.Written.WrittenTexts?>? newspaper = null,
        Func<ISessionItemScriptHost?>? itemScripts = null, Action<Arcanum.Scripting.Scr.ScrDialogStart>? callDialog = null)
    {
        _callDialog = callDialog;
        _itemScripts = itemScripts;
        _newspaper = newspaper;
        _encounter = encounter;
        _restock = restock;
        _ai = ai;
        _round = round;
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _talk = talk ?? throw new ArgumentNullException(nameof(talk));
        _prepareCall = prepareCall;
        _lootShowsContainer = lootShowsContainer;
    }

    // ---- ARC-UI-1739: the Newspapers event's texts, forwarded from the viewer's reading data ----

    private readonly Func<Arcanum.Data.Written.WrittenTexts?>? _newspaper;
    /// <summary>The written texts of the module; null without a host or while they load (no chain is armed).</summary>
    public Arcanum.Data.Written.WrittenTexts? NewspaperTexts => _newspaper?.Invoke();

    // ---- ARC-WORLD-1159: the Random Encounters event's host, forwarded to the viewer's encounter host ----

    private readonly Func<AreaEncounterHost?>? _encounter;
    /// <summary>The tables of the module's encounter chart; null without a host (no event is armed, no roll happens).</summary>
    public EncounterTables? EncounterTables => _encounter?.Invoke()?.EncounterTables;
    public SessionEncounterSite? EncounterSite() => _encounter?.Invoke()?.EncounterSite();

    public bool LootWindowShowsContainer => _lootShowsContainer?.Invoke() == true;

    /// <summary>ARC-WORLD-1158: where a critter's body lies now, from the shown map's AI host (null without one, or for a critter it does not hold).</summary>
    public CellCoord? BodyCell(SessionMapId map, string entity) => (_ai?.Invoke() as ISessionBodyWorld)?.BodyCell(map, entity);

    // ---- ARC-WORLD-096: the restock event's host, forwarded to the viewer's restock host ----

    private readonly Func<AreaRestockHost?>? _restock;
    private AreaRestockHost? Restock => _restock?.Invoke();
    public bool RestockReady => Restock is not null;
    public Arcanum.Simulation.Items.RestockHolder? RestockFacts(SessionMapId map, string entity) => Restock?.Facts(map, entity);
    public string? RestockMerchant(SessionMapId map, string container) => Restock?.Merchant(map, container);
    public bool Persistent(SessionMapId map, string item) => Restock?.Persistent(map, item) == true;
    public SessionRestockSet RestockSet(int source) => Restock?.Set(source) ?? new(SessionRestockSetStatus.Unsupported, null);
    public SessionRestockItem? RestockItem(int basicPrototype) => Restock?.Item(basicPrototype);
    public SessionItemKind RestockPileKind => Restock?.PileKind ?? new(SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0);
    // The viewer's insert check: every item fits (carry weight and room need the created character, NOT READY).
    public SessionInsertCheck RestockInsertCheck(SessionMapId map, string holder) => AreaInventoryHost.DiagnosticInsertCheck;
    // ARC-WORLD-101: a generated-once container's tile x and the hero's player name.
    public SessionGeneratedOnceFacts? GeneratedOnceFacts(SessionMapId map, string container) => Restock?.GeneratedOnceFacts(map, container);
    /// <summary>ARC-WORLD-103: an NPC's best-items wield after its regeneration reads its combat values from the area's combat fields.</summary>
    public SessionNpcWielder? RestockWielder(SessionMapId map, string npc) => Restock?.Wielder(map, npc, _combat()?.Critter(npc));

    private readonly Func<ISessionItemScriptHost?>? _itemScripts;

    /// <summary>
    /// ARC-MAGIC-1684: what the item event scripts of an NPC's best-items wield need: the item script host, the NPC's life target and its stored stats (the combat host's, with what a spell wrote laid over them); null when the host has
    /// no script host or does not know the NPC's combat values.
    /// </summary>
    public SessionNpcWieldScripts? NpcWieldScripts(SessionMapId map, string npc) =>
        _itemScripts?.Invoke() is { } host && _combat() is { } objects && objects.Critter(npc)?.Target is { } target && objects.StoredStats(npc) is { Count: SessionCritterIndexedWrites.StatCount } stats
            ? new(host, target, stats) : null;

    /// <summary>ARC-MAGIC-1561: the carried light of an NPC whose worn items the session recalculated (<see cref="AreaRestockHost.NpcLight"/>); null: the saved light stands (also while the restock host loads).</summary>
    public Arcanum.Data.Lighting.CarriedLightSize? NpcLight(SessionMapId map, string npc) => Restock?.NpcLight(map, npc, _combat());
    /// <summary>ARC-MAGIC-1561: <see cref="NpcLight"/> for several NPCs of a map at once; null while the restock host loads (nothing is known yet: the caller must not keep the answer).</summary>
    public IReadOnlyDictionary<string, Arcanum.Data.Lighting.CarriedLightSize>? NpcLights(SessionMapId map, IEnumerable<string> npcs) => Restock?.NpcLights(map, npcs, _combat());

    private readonly Action<SessionScriptCall>? _prepareCall;
    private readonly Action<Arcanum.Scripting.Scr.ScrDialogStart>? _callDialog;

    /// <summary>The reason the last refused call did not run, or null; <see cref="CallRefusals"/> counts the refusals.</summary>
    public string? LastCallRefusal { get; private set; }
    public int CallRefusals { get; private set; }

    public LifeFacts? CritterFacts(SessionMapId map, string entity)
    {
        // ARC-DEV-076 (MIG3): by identity, wherever the object stands: a follower the session moved answers as on its home map.
        return _combat()?.Facts(entity);
    }

    /// <summary>ARC-MAGIC-1568: the NPC's stored base stats by identity, from the shown map's combat host (null without one).</summary>
    public IReadOnlyList<int>? StoredStats(SessionMapId map, string entity) => _combat()?.StoredStats(entity);

    public bool ObjectExists(SessionMapId map, string entity) =>
        entity == SessionEventKinds.PlayerEntity || _combat()?.HasObject(entity) == true;

    public Arcanum.Simulation.Reaction.ReactionNpcFacts? ReactionNpcFacts(SessionMapId map, string entity) => _talk()?.State.NpcFacts();

    public Arcanum.Simulation.Reaction.ReactionPcFacts? ReactionPcFacts() => _talk()?.State.PcFacts;

    [OriginalFunction(0x445140, OriginalFunctionScope.Partial)]
    public bool RunScriptCall(SessionScriptCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        _prepareCall?.Invoke(call);
        if (_talk() is not { } host) { Refuse("no script host on this map"); return false; }
        var ran = host.RunCall(call, out var detail);
        if (!ran) Refuse(detail);
        else if (host.LastCallDialog is { } talk) _callDialog?.Invoke(talk);
        return ran;
    }

    private void Refuse(string detail)
    {
        LastCallRefusal = detail;
        CallRefusals++;
    }

    // ---- ARC-WORLD-092: the AI tick's host, forwarded to the shown map's AI host ----

    private ISessionAiWorld? Ai => _ai?.Invoke();

    public SessionLifeTarget? AiLifeTarget(string entity) => Ai?.AiLifeTarget(entity);
    public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => _round?.Invoke()?.ScanCombatants(map) ?? [];
    public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => _round?.Invoke()?.CombatantFacts(map, entity);
    public long DistanceToPc(string npc) => Ai?.DistanceToPc(npc) ?? -1;
    public bool RunScript(Arcanum.Simulation.Ai.AiScriptPoint point, string npc, string? triggerer) => Ai?.RunScript(point, npc, triggerer) ?? true;
    public bool RunWaypoints(string npc) => Ai?.RunWaypoints(npc) ?? false;
    public void RunStandpoints(string npc) => Ai?.RunStandpoints(npc);
    public bool IsGenerator(string npc) => Ai?.IsGenerator(npc) ?? false;
    public long? Spawn(string npc) => Ai?.Spawn(npc);
    public bool IsEncounterCreature(string npc) => Ai?.IsEncounterCreature(npc) ?? false;
    public void Destroy(string npc) => Ai?.Destroy(npc);
    public void InterruptAnimations(string npc) => Ai?.InterruptAnimations(npc);
    // Without a host the tick is not rescheduled (its NPC counts as gone and its sector as not loaded), as another host consumes it.
    public bool SectorLoaded(string npc) => Ai?.SectorLoaded(npc) ?? false;
    public Arcanum.Simulation.Ai.AiCatchUpFacts CatchUp(string npc) => Ai?.CatchUp(npc) ?? new();
    public void MoveToLeader(string npc) => Ai?.MoveToLeader(npc);
    public bool MapClearing => Ai?.MapClearing ?? false;
    public bool Sleeping(string npc) => Ai?.Sleeping(npc) ?? false;
    public bool Hidden(string npc) => Ai?.Hidden(npc) ?? false;
    public bool PcHidden => Ai?.PcHidden ?? false;
    public Arcanum.Simulation.Ai.AiConversation Conversation(string npc) => Ai?.Conversation(npc) ?? Arcanum.Simulation.Ai.AiConversation.None;
    public bool ConversationCallback => Ai?.ConversationCallback ?? false;
    public void EndConversation(string npc) => Ai?.EndConversation(npc);
    public int AnimationPriority(string npc) => Ai?.AnimationPriority(npc) ?? 0;
    public bool StunAnimationRunning(string npc) => Ai?.StunAnimationRunning(npc) ?? false;
    public bool AnimationRunning(string npc) => Ai?.AnimationRunning(npc) ?? false;
    public void ReWield(string npc, bool allSlots) => Ai?.ReWield(npc, allSlots);
    public string? Leader(string npc) => Ai?.Leader(npc);
    public void EndMaintainedSpells(string npc) => Ai?.EndMaintainedSpells(npc);
    public bool Heal(string npc) => Ai?.Heal(npc) ?? false;
    public bool SearchItems(string npc) => Ai?.SearchItems(npc) ?? false;
    public void UpdateDanger(string npc) => Ai?.UpdateDanger(npc);
    public void Perform(string npc) => Ai?.Perform(npc);
    public bool Loading => Ai?.Loading ?? false;
}
