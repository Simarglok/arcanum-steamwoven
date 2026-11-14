using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-046: follower orders in crash-site-area (exterior, cave and map mode). F1–F6 send the original's presets with the tile
// under the pointer (F6 per the active rules, D2); the follower portrait strip opens each follower's order menu (walk asks for a
// tile with the next left click). Orders go through the session; the HUD and the log name the outcomes and their line ranges.
// ARC-WORLD-117: the attack order's target is the highlighted object (F2: the one under the pointer at the release; the portrait's
// attack: the object of the next left press, its target cursor), and with the AI host each reached follower gets its AI pass.
public partial class TerrainViewerApp
{
    private AreaFollowerPortraits? _followerPortraits;
    // A portrait walk order waiting for its tile, or (ARC-WORLD-117) an attack order waiting for its object (the original's target
    // cursor); any non-modifier key, a modal window, focus loss, another menu choice or a rebuilt group drops it.
    private (string Entity, CompanionOrderType Type)? _pendingOrder;
    // The portraits' last state (session, revision, group, modal), so a frame with nothing new does not rebuild them.
    private (Arcanum.Session.GameSessionOwner? Session, long Revision, AreaCompanionGroup? Group, bool Modal, object? Names, object? Combat)? _portraitStamp;
    // ARC-DEV-337 (B52): the process frame from which a shown follower without life values is asked again (null: none is missing). The stamp holds no
    // value of the bars, so a portrait built before the follower's record resolved (a map change, a window of the main map read later) would stay empty
    // at full health; a follower the host never has values for is asked twice a second, not every frame.
    private ulong? _portraitLifeRetry;
    private const ulong PortraitLifeRetryFrames = 30;

    /// <summary>Orders are taken only while the world owns input: focused, no modal window or control focus, no transfer pending.</summary>
    private bool OrderInputOpen => _worldFocused && !PanelOutsideStackOpen && _interfaceWindows.FollowerOrderKeys(AnotherHasTheTurn) && !WorldUiFocus && !AreaTransferPending;

    // The F1–F6 key pressed while the world owned input; its release sends the preset (the original acts on key up).
    private WorldKey? _orderKeyArmed;

    /// <summary>F1–F6 without modifiers while the session party is wired: the presets, sent on the release of a key pressed while the
    /// world owned input, with the tile under the pointer at the release (the original's key up). A press or release while the world
    /// does not own input shows "ignored". Other keys, a modifier, or no party, are not orders.</summary>
    private bool RouteOrderKey(WorldKeyDecision decision, WorldKeyModifiers modifiers)
    {
        if (decision.Key is < WorldKey.F1 or > WorldKey.F6 || modifiers != WorldKeyModifiers.None) return false;
        if (_areaSession is not { } session || _areaCompanions is null || _conversationHost is not { PartyWired: true }) return false;
        if (decision.Echo) return true;
        var key = decision.Key.ToString();
        if (!OrderInputOpen)
        {
            _orderKeyArmed = null;
            _companionNotice = $"{key} ignored: the mode or a window owns the input, another has the turn, the world has no input focus or a map change is pending";
            RefreshReadyStatus();
            return true;
        }
        if (decision.Pressed) { _orderKeyArmed = decision.Key; return true; }
        if (_orderKeyArmed != decision.Key) return true;
        _orderKeyArmed = null;
        var preset = CompanionOrderRules.Preset(decision.Key - WorldKey.F1, session.Rules.Orders);
        if (!preset.All)
        {
            _companionNotice = $"{key}: no order (the original's preset names nobody)";
            GD.Print($"{AreaCompanionOrders.LogPrefix} order source={key} type={preset.Type} reached=0 (names nobody)");
            RefreshReadyStatus();
            return true;
        }
        // ARC-WORLD-117: the attack preset's target is the highlighted object at the release (0x43C570).
        IssueAreaOrder(key, new(preset.Type, CompanionOrderAddressing.All, [], Tile: PointerCell())
            { Target = preset.Type == CompanionOrderType.Attack ? OrderTarget(_hoverKey) : null });
        return true;
    }

