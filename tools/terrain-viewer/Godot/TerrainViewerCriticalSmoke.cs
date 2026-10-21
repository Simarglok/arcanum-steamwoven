using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private static readonly HashSet<CombatRollPurpose> TablePurposes =
    [
        CombatRollPurpose.CriticalStun, CombatRollPurpose.CriticalStunCheck, CombatRollPurpose.CriticalKnockout, CombatRollPurpose.CriticalKnockoutCheck,
        CombatRollPurpose.CriticalCrippleArm, CombatRollPurpose.CriticalCrippleArmCheck, CombatRollPurpose.CriticalCrippleLeg,
        CombatRollPurpose.CriticalCrippleLegCheck, CombatRollPurpose.CriticalDropHelmet, CombatRollPurpose.CriticalBlind,
        CombatRollPurpose.CriticalBlindCheck, CombatRollPurpose.CriticalScar, CombatRollPurpose.CriticalKnockdown,
        CombatRollPurpose.CriticalDamageWeapon, CombatRollPurpose.CriticalDropWeapon, CombatRollPurpose.CriticalDamageArmor,
    ];

    // Native observation (ARC-COMBAT-009) with --hero, the last stage of map mode: the hero's critical-hit fate (a granted point)
    // makes its next attack on the living NPC with the most hit points a critical hit, which the session resolves with the
    // original's critical-effect tables over the NPC's real data (body type, constitution, worn items as the viewer passes them);
    // the stage checks that the hit was critical and that the tables drew whenever their sections apply, and reports the effects and
    // the item steps. NONE without a hero or a living NPC with combat values.
    private Task RunAreaCriticalSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area)
        {
            GD.Print("ARC-COMBAT-009 native-smoke critical NONE (no hero)");
            return Task.CompletedTask;
        }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-009 critical smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_CRITICAL_SMOKE_FAILED");
        }
        var mapId = new SessionMapId(CurrentAreaMap);
        var candidates = session.QueryMapInventory(mapId)?.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (h.Entity, Combat: _combatHost.Build(CurrentAreaMap, h.Entity).Combat))
            .Where(c => c.Combat is not null && session.QueryLife(c.Combat.Target).View?.Status is { IsDead: false })
            .OrderByDescending(c => LifeRules.MaxHitPoints(c.Combat!.Facts, 0)).ThenBy(c => c.Entity, StringComparer.Ordinal).ToArray() ?? [];
        if (candidates.Length == 0) { GD.Print($"ARC-COMBAT-009 native-smoke critical NONE map={_readyMapName} (no living NPC with combat values)"); return Task.CompletedTask; }
        var (entity, combat) = candidates[0];
        Require(session.GrantHeroFatePoint(session.Version).Disposition == GameSessionDisposition.Changed, "a fate point granted");
        Require(session.ArmHeroFate(session.Version, FateKind.CriticalHit).Disposition == GameSessionDisposition.Changed, "the critical-hit fate armed");
        var row = _inventoryHost!.Maps.Single(m => m.RuntimeMap == CurrentAreaMap).Row(entity);
        var draws = session.QueryRandom().Draws;
        var targetWorn = _combatHost.WornItems(session, CurrentAreaMap, entity);
        var result = session.AttackCritter(session.Version, new(combat!)
        {
            HeroWorn = AreaCombatHost.HeroWorn(session, _areaKitDurability),
            TargetWorn = targetWorn,
            TargetCell = row.WorldX is { } x && row.WorldY is { } y ? new(checked((int)x), checked((int)y)) : null,
        });
        Require(result.Disposition == GameSessionDisposition.Changed && result.Resolution?.Outcome == AttackOutcome.CriticalHit,
            $"the fate made a critical hit ({result.Disposition} {result.Failure} {result.Resolution?.Outcome})");
        var defender = combat!.Combatant(LifeRules.Evaluate(combat.Loaded, combat.Facts), combat.Loaded, CombatAnomalyProfile.Steamwoven);
        var tableRolls = result.Resolution!.Rolls.Count(roll => TablePurposes.Contains(roll.Purpose));
        // A recipient draws at least the stun roll unless it is of body type 2 or undead, and an undead one of body type 0 the
        // knockdown roll; the item rolls need worn items.
        var undead = defender.Conditions.HasFlag(CombatantConditions.Undead);
        Require(tableRolls > 0 || defender.CriticalBody == OriginalCriticalEffects.ItemsOnlyBody
            || undead && defender.CriticalBody != OriginalCriticalEffects.KnockdownBody, "the critical-effect tables drew");
        if (result.Life is { } life && life.Events.HasFlag(LifeEvents.Stunned))
            EndStun(session, result.Recipient == DamageRecipient.Target ? combat.Target : SessionLifeTarget.Hero, entity);
        SyncAreaCombatBodies();
        SyncAreaInventory();
        GD.Print("ARC-COMBAT-009 native-smoke critical PASS " + System.Text.Json.JsonSerializer.Serialize(new
        {
            Map = _readyMapName, Entity = entity, defender.CriticalBody, defender.Constitution, defender.ConstitutionMaximum,
            Worn = (targetWorn is null ? defender.Worn : CombatantRules.WornItems(targetWorn.Keys)).ToString(), Location = result.Resolution.HitLocation.ToString(),
            Effects = result.Resolution.Damage?.HitEffects.ToString(), Multiplier = result.Resolution.Damage?.Multiplier.ToString(),
            TableRolls = tableRolls, Draws = session.QueryRandom().Draws - draws, Events = result.Life?.Events.ToString(),
            Criticals = result.Criticals.Select(step => $"{step.Kind}:{step.Outcome}").ToArray(), Notice = AreaCombatHost.Describe(result, null, null),
            Parity = "NOT_VERIFIED (tables read in the original executable; no original-runtime run)",
        }));
        return Task.CompletedTask;
    }
}
