using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Presentation.Hud;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-021 (alpha N4, slice 4 of docs/research/combat-hud.md): floating text in crash-site-area. Every attack the viewer runs through
// the session (the hero's on an NPC or an object, a projectile's strike, an NPC's on the hero or another NPC) makes the original's
// floaters (CombatFloaters, ledger CH12) at the options window's "text floaters" level, with the texts of the player's mes\combat.mes;
// they rise at the "float speed" step every 50 ms or more of wall time and fade (CH11) over their object: the hero, a companion or an
// NPC where it stands now, an object on its cell. The lines belong to the shown map: a new scene drops them.
public partial class TerrainViewerApp
{
    private const string HeroFloater = AiSchedulerRules.Pc;
    private AreaFloaterLayer? _floaters;
    private object? _floaterScene;
    // An owner's cell when the line was made, for an object no live lookup finds.
    private readonly Dictionary<string, (long X, long Y)> _floaterCells = new(StringComparer.Ordinal);
    private string? _combatTextsModule;
    private Task<AreaCombatTexts>? _combatTextsTask;
    private AreaCombatTexts? _combatTexts;
    // Smokes step the lines by their own clock.
    private bool _floaterClockForSelfTest;

    /// <summary>Starts reading the floaters' texts for this module once (a reload, the cave or a map transfer reuses them).</summary>
    private void BindCombatTexts(TerrainViewerArguments arguments)
    {
        if (_combatTextsModule == arguments.ModulePath) return;
        _combatTextsModule = arguments.ModulePath; _combatTexts = null;
        var module = arguments.ModulePath;
        _combatTextsTask = SelfTestBootstrap ? Task.FromResult(AreaCombatTexts.Project("self-test: no game folder")) : Task.Run(() => AreaCombatTexts.Load(module));
    }