    /// <summary>The chart cell under the pointer, or null off the map.</summary>
    private CellCoord? PointerCell()
    {
        if (_canvas is null) return null;
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition();
        return AreaCellPicking.Pick(new(chart.X, chart.Y));
    }

    /// <summary>ARC-WORLD-117: an object of the shown map as the attack order's target, or null.</summary>
    private SessionCombatant? OrderTarget(Arcanum.Data.Area.AreaObjectKey? key) =>
        _combatHost is { } combat ? AreaCompanionOrders.Target(combat, CurrentAreaMap, key) : null;

    /// <summary>Sends an order (its followers' facts are gathered here) and shows its outcomes. ARC-WORLD-117: through the shown map's
    /// AI host when it runs (the followers' combat facts, the attack event's world, each follower's AI pass).</summary>
    private SessionCompanionOrderResult? IssueAreaOrder(string source, CompanionOrderRequest request)
    {
        if (_areaSession is not { } session || _areaCompanions is not { } group || _conversationHost is not { } host
            || _worldActor.Session is not AreaGridSession pc)
        {
            _companionNotice = $"{source}: order not sent (no area session)";
            RefreshReadyStatus();
            return null;
        }
        var ai = AreaAiHostNow();
        // Only the combat orders read the combat facts (an invalid one refuses the whole order); without the host an attack order goes
        // without a target, as before ARC-WORLD-117 (a target would lift NO_ATTACK and fail for want of the attack event's world).
        Func<string, SessionPeaceFacts>? combat = ai is not null && request.Type is CompanionOrderType.Attack or CompanionOrderType.BackOff ? ai.OrderFacts : null;
        var facts = AreaCompanionOrders.Facts(session, group, pc.State.CommittedCell, host.State, CompanionName, combat);
        var (result, noGoal) = AreaCompanionOrders.Issue(session, group, request with { Followers = facts, Target = ai is null ? null : request.Target },
            pc.State.CommittedCell, WorldTime, ai);
        GD.Print(AreaCompanionOrders.Log(source, request.Type, result) + (noGoal.Count == 0 ? "" : " | goal not started: " + string.Join(", ", noGoal))
            + (request.Type == CompanionOrderType.Attack ? $" | target={request.Target?.Entity ?? "none"} ai={ai is not null}" : ""));
        SayOrderAcknowledgements(result);
        _companionNotice = result.Failure != GameSessionFailure.None ? $"{source}: order refused ({result.Failure})"
            : result.Outcomes.Count == 0 ? $"{source}: {request.Type} reached no follower within {CompanionOrderRules.Reach} tiles"
            : $"{source}: " + string.Join("; ", result.Outcomes.Select(AreaCompanionOrders.Describe))
                + (noGoal.Count == 0 ? "" : $" (no goal started for {noGoal.Count}: tile off the map)");
        RefreshFollowerPortraits();
        RefreshReadyStatus();
        return result;
    }

    /// <summary>
    /// ARC-UI-2079 (ledger TB28, the order handler's broadcast callback): each reached follower says the acknowledgement line its order gives (2100-2199, the refusal 2200-2299, the farewells 1900-2099) to the broadcaster, the hero, who is the
    /// listener its speech display turns it to. The follower's can-speak test is the line builder's (flag 1 for an acknowledgement; flag 0 for a refusal or a farewell, which also refuses a follower in combat focus or fleeing). No active
    /// test stands before them, unlike the AI sites. Not modelled: the curse's race line, and the move order's lines (the viewer sends no move order).
    /// </summary>
    private void SayOrderAcknowledgements(SessionCompanionOrderResult result)
    {
        foreach (var outcome in result.Outcomes)
        {
            if (outcome.Lines is not { } lines) continue;
            var flagZero = outcome.Line is CompanionOrderLine.Cannot or CompanionOrderLine.FriendlyFarewell or CompanionOrderLine.HostileFarewell;
            SayLine(outcome.Entity, lines, Arcanum.Simulation.Ai.AiSchedulerRules.Pc, refusesFightOrFlee: flagZero);
        }
    }

