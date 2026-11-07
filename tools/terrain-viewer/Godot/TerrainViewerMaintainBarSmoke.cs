using Arcanum.Data.Magic;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-MAGIC-026 native observation, inside the cast stage before the hotkey bar (an automated run, which draws the bars only because
    // this smoke asks): pointer events go through the window's GUI. The hero casts the first maintained college spell (from the data, no
    // spell named: a maintenance period, magic, an icon, no damage at BEGIN) that starts at itself or else at <paramref name="npc"/>,
    // released at once (no casting goal is played); its first maintenance tick puts it in slot 0, which shows the spell's icon while the
    // slots at or beyond the hero's intelligence / 4 show their plugged art and the open empty ones their slot art. Hovering slot 0
    // describes the spell; a left click on it plays 3000 and 3001 and ends the spell, and the slot shows its slot art again.
    private async Task RunAreaMaintainBarSmokeAsync(GameSessionOwner session, string npc)
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-MAGIC-026 maintain-bar smoke FAIL " + why); throw new InvalidOperationException("ARC-MAGIC-026 maintain-bar smoke: " + why); } }
        var barForSelfTest = _gameBarForSelfTest;
        _gameBarForSelfTest = true; _gameBarSoundsForSelfTest.Clear();
        try
        {
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            async Task Button(Vector2 at, bool pressed)
            {
                GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                GetViewport().PushInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0,
                });
                await Frames(1);
            }
            Require(SpellCasterNow() is not null && _spellRecords is { } loaded && loaded.Length > 0, "a cast host and the spell data");
            var open = session.QueryMaintainBarOpenSlots();
            Require(open >= 1, $"the hero opens a slot (intelligence / 4 = {open})");
            Require(session.QueryMaintainBar().SameAs(MaintainBar.Empty), "the bar starts empty");

            var started = StartMaintainedSpellForSmoke(session, npc);
            int? run = started?.Run; SessionSpellRecord? spell = started?.Spell; string? at = started?.At;
            Require(run is not null && spell is not null, "a maintained spell took a slot");
            Require(session.QueryMaintainBar().SlotOf(run!.Value) == 0, "the first maintained spell takes slot 0");
            await Frames(3);
            var view = _maintainBarView;
            Require(view is not null && _gameBarPanel is { Visible: true }, "the bars and the maintain bar show");
            var icon = Art(HotkeyBarRules.SpellIcon(spell!.Data.Number)!.Value)?[0];
            Require(icon is not null && view!.SlotArtForSelfTest(0) == icon, "slot 0 shows the spell's icon");
            for (var i = 1; i < MaintainBarRules.Slots; i++)
            {
                Require(view!.PluggedForSelfTest(i) == (i >= open), $"slot {i} plugged exactly when closed (open {open})");
                Require(view.SlotArtForSelfTest(i) is { } art && art == _gameBarPanel!.ArtFrame(AreaMaintainBarLayout.SlotArt(i)), $"slot {i} shows its slot art");
            }

            // The hover describes the spell (as the hotkey bar does), from off the strip first.
            var away = new Vector2(40, 160);
            GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away });
            await Frames(1);
            _messageClockShiftForSelfTest += AreaMessageLog.QuietMilliseconds + 1;
            var described = _hotkeyDescriptionsForSelfTest;
            var centre = view!.SlotCentreForSelfTest(0);
            GetViewport().PushInput(new InputEventMouseMotion { Position = centre, GlobalPosition = centre });
            await Frames(2);
            Require(_hotkeyDescriptionsForSelfTest == described + 1, "hovering slot 0 described the spell");

            _gameBarSoundsForSelfTest.Clear();
            var stops = _maintainStopsForSelfTest;
            await Button(centre, true);
            await Button(centre, false);
            await Frames(2);
            Require(_gameBarSoundsForSelfTest.Select(s => s.Id).SequenceEqual([AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound]),
                "the slot played 3000 then 3001: " + string.Join(",", _gameBarSoundsForSelfTest.Select(s => s.Id)));
            Require(_maintainStopsForSelfTest == stops + 1 && session.QuerySpellRuns()[run.Value] is null, "the click ended the spell");
            Require(session.QueryMaintainBar().SameAs(MaintainBar.Empty), "the bar is empty again");
            Require(view.SlotArtForSelfTest(0) is { } empty && empty == _gameBarPanel!.ArtFrame(AreaMaintainBarLayout.SlotArt(0)), "slot 0 shows its slot art again");
            GD.Print($"ARC-MAGIC-026 native-smoke maintain-bar PASS spell={spell.Data.Number} target={(at == SessionSpellCaster.Hero ? "hero" : "npc")} "
                + $"open={open} run={run}");
        }
        finally
        {
            // Any run this stage left goes, so the later stages see no maintained spell.
            if (_spellCaster is { } host)
                foreach (var left in session.QueryMaintainBar().Slots.Where(s => s.State != MaintainSlotState.Empty))
                    host.Act(new(SessionSpellRunActionKind.ForceEnd, left.Run));
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
            _gameBarForSelfTest = barForSelfTest; _messageClockShiftForSelfTest = 0;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    // The first maintained college spell (from the data, no spell named: a maintenance period, magic, an icon, no damage at BEGIN) that the hero can start at itself or else at <paramref name="npc"/>, released
    // at once (no casting goal is played) and holding a slot of the maintain bar (its first maintenance tick took it); its run, record and target, or null. Shared by the maintain-bar and the saved-runs stages.
    private (int Run, SessionSpellRecord Spell, string At)? StartMaintainedSpellForSmoke(GameSessionOwner session, string npc)
    {
        var caster = _spellCaster!;
        bool Maintained(SessionSpellRecord r) => r.Run is { MaintainPeriod: > 0, Technology: false } && r.Data.Number < SpellRecord.CollegeSpells
            && r.Data.Enabled && HotkeyBarRules.SpellIcon(r.Data.Number) is not null
            && !r.Of(SpellRunAction.Begin).Any(c => c.Type == SpellComponentType.Damage);
        foreach (var candidate in _spellRecords!.Where(Maintained))
        {
            foreach (var target in new[] { SessionSpellCaster.Hero, npc })
            {
                session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Fatigue = 1000 });
                var attempt = caster.CastAt(SessionSpellCaster.Hero, candidate.Data.Number, target, SpellLocation.None);
                if (attempt is not { Outcome: SpellCastAttemptOutcome.Started, RunId: { } id }) continue;
                caster.Release(id);
                if (session.QueryMaintainBar().SlotOf(id) is not null) { FlushSpellLog(null); return (id, candidate, target); }
                // Not maintained this time (the target refused it, or it ended): any run left goes, and the next is tried.
                if (session.QuerySpellRuns()[id] is not null) caster.Act(new(SessionSpellRunActionKind.ForceEnd, id));
            }
        }
        FlushSpellLog(null);
        return null;
    }
}