    /// <summary>The texts, waiting at most a second for a read still running (the project's text after that, read again next time).</summary>
    private AreaCombatTexts CombatTexts()
    {
        if (_combatTexts is { } read) return read;
        if (_combatTextsTask is not { } task) return AreaCombatTexts.Project("not bound");
        // A wait on the handle never throws for a faulted task (Task.Wait would).
        if (!task.IsCompleted && !((IAsyncResult)task).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1))) return AreaCombatTexts.Project("still loading");
        _combatTexts = task.IsCompletedSuccessfully ? task.Result : AreaCombatTexts.Project("load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _combatTextsTask = null;
        GD.Print($"ARC-UI-021 combat texts fromGame={_combatTexts.FromGame} detail={_combatTexts.Detail}");
        return _combatTexts;
    }

    /// <summary>A critter's side for its floater colour: the PC, a companion the PC leads, anyone else.</summary>
    private FloaterSide FloaterSideOf(string entity) =>
        entity == HeroFloater ? FloaterSide.Pc : _areaSession?.QueryCompanion(entity).LedByPc == true ? FloaterSide.PcFollower : FloaterSide.Other;

    /// <summary>The floaters of an attack on a critter by <paramref name="attacker"/> on <paramref name="target"/> (entities, or the
    /// hero's <see cref="HeroFloater"/>).</summary>
    private void FloatCritterAttack(SessionAttackResult result, string attacker, string target)
    {
        var hit = AreaCombatFloaters.Of(result, FloaterSideOf(attacker), FloaterSideOf(target));
        Float(hit, attacker, result.Recipient == Arcanum.Simulation.Combat.DamageRecipient.Attacker ? attacker : target);
    }

    /// <summary>The floaters of the hero's attack on an object (a door, window, container, scenery, trap or item; a wall without a
    /// row), its lines over the object's cell.</summary>
    private void FloatObjectAttack(SessionObjectAttackResult result, string target, AreaObjectRecord? row, string? attacker = null)
    {
        // A portal's or scenery's entity names no inventory row: its cell comes from its map row.
        if (row is { WorldX: not null, WorldY: not null }) _floaterCells[target] = LiveWorldCell(row);
        // ARC-COMBAT-1140: an NPC's arrow on an object is that NPC's attack (its side colours the lines); the hero's otherwise.
        var hit = AreaCombatFloaters.Of(result, attacker is null ? FloaterSide.Pc : FloaterSideOf(attacker));
        Float(hit, attacker ?? HeroFloater, result.Life is null ? target : attacker ?? HeroFloater);
    }

    /// <summary>ARC-COMBAT-1138: the damage line of an object (or a ground item) that damage without an attacker reached (a spell, a trap, a burn, a
    /// script), over the object's cell; shown at "text floaters" 2 only, in white (CH12, CH13).</summary>
    private void FloatObjectDamage(string target, AreaObjectRecord? row, CombatFloaterHit hit)
    {
        if (row is { WorldX: not null, WorldY: not null }) _floaterCells[target] = LiveWorldCell(row);
        Float(hit, HeroFloater, target);
    }

    private void Float(CombatFloaterHit? hit, string attacker, string struck)
    {
        if (hit is null || _readyMode != TerrainViewerMode.Area || _readyArea is null) return;
        var floaters = CombatFloaters.Of(hit, _gameSettings.Floats, CombatTexts().Text);
        if (floaters.Count == 0) return;
        var layer = FloaterLayer();
        DropFloatersOfOldScene(layer);
        foreach (var floater in floaters)
        {
            var owner = floater.Over == FloaterOver.Attacker ? attacker : struck;
            if (FloaterCell(owner) is { } cell) _floaterCells[owner] = cell;
            if (!layer.Field.Add(owner, floater.Text, floater.Color)) GD.Print($"ARC-UI-021 floater refused owner={owner} (field full)");
        }
        GD.Print($"ARC-UI-021 floaters attacker={attacker} struck={struck} level={_gameSettings.Floats} lines={string.Join("|", floaters.Select(f => $"{f.Over}:{f.Color}:{f.Text}"))}");
        layer.Redraw();
    }

    // ARC-SCRIPT-050, ARC-UI-1997: the float lines (23) of the dialog scripts the viewer ran, over their speakers. The original hands them to the dialog
    // interface's float callback (0x568430 → its speech display 0x568540), which makes a text bubble over the speaker (0x4D5FE0, ledger TB19): white for
    // anyone, green for a PC, living the "text duration" setting. The same queue carries the lines of tile scripts, of the followers' comments and of a
    // greeting that opens no window.
    private int _scriptFloats;

    // ARC-DEV-298 (B20): the float lines of tile scripts (owner, text), queued when the run was applied.
    private readonly Queue<(string Owner, string Text, string? Listener)> _tileScriptFloats = new();

    /// <summary>The bubble clock: the real one (the original's timeGetTime), or the smoke's own.</summary>
    private long BubbleNowMs() => _floaterClockForSelfTest ? BubbleClockForSelfTest : (long)Time.GetTicksMsec();

    internal long BubbleClockForSelfTest { get; set; }

    /// <summary>ARC-UI-1997: a text bubble over <paramref name="owner"/> (the speech display makes one): the owner's old bubble is replaced; true when it was made.</summary>
    private bool Bubble(string owner, string text, bool dialogLine = false, bool persistent = false, string? listener = null)
    {
        if (_readyMode != TerrainViewerMode.Area || _readyArea is null || string.IsNullOrWhiteSpace(text)) return false;
        var layer = FloaterLayer();
        DropFloatersOfOldScene(layer);
        if (FloaterCell(owner) is { } cell) _floaterCells[owner] = cell;
        layer.Bubbles.SetTextDuration(_gameSettings.TextDuration);
        layer.AddBubble(owner, text, owner == HeroFloater ? BubbleColor.Green : BubbleColor.White, BubbleNowMs());
        if (persistent) layer.Bubbles.SetLife(owner, TextBubbleRules.Persistent, BubbleNowMs());
        if (!dialogLine) _scriptFloats++;
        // ARC-UI-2055 (ledger TB26): the speech display also turns an idle living speaker to its listener, for every caller that passes one.
        if (listener is not null) FaceListenerOf(owner, listener);
        return true;
    }

    /// <summary>
    /// ARC-UI-2055 (ledger TB26): the stop-and-face step of the speech display for a float line or a bark: a living speaker that runs no animation of its own turns to its listener; the listener never turns. An
    /// NPC speaker turns through the AI host (<see cref="AreaAiHost.FaceListener"/>), the hero through the hero request host's rotate starter (<see cref="FaceRequest"/>: refused for a hero that cannot act or has a main run).
    /// Nothing turns for a listener that is the speaker itself (the script float line over the PC) or one with no cell here. True when the speaker was turned (the hero: when its rotate starter started a turn).
    /// </summary>
    private bool FaceListenerOf(string speaker, string listener)
    {
        if (speaker == listener) return false;
        if (speaker != HeroFloater) return AreaAiHostNow()?.FaceListener(speaker, listener) ?? false;
        if (FloaterCell(listener) is not { } at) return false;
        var result = HeroRequests.Apply(new FaceRequest(new(checked((int)at.X), checked((int)at.Y))));
        GD.Print($"ARC-UI-2055 hero speaker faces its listener={listener} outcome={result.Outcome} rotation={result.Rotation?.ToString() ?? "-"}");
        return result.Rotation is not null;
    }

    /// <summary>The bubble owner a float line's listener names: the hero's, or the object's entity code; null when the line has no listener.</summary>
    private static string? ListenerOwner(ScrObjectRef? listener) => listener is not { } object_ ? null : object_.Code == AreaScriptHost.PcCode ? HeroFloater : object_.Code;

    /// <summary>The open dialog's speaker as a bubble owner (its entity id), or null.</summary>
    private string? DialogBubbleOwner() => _conversationSpeaker is { } speaker ? AreaConversationState.EntityId(speaker.Row) : null;

    /// <summary>
    /// ARC-UI-2027 (ledger TB23): the NPC's line in an open dialog is a persistent bubble over the NPC (life −2: it
    /// does not age while the window is open); the window lists only the PC's options. The speaker's old bubble is replaced. A dialog that no longer awaits a reply (its final line) shows the line with the default life.
    /// </summary>
    private void ShowDialogLineBubble(string text, bool persistent)
    {
        if (DialogBubbleOwner() is not { } owner) return;
        // The reply's own float lines (a script's float over the speaker) ran before the answer is shown: they go first, so the answer's bubble is the one that stays.
        TickScriptFloats();
        if (!Bubble(owner, text, dialogLine: true, persistent: persistent)) return;
        GD.Print($"ARC-UI-2027 dialog line bubble owner={owner} persistent={persistent} text={text}");
    }

    /// <summary>ARC-UI-2027 (ledger TB24): a processed reply removes the speaker's bubble before the answer shows.</summary>
    private void RemoveDialogLineBubble()
    {
        if (DialogBubbleOwner() is { } owner && _floaters is { } layer && layer.Bubbles.Remove(owner)) layer.Redraw();
    }

    /// <summary>ARC-UI-2027 (ledger TB25): the dialog's end gives the speaker's bubble its default life from now (it was persistent).</summary>
    private void ExpireDialogLineBubble(AreaSpeaker? speaker)
    {
        if (speaker is null || _floaters is not { } layer) return;
        layer.Bubbles.SetLife(AreaConversationState.EntityId(speaker.Row), TextBubbleRules.DefaultLife, BubbleNowMs());
    }

    private void TickScriptFloats()
    {
        while (_tileScriptFloats.Count > 0)
        {
            var (tileOwner, tileText, tileListener) = _tileScriptFloats.Dequeue();
            if (Bubble(tileOwner, tileText, listener: tileListener)) GD.Print($"ARC-DEV-298 float owner={tileOwner} floated={_scriptFloats} text={tileText}");
        }
        if (_conversationHost?.Scripts is not { } scripts) return;
        foreach (var line in scripts.TakeFloats())
        {
            var owner = line.Speaker == scripts.Pc ? HeroFloater : line.Speaker.Code;
            if (Bubble(owner, line.Text, listener: ListenerOwner(line.Listener))) GD.Print($"ARC-SCRIPT-050 float owner={owner} floated={_scriptFloats} text={line.Text}");
        }
    }

    private AreaFloaterLayer FloaterLayer()
    {
        if (_floaters is { } layer) return layer;
        // ARC-DEV-156 (B28): the lines in the original's font art 229 when it read (the viewer's font without a game folder).
        _floaters = new AreaFloaterLayer(TextFonts()?.Text) { Anchor = FloaterAnchor, BubbleAnchor = BubbleAnchorOf, BubbleView = () => new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size) };
        AddChild(_floaters);
        // First among the app's children: on its layer it draws under the diagnostic HUD.
        MoveChild(_floaters, 0);
        _floaterScene = _readyArea;
        return _floaters;
    }

    /// <summary>A new scene drops the lines of the old one (and the cells kept for them).</summary>
    private void DropFloatersOfOldScene(AreaFloaterLayer layer)
    {
        if (ReferenceEquals(_floaterScene, _readyArea)) return;
        layer.Field.Clear(); layer.Bubbles.Clear(); _floaterCells.Clear(); _floaterScene = _readyArea; layer.Redraw();
    }

    /// <summary>
    /// ARC-DEV-296 (B42, B39): the damage the cast host applied since the last frame that is no weapon swing or shot (a spell's, a trap's, a
    /// burn's, a script's) floats its lines over the victim as the original's damage function does (CH12), and the experience awards a
    /// spell or a PC-placed trap earned (ARC-DEV-295) show their level-up messages and level the followers like a weapon's.
    /// </summary>
    private void TickCasterDamage()
    {
        // A caster left over from another session (a quick load) is not drained against the new one.
        if (_spellCaster is not { } caster || !ReferenceEquals(caster.Owner, _areaSession)) return;
        foreach (var step in caster.TakeDamageSteps())
        {
            var victim = step.Victim == SessionSpellCaster.Hero ? HeroFloater : step.Victim;
            var attacker = step.Attacker is null ? victim : step.Attacker == SessionSpellCaster.Hero ? HeroFloater : step.Attacker;
            Float(AreaCombatFloaters.Of(step, FloaterSideOf(victim)), attacker, victim);
        }
        foreach (var award in caster.TakeExperienceAwards()) LevelAreaFollowers(award);
    }

    // ARC-AI-1638, ARC-AI-1680 (0x4B4390 asks 0x4AEAB0 inside the damage step, at the moment of each hit):
    /// <summary>
    /// The cast host's callback for a spell, trap or burn step that has just landed. A follower it brings below a fifth says its near-death line then, so its can-speak gate reads
    /// the state after this step and not after a later step of the same frame; a script's step asked it already in its answer.
    /// </summary>
    private void AskNearDeathOfCastStep(SessionSpellDamageStep step)
    {
        // A caster left over from another session (a quick load) asks nothing against the new one.
        if (step.FromScript || step.Victim == SessionSpellCaster.Hero || _spellCaster is not { } caster || !ReferenceEquals(caster.Owner, _areaSession)) return;
        if (FollowerHurt(step.Victim, step.Life) is { } near)
            GD.Print($"ARC-AI-1638 near-death follower={near.Speaker} lines={near.Lines.First}-{near.Lines.Last} at-cast-step attacker={step.Attacker ?? "-"}");
    }

    /// <summary>Each frame: a new scene drops the lines; otherwise they rise (every 50 ms or more) and are drawn where their objects are.</summary>
    private void TickFloaters()
    {
        TickScriptFloats();
        TickCasterDamage();
        if (_floaters is not { } layer) return;
        DropFloatersOfOldScene(layer);
        layer.Level = _gameSettings.Floats;
        // ARC-UI-1997: the Text Duration line changes the live bubbles' life at once, then the real clock ages them.
        if (layer.Bubbles.TextDuration != _gameSettings.TextDuration) layer.Bubbles.SetTextDuration(_gameSettings.TextDuration);
        // An expired last bubble needs one more draw, to be wiped from the surface.
        var expired = layer.Bubbles.Tick(BubbleNowMs());
        if (expired || layer.Bubbles.Count > 0) layer.Redraw();
        if (layer.Field.Count == 0) return;
        if (!_floaterClockForSelfTest) layer.Field.Advance((long)Time.GetTicksMsec(), _gameSettings.FloatSpeed);
        layer.Redraw();
    }

    /// <summary>The cell an owner stands on now: the hero's committed cell, a companion's live cell, an NPC the AI walked, a map row's
    /// cell; else the cell it had when its line was made.</summary>
    private (long X, long Y)? FloaterCell(string owner)
    {
        if (owner == HeroFloater)
        {
            if (_worldActor.Session is not AreaGridSession grid) return null;
            var at = grid.Chart.ToWorld(grid.State.CommittedCell);
            return (at.X, at.Y);
        }
        if (_areaCompanions is { } group && group.Map.RuntimeMap == CurrentAreaMap && group.Companions.FirstOrDefault(c => c.Entity == owner) is { } companion)
            return group.WorldCell(companion.Speaker);
        if (_inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } map && map.TryGetRow(owner, out var row)
            && row.WorldX is not null && row.WorldY is not null)
            return LiveWorldCell(row);
        return _floaterCells.TryGetValue(owner, out var kept) ? kept : null;
    }

    /// <summary>The screen point 90 px (at the view's zoom) above an owner's tile centre, the floater box's bottom centre.</summary>
    private Vector2? FloaterAnchor(string owner) => ObjectScreenPoint(owner, FloaterRules.BoxRise);

    /// <summary>ARC-UI-1997: a bubble's anchor, the owner's tile centre on the screen with the original's (40, 20), without the draw offset.</summary>
    private Vector2? BubbleAnchorOf(string owner) => ObjectScreenPoint(owner, 0);

    private Vector2? ObjectScreenPoint(string owner, int rise)
    {
        if (_canvas is null || _readyArea is not { } area || _readyMode != TerrainViewerMode.Area || FloaterCell(owner) is not { } cell) return null;
        var chart = area.Plan.Chart;
        var projected = TerrainGeometry.Project(checked((int)(cell.X - chart.OriginX)), checked((int)(cell.Y - chart.OriginY)));
        return _canvas.GetGlobalTransformWithCanvas() * new Vector2(projected.X + TextBubbleRules.AnchorX, projected.Y + TextBubbleRules.AnchorY - rise);
    }

    internal AreaFloaterLayer? FloatersForSelfTest => _floaters;
}