    /// <summary>The name the PC knows a companion by: its hover name on the shown map, else its dialog script.</summary>
    private string CompanionName(AreaCompanion companion) =>
        _hoverIndex is { } index && ReferenceEquals(_hoverIndexArea, _readyArea) && index.Contains(companion.Key) && index.Name(companion.Key) is { Length: > 0 } name
            ? name : companion.Template.Created ? companion.Entity : $"script {companion.Speaker.ScriptId}";

    /// <summary>Shows a portrait per follower in the array (in its order) while the session party is wired; none while a modal window
    /// is open (its menu closes and a pending walk order is dropped). Rebuilt only when the session, its revision, the group or the
    /// modal state changed.</summary>
    private void RefreshFollowerPortraits()
    {
        RefreshHeroGauges();
        // ARC-UI-020: the combat host gives the portraits' bars their values.
        var stamp = (_areaSession, _areaSession?.Version.Revision ?? -1, _areaCompanions, WorldModal, (object?)_hoverIndex, (object?)_combatHost);
        if (_portraitStamp == stamp && (_portraitLifeRetry is not { } retry || Engine.GetProcessFrames() < retry)) return;
        // ARC-UI-1736 (FP1, the module's reset 0x56A880): a new session (a new game, a load) shows the panel again from the first follower.
        if (_portraitStamp is { } previous && !ReferenceEquals(previous.Session, _areaSession) && _areaSession is not null) _followerPortraits?.ResetPanel();
        _portraitStamp = stamp;
        _portraitLifeRetry = null;
        if (_areaSession is not { } session || _areaCompanions is not { } group || _conversationHost is not { PartyWired: true } || WorldModal)
        {
            // A modal window only covers the strip (ARC-UI-1736: the scroll offset stays); no session, group or party shows none.
            if (WorldModal && _areaSession is not null && _areaCompanions is not null && _conversationHost is { PartyWired: true }) _followerPortraits?.Suspend();
            else _followerPortraits?.Show([]);
            _followerPortraits?.CloseMenu();
            if (TargetsDroppedByModal) { CancelPendingOrder("a window opened"); _orderKeyArmed = null; }
            return;
        }
        if (_followerPortraits is null)
        {
            _followerPortraits = new AreaFollowerPortraits(); AddChild(_followerPortraits);
            _followerPortraits.ItemChosen += FollowerMenuChosen;
            _followerPortraits.PortraitClicked += FollowerPortraitClick;
            _followerPortraits.PortraitHovered += FollowerPortraitHovered;
            _followerPortraits.PortraitHoverLeft += FollowerPortraitHoverLeft;
            _followerPortraits.MenuFacts = FollowerMenuFacts;
            // ARC-UI-024: the frames stand at the window's top left, where the diagnostic HUD starts.
            _followerPortraits.EdgeChanged += () => _hud?.SetLeftInset(_followerPortraits.RightEdge);
        }
        var shown = group.Companions.ToDictionary(c => c.Entity, StringComparer.Ordinal);
        var followers = session.QueryParty().Followers.Where(f => shown.ContainsKey(f.Entity)).Select(f => shown[f.Entity]).ToArray();
        // ARC-UI-024: the frames' art and each follower's portrait, read when a new one is needed.
        LoadFollowerPortraits(followers);
        // ARC-UI-020: each portrait's bars from the follower's life.
        var shownFollowers = followers.Select(c => (c.Entity, CompanionName(c), CompanionLife(c), FollowerPortrait(c))).ToArray();
        if (shownFollowers.Any(f => f.Item3 is null)) _portraitLifeRetry = Engine.GetProcessFrames() + PortraitLifeRetryFrames;
        _followerPortraits.Show(shownFollowers);
    }

