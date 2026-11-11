using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const int ShieldOfProtectionSpell = 30;

    /// <summary>
    /// ARC-MAGIC-045 (E4-a2) native observation (stage "body-styles", only when named, with --hero, on a map window with the inventory active): the object flags a spell writes change how the first living NPC the
    /// AI host holds is drawn (<see cref="AreaBodyStyleRules"/>). Translucent (0x40) draws its body at constant alpha 128, shrunk (0x80) at half size about its hotspot, don't-draw (0x100) not at all (nor picked),
    /// invisible (0x200) not at all unless the hero detects invisible, then at alpha 128; clearing the flag draws it as before. Shield of Protection (spell 30) cast at it and a hit that reaches it plays the
    /// run spell's hit eye candy (entry 6 × 30 + 5, 0x459A30) through the eye-candy host. The flags are written through the session as a run's ObjFlag component does (the spell path is the cast stage's). The
    /// map's own state is left as it was. ARC_AREA_BODY_STYLES_CAPTURE=PREFIX saves the window plain, translucent, shrunk and hidden as PREFIX-plain.png, -translucent.png, -shrunk.png and -hidden.png.
    /// </summary>
    private async Task RunAreaBodyStylesStageAsync()
    {
        for (var wait = 0; wait < 1800 && (ExtraArt() is null || !InventoryActive); wait++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || !InventoryActive)
        { GD.Print("ARC-MAGIC-045 native-smoke body-styles SKIP (needs --hero, an area session and the inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-MAGIC-045 body-styles smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_BODY_STYLES_SMOKE_FAILED");
        }
        if (HeroPoolPlace() is null) { GD.Print("ARC-MAGIC-045 native-smoke body-styles SKIP (the hero has no committed cell)"); return; }

        var aiBefore = _aiForSmoke;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        _aiForSmoke = true;
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_BODY_STYLES_CAPTURE");
        async Task Settle(int frames = 3)
        {
            for (var i = 0; i < frames; i++)
            {
                TickAreaClock(_talkSmokeHostMs += 100);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                SyncAreaInventory(); FlushSpellLog(null); SyncAreaBodyStyles();
            }
        }
        async Task Shot(string name)
        {
            await Settle(3);
            if (capture is not { Length: > 0 }) return;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image is null || image.SavePng($"{capture}-{name}.png") != Error.Ok) throw new InvalidOperationException("AREA_BODY_STYLES_CAPTURE_FAILED " + name);
        }
        string? entity = null;
        var runs = new List<int>();
        var written = new List<(string Entity, SessionObjectFlagField Field, int Mask)>();
        void Write(string who, SessionObjectFlagField field, int mask, bool on)
        {
            var result = session.WriteObjectFlag(session.Version, who, field, mask, on);
            Require(result.Disposition is GameSessionDisposition.Changed or GameSessionDisposition.NoChange, $"the session wrote {field} 0x{mask:X} {(on ? "on" : "off")} on {who}: {result.Failure}");
            if (on) written.Add((who, field, mask));
        }
        try
        {
            for (var wait = 0; wait < 600 && (AreaAiHostNow() is null || SpellCasterNow() is null); wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(AreaAiHostNow() is { } && SpellCasterNow() is not null, "the AI host and the cast host are built");
            var host = AreaAiHostNow()!;
            var candidates = host.Scene.Npcs.Where(n => n.Row.WorldX is not null && !session.QueryNpcDead(n.Entity)).Take(16).ToArray();
            AreaAiNpc? npc = null;
            AreaDrawCommand? plain = null;
            if (_worldActor.Session is AreaGridSession grid)
            {
                foreach (var candidate in candidates)
                {
                    var npcCell = LiveWorldCell(candidate.Row);
                    var place = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
                    try { AreaSmokePlace(place, AreaSmokeStandable(place, new(checked((int)(npcCell.X - grid.Chart.OriginX)), checked((int)(npcCell.Y - grid.Chart.OriginY))), 2)); }
                    catch (InvalidOperationException) { continue; }
                    PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
                    await Settle(30);
                    if (session.QueryNpcDead(candidate.Entity)) continue;
                    var shown = _canvas!.AreaVisibleCommands.FirstOrDefault(c => c.Identity == candidate.Row.Key);
                    if (shown is null || shown.Hot is null || shown.Blended) continue;
                    npc = candidate; plain = shown; break;
                }
            }
            if (npc is null || plain is null) { GD.Print($"ARC-MAGIC-045 native-smoke body-styles NONE (none of {candidates.Length} hosted NPCs is drawn plain with a hotspot beside the hero)"); return; }
            entity = npc.Entity;
            var key = npc.Row.Key;
            AreaDrawCommand? Drawn() => _canvas!.AreaVisibleCommands.FirstOrDefault(c => c.Identity == key);
            // The NPC idles, so its frame (and rectangle) changes between looks: sizes are compared as the drawn width over the frame's own width.
            static double Ratio(AreaDrawCommand c) => (double)c.Destination.Width / Math.Max(1, c.Source.Width);
            var plainRatio = Ratio(plain);
            GD.Print($"ARC-MAGIC-045 body-styles target={entity} key={key} size={plain.Destination.Width}x{plain.Destination.Height} hot={plain.Hot}");
            await Shot("plain");

            // Translucent: constant alpha, same rectangle.
            Write(entity, SessionObjectFlagField.Object, AreaBodyStyleRules.Translucent, true);
            await Settle(3);
            var translucent = Drawn();
            Require(translucent is { Alpha: AreaBodyStyleRules.TranslucentAlpha } && Math.Abs(Ratio(translucent) - plainRatio) < 0.05, $"translucent draws alpha 128 at the same scale ({translucent?.Alpha} {translucent?.Destination})");
            await Shot("translucent");
            Write(entity, SessionObjectFlagField.Object, AreaBodyStyleRules.Translucent, false);
            await Settle(3);
            Require(Drawn() is { Alpha: 255 } back && Math.Abs(Ratio(back) - plainRatio) < 0.05, "clearing translucent draws the body plain again");

            // Shrunk: half the frame about the hotspot.
            Write(entity, SessionObjectFlagField.Object, AreaBodyStyleRules.Shrunk, true);
            await Settle(3);
            var shrunk = Drawn();
            Require(shrunk is not null && Math.Abs(Ratio(shrunk) - plainRatio / 2) < 0.05, $"shrunk draws half the frame ({shrunk?.Destination} of {shrunk?.Source})");
            await Shot("shrunk");
            Write(entity, SessionObjectFlagField.Object, AreaBodyStyleRules.Shrunk, false);
            await Settle(3);
            Require(Drawn() is { } full && Math.Abs(Ratio(full) - plainRatio) < 0.05, "clearing shrunk draws the body full size again");

            // Don't-draw: no body (the eye candy and overlays stay), and the pick finds nothing there.
            Write(entity, SessionObjectFlagField.Object, AreaBodyStyleRules.DontDraw, true);
            await Settle(3);
            Require(Drawn() is null, "don't-draw draws no body");
            await Shot("hidden");
            Write(entity, SessionObjectFlagField.Object, AreaBodyStyleRules.DontDraw, false);
            await Settle(3);
            Require(Drawn() is not null, "clearing don't-draw draws the body again");

            // Invisible: not drawn unless the hero detects invisible, then translucent.
            Write(entity, SessionObjectFlagField.Object, AreaBodyStyleRules.Invisible, true);
            await Settle(3);
            Require(Drawn() is null, "an invisible body is not drawn to a hero that cannot see invisible");
            Write(SessionSpellCaster.Hero, SessionObjectFlagField.Spell, AreaBodyStyleRules.DetectsInvisibleSpell, true);
            await Settle(3);
            Require(Drawn() is { Alpha: AreaBodyStyleRules.TranslucentAlpha }, "an invisible body is drawn translucent to a hero that detects invisible");
            Write(SessionSpellCaster.Hero, SessionObjectFlagField.Spell, AreaBodyStyleRules.DetectsInvisibleSpell, false);
            Write(entity, SessionObjectFlagField.Object, AreaBodyStyleRules.Invisible, false);
            await Settle(3);
            Require(Drawn() is { Alpha: 255 }, "the body is plain again once invisible and the detection are gone");

            // The hit eye candy (0x459A30, SFR3): Shield of Protection on the critter, then a hit that reaches it.
            var caster = SpellCasterNow()!;
            var attempt = caster.CastByScript(null, ShieldOfProtectionSpell, entity, free: true, unresistable: true);
            Require(attempt.Outcome == SpellCastAttemptOutcome.Started && attempt.RunId is not null, $"the script cast of spell {ShieldOfProtectionSpell} started: {attempt.Outcome}");
            runs.Add(attempt.RunId!.Value);
            bool Shielded() => (session.QueryObjectFlags(entity!).Spell(0) & SessionSpellCaster.ShieldedSpellFlag) != 0;
            for (var i = 0; i < 120 && !Shielded(); i++) await Settle(1);
            Require(Shielded(), "the BEGIN of Shield of Protection set the shielded spell flag on the critter");
            var from = caster.Log.Count;
            caster.CritterHit(entity);
            FlushSpellLog(null);
            await Settle(2);
            var eye = caster.Log.Skip(from).Where(e => e.Kind == SpellCastEventKind.EyeCandyAdded && e.Object == entity).ToArray();
            Require(eye.Length == 1 && eye[0].Value == SpellEyeCandyRules.EntryIndex(ShieldOfProtectionSpell, SpellEyeCandyRules.Hit) && eye[0].Run == attempt.RunId && eye[0].Detail == 0,
                $"a hit on the shielded critter logs the spell's hit eye candy: {string.Join(",", eye.Select(e => $"{e.Value}@{e.Run}"))}");
            GD.Print($"ARC-MAGIC-045 body-styles hit-eye-candy entry={eye[0].Value} run={eye[0].Run} overlays={ShownEyeCandyHost?.Shown().Count() ?? 0}");
            GD.Print("ARC-MAGIC-045 native-smoke body-styles PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = area.Snapshot.RuntimeMap, Width = plain.Destination.Width, Height = plain.Destination.Height }));
        }
        finally
        {
            foreach (var run in runs) if (session.QuerySpellRuns()[run] is not null) SpellCasterNow()?.Act(new(SessionSpellRunActionKind.ForceEnd, run));
            foreach (var (who, field, mask) in written) session.WriteObjectFlag(session.Version, who, field, mask, false);
            FlushSpellLog(null);
            SyncAreaInventory();
            _aiForSmoke = aiBefore;
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            if (!aiBefore) { RetireAiHost(); _aiHostStamp = null; }
        }
    }
}
