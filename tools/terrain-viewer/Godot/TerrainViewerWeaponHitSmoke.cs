using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    internal const string WeaponHitCode = "ARC-RENDER-1990";

    // The prototype files of the two installed weapons whose weapon-list hit entry has art: the Tesla Rod and Bronwyck's Gun; 6050 names a weapon whose hit entry is empty.
    private const int TeslaRodPrototype = 6060, BronwycksGunPrototype = 6069, PlainWeaponPrototype = 6050;

    /// <summary>
    /// ARC-RENDER-1990 (FR-COMBAT-006; ledger ER5, TT14) native observation (stage "weapon-hit", only when named, with --hero, on a map window with the inventory active): the attack end (0x4B6930) of a hit on the hero asks the weapon's own hit
    /// effect (0x4377C0) first. The stage runs the cast host's attack end for the hero as the victim with the prototype of the Tesla Rod (6060) and of Bronwyck's Gun (6069): the weapon list's hit entry plays as an overlay run on the hero (the entry's own
    /// art, additive, both overlay parts) and the blood of the hit is not asked; with no weapon, and with a weapon whose hit entry is empty (6050), the blood of the hit is asked and no entry of the weapon list plays. NONE when the weapon list is
    /// not read. <c>ARC_WEAPON_HIT_CAPTURE=FOLDER</c> saves a readback of the view while the blood (before the change: what every weapon did), the Tesla Rod's and Bronwyck's effect shows, and (ARC-RENDER-2011) before and while the swing flash of a firearm shows on the hero (for the thread's composite; never committed).
    /// </summary>
    private async Task RunAreaWeaponHitStageAsync(TerrainLoadResult? mapMode = null)
    {
        for (var wait = 0; wait < 1800 && (ExtraArt() is null || !InventoryActive); wait++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _worldActor.Session is not AreaGridSession || !InventoryActive)
        { GD.Print($"{WeaponHitCode} native-smoke weapon-hit SKIP (needs --hero, an area session and the inventory)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"{WeaponHitCode} weapon-hit smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_WEAPON_HIT_SMOKE_FAILED");
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var scaled = false;
        var captureDir = System.Environment.GetEnvironmentVariable("ARC_WEAPON_HIT_CAPTURE");
        async Task Shot(string name)
        {
            if (string.IsNullOrEmpty(captureDir)) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            System.IO.Directory.CreateDirectory(captureDir);
            using var image = GetViewport().GetTexture().GetImage();
            image.SavePng(System.IO.Path.Combine(captureDir, name + ".png"));
        }
        try
        {
            _worldFocusSimulationForSelfTest = true; _worldFocused = true; SelectSimulatedPadForSelfTest();
            var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
                ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
            AreaSmokePlace(map, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (var wait = 0; wait < 600 && SpellCasterNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var caster = SpellCasterNow();
            Require(caster is not null, "the map's cast host is built (spell data " + _spellData?.Status + ")");
            if (_spellData?.WeaponEyeCandy is not { } table) { GD.Print($"{WeaponHitCode} native-smoke weapon-hit NONE (the weapon list is not read: {_spellData?.WeaponEyeCandyDetail})"); return; }
            // A placement restarts the smoke clock in tenths of a second; the frames here are twentieths.
            scaled = true; _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++) { TechSmokeFrame(); FlushSpellLog(null); }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Frames(5);
            Require(caster!.WeaponEyeCandyAdd is not null, "the viewer wired the weapon eye-candy add into the cast host");
            var hit = new AttackDamage(DamageRecipient.Target, new(12, 0, 0, 0, 0), new(12, 0, 0, 0, 0), 12, 0, 0, DamageMultiplier.None, false, CriticalHitEffects.None, CriticalMissEffects.None, false, DamagePrevention.None);
            int Entry(int prototype) => EyeCandyEntryRef.Make(EyeCandyList.Weapon, WeaponHitEffectRules.Index(prototype, WeaponHitEffectRules.Hit)!.Value)!.Value;
            bool WeaponShows(int prototype) => ShownEyeCandyHost?.Shown().Any(o => o.Owner == SessionSpellCaster.Hero && o.Eye.Entry == Entry(prototype)) == true;
            bool AnyWeaponShows() => ShownEyeCandyHost?.Shown().Any(o => o.Owner == SessionSpellCaster.Hero && EyeCandyEntryRef.ListOf(o.Eye.Entry) == EyeCandyList.Weapon) == true;
            bool BloodShows() => ShownEyeCandyHost?.Shown().Any(o => o.Owner == SessionSpellCaster.Hero && EyeCandyEntryRef.ListOf(o.Eye.Entry) == EyeCandyList.Anim) == true;
            int GoreFrom() => caster.Log.Count;
            async Task Clear() { _eyeCandyHost?.LoseAll(); await Frames(2); Require(!AnyWeaponShows() && !BloodShows(), "the overlay host is empty before a hit"); }

            // No weapon: the attack end's blood of the hit, no weapon effect.
            await Clear();
            await Shot("before");
            var from = GoreFrom();
            caster.AttackEnd(SessionSpellCaster.Hero, hit, false, null);
            Require(caster.Log.Skip(from).Any(e => e.Kind == SpellCastEventKind.HitGore), "a hit with no weapon asks the blood of the hit");
            for (var i = 0; i < 40 && !BloodShows(); i += 2) await Frames(2);
            Require(BloodShows() && !AnyWeaponShows(), "the blood plays and no weapon effect does");
            await Shot("blood");

            // A weapon whose hit entry is empty: as no weapon.
            await Clear();
            Require(table.At(WeaponHitEffectRules.Index(PlainWeaponPrototype, WeaponHitEffectRules.Hit)!.Value).Art is null, "the plain weapon's hit entry is empty");
            from = GoreFrom();
            caster.AttackEnd(SessionSpellCaster.Hero, hit, false, PlainWeaponPrototype);
            Require(caster.Log.Skip(from).Any(e => e.Kind == SpellCastEventKind.HitGore), "a weapon with no hit entry leaves the blood of the hit as it was");
            await Frames(2);
            Require(!AnyWeaponShows(), "and plays no weapon effect");

            // The two weapons with a hit entry: their effect plays on the hero and replaces the blood.
            foreach (var (name, prototype) in new[] { ("tesla-rod", TeslaRodPrototype), ("bronwycks-gun", BronwycksGunPrototype) })
            {
                await Clear();
                var entry = table.At(EyeCandyEntryRef.IndexOf(Entry(prototype)));
                Require(entry.Art is not null && (entry.Flags & SpellEyeCandyFlags.Animates) != 0, $"{name}: the hit entry has art and animates: {entry}");
                from = GoreFrom();
                var ended = caster.AttackEnd(SessionSpellCaster.Hero, hit, false, prototype);
                Require(!ended && !caster.Log.Skip(from).Any(e => e.Kind == SpellCastEventKind.HitGore), $"{name}: the blood of the hit is not asked");
                for (var i = 0; i < 40 && !WeaponShows(prototype); i += 2) await Frames(2);
                var shown = ShownEyeCandyHost?.Shown().FirstOrDefault(o => o.Owner == SessionSpellCaster.Hero && o.Eye.Entry == Entry(prototype));
                Require(shown is not null, $"{name}: the weapon list's hit entry shows on the hero");
                Require(shown!.Eye.Art == entry.Art, $"{name}: the run holds the list entry's art");
                Require(!BloodShows(), $"{name}: no blood plays");
                var first = shown.Eye.Frame;
                for (var i = 0; i < 100 && (ShownEyeCandyHost?.Shown().FirstOrDefault(o => o.Handle.Equals(shown.Handle))?.Eye.Frame ?? first) == first; i += 2) await Frames(2);
                Require(_eyeCandyDrawnForSelfTest.Contains(Entry(prototype)), $"{name}: the draw placed the entry's parts");
                await Shot(name);
                GD.Print($"{WeaponHitCode} weapon-hit {name} prototype={prototype} entry={Entry(prototype)} art=0x{entry.Art!.Value:X8} frame={first}");
            }
            // ARC-RENDER-2011: the weapon's swing (kind 1) and begin (kind 0) effects go on the attacker (the hero here), turned to its facing, hit or miss, and play the entry's sound.
            var swingGroup = Enumerable.Range(0, table.Entries.Count / WeaponHitEffectRules.PerWeapon).FirstOrDefault(g => table.At(g * WeaponHitEffectRules.PerWeapon + WeaponHitEffectRules.Swing) is { Art: not null } e && (e.Flags & SpellEyeCandyFlags.Animates) != 0, -1);
            if (swingGroup < 0) { GD.Print($"{WeaponHitCode} weapon-swing NONE (no weapon with an animating swing entry)"); }
            else
            {
                var swingPrototype = WeaponHitEffectRules.FirstPrototype + WeaponHitEffectRules.PrototypeFileOffset + swingGroup;
                var swingEntry = EyeCandyEntryRef.Make(EyeCandyList.Weapon, swingGroup * WeaponHitEffectRules.PerWeapon + WeaponHitEffectRules.Swing)!.Value;
                var swingShape = table.At(EyeCandyEntryRef.IndexOf(swingEntry));
                bool SwingShows() => ShownEyeCandyHost?.Shown().Any(o => o.Owner == SessionSpellCaster.Hero && o.Eye.Entry == swingEntry) == true;
                await Clear();
                Require(table.At(WeaponHitEffectRules.Index(PlainWeaponPrototype, WeaponHitEffectRules.Swing)!.Value).Art is null, "the plain weapon's swing entry is empty");
                Require(!caster.WeaponAttackEffect(SessionSpellCaster.Hero, WeaponHitEffectRules.Swing, PlainWeaponPrototype, 3), "a weapon with an empty swing entry plays none");
                Require(!caster.WeaponAttackEffect(SessionSpellCaster.Hero, WeaponHitEffectRules.Swing, null, 3), "unarmed plays none");
                await Frames(2);
                Require(!AnyWeaponShows(), "and nothing of the weapon list shows");
                await Shot("swing-before");
                var soundsBefore = _weaponEyeCandySoundsForSelfTest.Count;
                var octant = 5;
                Require(caster.WeaponAttackEffect(SessionSpellCaster.Hero, WeaponHitEffectRules.Swing, swingPrototype, octant), $"the swing entry {swingEntry} of prototype {swingPrototype} plays on the hero: {swingShape}");
                for (var i = 0; i < 40 && !SwingShows(); i += 2) await Frames(2);
                var swing = ShownEyeCandyHost?.Shown().FirstOrDefault(o => o.Owner == SessionSpellCaster.Hero && o.Eye.Entry == swingEntry);
                Require(swing is not null, "the swing entry shows on the attacker");
                Require(EyeCandyArt.Rotation(swing!.Eye.Art) == octant && EyeCandyArt.Number(swing.Eye.Art) == EyeCandyArt.Number(swingShape.Art!.Value), $"its art is the entry's turned to the facing {octant}: 0x{swing.Eye.Art:X8}");
                Require(swingShape.Sound < 0 || _weaponEyeCandySoundsForSelfTest.Count == soundsBefore + 1, "the entry's sound was played with the add");
                Require(!BloodShows(), "a swing asks no blood");
                for (var i = 0; i < 100 && !_eyeCandyDrawnForSelfTest.Contains(swingEntry); i += 2) await Frames(2);
                Require(_eyeCandyDrawnForSelfTest.Contains(swingEntry), "the draw placed the swing entry's parts");
                for (var k = 0; k < 6; k++) { await Shot($"swing-{k}"); await Frames(1); }
                GD.Print($"{WeaponHitCode} weapon-swing prototype={swingPrototype} entry={swingEntry} art=0x{swing.Eye.Art:X8} octant={octant} sound={swingShape.Sound}");
            }
            GD.Print("ARC-RENDER-1990 native-smoke weapon-hit PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Tesla = Entry(TeslaRodPrototype), Bronwyck = Entry(BronwycksGunPrototype), Swing = swingGroup }));
        }
        finally
        {
            HeroRunsHost()?.LoseAll("SMOKE_WEAPON_HIT_END");
            _eyeCandyHost?.LoseAll();
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