    /// <summary>
    /// ARC-DEV-280 (B33): the left button's release over a follower's portrait is a click on that follower, as the original's portrait button
    /// handler (0x56A9D0, button event 1) passes the follower to the world click dispatcher 0x54EA80 like a press on its body: out of combat
    /// mode a talk (goal 21, 0x54ED30), in combat mode nothing without Alt. The click is taken under the world click's guards; the target modes aim
    /// (FP6); ARC-UI-1787: a pending walk or attack order (mode 18, which the dispatcher does not list) leaves it the ordinary click, a talk.
    /// </summary>
    private void FollowerPortraitClick(string entity)
    {
        // The round guard first (0x4B6D70, 0x4B6D80), then the interface mode (0x551A00, FP6).
        if (!WorldClickInputOpen || AreaTransferPending || HeroLyingDown || _areaSession?.QueryCombatRound() is { } round && CombatModeActive && !round.Actor.IsHero)
        {
            _companionNotice = "follower click ignored: a window, a cursor or another's turn owns the input";
            RefreshReadyStatus();
            return;
        }
        // ARC-UI-1736 (FP6, the portrait dispatcher 0x54EA80): in the spell target mode (1), the skill target mode (2) and the item mode (16) the portrait is a
        // second way to aim at the follower (a heal, a buff, a repair); the target's own valid-target test decides. ARC-UI-1787: armed lockpicks are mode 16 too
        // (the lockpick path of 0x571CB0), and the follower order's target mode (18) is no mode of the dispatcher's three: the click is the ordinary one below.
        if (_gameCursorItem is null && _spellGoal is null && _pendingOrder is null)
        {
            if (_spellTargeting is not null) { FollowerAimSpell(entity); return; }
            if (_itemAiming is not null) { FollowerAimItem(entity); return; }
            if (_lockpickArmed is not null) { FollowerAimLockpick(); return; }
            if (PickPocketArmed) { FollowerAimPickPocket(entity); return; }
            if (DisarmArmed) { CancelDisarm("a follower is no trap"); _companionNotice = "disarm traps: a follower is no trap"; RefreshReadyStatus(); return; }
        }
        if (_gameCursorItem is not null || _spellTargeting is not null || _spellGoal is not null || ItemTargetOpen || PickPocketArmed || DisarmArmed)
        {
            _companionNotice = "follower click ignored: a window, a cursor or another's turn owns the input";
            RefreshReadyStatus();
            return;
        }
        if (HeroInCombatMode)
        {
            _companionNotice = "follower click in combat mode: attacking a follower is not ready"; // 0x54EFDA: the left Alt attacks it, nothing happens without
            RefreshReadyStatus();
            return;
        }
        var companion = _areaCompanions?.Companions.FirstOrDefault(c => c.Entity == entity);
        CancelPortalApproach("a follower click replaced it", keepArmed: true);
        CancelAreaAttack("a follower click replaced it");
        if (companion is null || PresentSpeaker(companion.Key) is not { } speaker || !TryAreaTalk(speaker))
        {
            _companionNotice = "follower click: no talk started";
            RefreshReadyStatus();
            return;
        }
        SetObjectApproachGait();
    }

    /// <summary>The cast host's id of a follower (its row on the shown map), or null for a visiting follower, which has none here.</summary>
    private string? FollowerObjectId(string entity) =>
        _areaCompanions?.Companions.FirstOrDefault(c => c.Entity == entity) is { } companion ? SpellPickId(companion.Key) : null;

    /// <summary>ARC-UI-1736 (FP6, 0x4F2680 then the spell handler 0x57C110): the open spell cursor takes the follower as the object target, with no tile.</summary>
    private void FollowerAimSpell(string entity)
    {
        if (_spellTargeting is not { } targeting || InventoryOpen) return;
        if (FollowerObjectId(entity) is not { } at || SpellCasterNow() is not { } caster) { _spellNotice = "no target here"; RefreshAreaStatus(); return; }
        var pick = caster.PickTarget(SessionSpellCaster.Hero, targeting.Spell, targeting.Mask, at, null);
        GD.Print($"ARC-UI-1736 portrait-aim spell={targeting.Spell} follower={entity} passes={pick.Test.Passes} reason=0x{pick.Test.Reason:X}");
        SpellTargetApply(targeting, pick.Object?.Id, pick.Tile, pick.Test);
    }

    /// <summary>ARC-UI-1736 (FP6, the item handler 0x571CB0): the open item cursor takes the follower as the object target, as a press on its body would.</summary>
    private void FollowerAimItem(string entity)
    {
        if (_itemAiming is not { } aim) return;
        if (ItemAimPickAt(aim, FollowerObjectId(entity), null) is not { } pick) { ItemAimNotice("no target here"); return; }
        GD.Print($"ARC-UI-1736 portrait-aim item={aim.Item} follower={entity} passes={pick.Test.Passes}");
        if (!pick.Test.Passes) { ItemAimRefused(pick); return; }
        ApplyItemAim(aim, pick.Object?.Id, pick.Tile, false, false);
    }

    /// <summary>ARC-UI-1787 (FP6, mode 16 with the lockpicks, the item handler 0x571CB0 after the target test 0x4F2680, whose mask for an item with no spell is any object, then the use 0x462CC0, whose lockpick branch tests the target's type with 0x441CF0): the
    /// armed lockpicks take the follower as the target object, as a press on a body that is no door, window or container does (<see cref="TryAreaPortalUse"/>): a critter is
    /// no lock (the type test 0x441CF0 answers no, PL2), so the pick ends and the item mode pops (SA51). Like the original's use it touches no goal of the hero (the world press also drops a walk to talk or use, a limit of that press).</summary>
    private void FollowerAimLockpick()
    {
        CancelAreaAttack("armed lockpicks took the portrait click");
        DisarmLockpicks();
        _portalNotice = "pick a lock cancelled: not a door, window or container";
        GD.Print("ARC-UI-1787 portrait-aim lockpicks follower target is no lock, mode popped");
        RefreshAreaStatus();
    }

    /// <summary>ARC-UI-1736 (FP6, the skill handler 0x57A1F0): the armed pick pocket takes the follower as its victim (the skill's own rules decide).</summary>
    private void FollowerAimPickPocket(string entity)
    {
        CancelAreaAttack("an armed pick pocket took the press");
        PickPocketOn(_areaCompanions?.Companions.FirstOrDefault(c => c.Entity == entity)?.Key);
    }

    private void FollowerMenuChosen(string entity, FollowerMenuItem item)
    {
        // A new choice replaces a walk or attack order still waiting for its target.
        CancelPendingOrder("another menu choice");
        if (!OrderInputOpen)
        {
            _companionNotice = "follower order ignored: the mode or a window owns the input, another has the turn, the world has no input focus or a map change is pending";
            RefreshReadyStatus();
            return;
        }
        // ARC-UI-1736 (FP11, FP12): the two window rows open the follower's windows (the order rules have nothing to send for them).
        if (item == FollowerMenuItem.Inventory) { OpenFollowerInventory(entity); return; }
        if (item == FollowerMenuItem.CharacterSheet) { OpenFollowerSheet(entity); return; }
        if (CompanionOrderRules.PortraitOrder(item, FollowerMenuFacts(entity).MindControlled) is not { } type)
        {
            _companionNotice = $"{AreaCompanionOrders.Label(item)}: not modelled for followers";
            RefreshReadyStatus();
            return;
        }
        if (type is CompanionOrderType.WalkHere or CompanionOrderType.Attack)
        {
            // OL1: the target cursor does not open for a dead or unconscious PC.
            if (HeroCannotPickOrderTarget)
            {
                _companionNotice = $"{AreaCompanionOrders.Label(item)}: not while the hero is dead or unconscious";
                RefreshReadyStatus();
                return;
            }
            // ARC-DEV-110 (MIG16 S3): the order waiting for its target is interface mode 18 (follower order) on the viewer's stack. The
            // mode is an overlay pushed over the top, so the other target modes are left first (the viewer takes one press at a time).
            var word = AreaCompanionOrders.Label(item);
            CancelSpellTargeting($"a {word} order took the next press");
            CancelArmedLockpicks($"a {word} order took the next press");
            CancelPickPocket($"a {word} order took the next press");
            CancelDisarm($"a {word} order took the next press");
            if (!OpenOverlayMode(InterfaceMode.FollowerOrder, _ => _pendingOrder = null))
            {
                _companionNotice = $"{word}: not ready (the interface mode did not open)";
                RefreshReadyStatus();
                return;
            }
            // The HUD's companion status names the pending order while it waits.
            _pendingOrder = (entity, type);
            _companionNotice = null;
            RefreshReadyStatus();
            return;
        }
        IssueAreaOrder("portrait", new(type, CompanionOrderAddressing.Portrait, [], entity, AreaCompanionOrders.Label(item)));
    }

    /// <summary>ARC-UI-1736 (FP12, 0x56AB68, the editor 0x5597C0 with mode 2): the follower's character sheet, the NPC sheet (never greyed; the editor's own refusal for a dead hero).</summary>
    private void OpenFollowerSheet(string entity)
    {
        if (_areaCompanions?.Companions.FirstOrDefault(c => c.Entity == entity) is not { } companion) return;
        GD.Print($"ARC-UI-1736 follower character sheet entity={entity}");
        OpenNpcSheet(() => BuildNpcSheet(companion.Speaker.Row, entity), entity);
    }

    private bool HeroCannotPickOrderTarget => _areaSession?.QueryHeroLife()?.Status is { IsDead: true } or { IsUnconscious: true };

    /// <summary>The HUD's word of a pending order.</summary>
    private static string PendingOrderWord(CompanionOrderType type) => type == CompanionOrderType.Attack ? "attack" : "walk";

    /// <summary>Drops a pending walk or attack order with its reason in the HUD (nothing when none is pending).</summary>
    private void CancelPendingOrder(string why)
    {
        if (_pendingOrder is not { } pending) return;
        CloseTargetMode(InterfaceMode.FollowerOrder);
        _companionNotice = $"{PendingOrderWord(pending.Type)} order cancelled: {why}";
    }

    /// <summary>The pending order's target: the next press after choosing walk (its tile) or, ARC-WORLD-117, attack (the object under
    /// it, the original's object target cursor; an empty spot sends the order without one, which does nothing but the AI pass). True
    /// when it took the press.</summary>
    private bool TryPendingOrder(Vector2 screen)
    {
        if (_pendingOrder is not { } pending || _canvas is null) return false;
        var entity = pending.Entity;
        // The press pops the follower order mode (SA51: a click pops and the follower speaks the order).
        CloseTargetMode(InterfaceMode.FollowerOrder);
        // The caller (WorldRouteClick) has already ignored a press while the world does not own input.
        if (HeroCannotPickOrderTarget)
        {
            _companionNotice = $"{PendingOrderWord(pending.Type)} order cancelled: the hero is dead or unconscious";
            RefreshReadyStatus();
            return true;
        }
        if (pending.Type == CompanionOrderType.Attack)
        {
            IssueAreaOrder("portrait", new(CompanionOrderType.Attack, CompanionOrderAddressing.Portrait, [], entity,
                AreaCompanionOrders.Label(FollowerMenuItem.Attack)) { Target = OrderTarget(AreaObjectAtScreen(screen)) });
            return true;
        }
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is not { } cell) { _companionNotice = "walk: no tile there"; RefreshReadyStatus(); return true; }
        IssueAreaOrder("portrait", new(CompanionOrderType.WalkHere, CompanionOrderAddressing.Portrait, [], entity,
            AreaCompanionOrders.Label(FollowerMenuItem.Walk), cell));
        return true;
    }
}
