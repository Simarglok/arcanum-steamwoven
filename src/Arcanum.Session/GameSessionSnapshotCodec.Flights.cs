using System.Text.Json;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

public static partial class GameSessionSnapshotCodec
{
    // ARC-DEV-288 (schema 80): the projectiles in flight with the shots they carry.
    private static readonly string[] FlightsFields = ["lastProjectile", "projectiles", "nextShot", "heroShots", "npcThrows", "npcArrows", "lastMissile", "missiles"];
    // ARC-DEV-289 (schema 82): the spell missiles in flight.
    private static readonly string[] MissileFields = ["entity", "map", "cell", "offsetX", "offsetY", "flight"];
    private static readonly string[] MissileFlightFields =
        ["caster", "run", "spell", "target", "art", "stepsPerTick", "paceMs", "lineStart", "lineEnd", "next", "x", "y", "lineSteps", "steps", "frames", "legs"];
    private static readonly string[] ProjectileFields = ["entity", "map", "cell", "offsetX", "offsetY", "flight"];
    private static readonly string[] ProjectileFlightFields =
    [
        "shot", "target", "critter", "object", "shooter", "thrower", "destination", "art", "lineStart", "lineEnd", "next", "x", "y", "steps", "hit", "freeFlight", "range",
        "returning", "targetless", "returns", "sizeArc", "scale", "stepsPerTick", "frames", "ticks",
    ];
    private static readonly string[] HeroShotFields =
        ["shot", "weapon", "skill", "skillLevel", "training", "reach", "check", "hit", "entity", "object", "thrown", "weaponItem", "struck", "boomerang", "returning"];
    private static readonly string[] NpcThrowFields = ["shot", "thrower", "thrown"];
    private static readonly string[] NpcArrowFields = ["shot", "shooter", "values", "check", "hit", "target"];
    private static readonly string[] ThrownShotFields = ["item", "range"];
    private static readonly string[] CarriedCheckFields = ["refusal", "outcome", "missCause", "hitLocation", "aimed", "weaponMagicCancelled", "fate", "criticalSelfHit"];
    private static readonly string[] NpcAttackValuesFields = ["combatant", "weapon", "skill", "skillLevel", "training", "weaponSpeed", "attackCost", "speed", "range", "supported"];
    // Every property of Combatant and CombatWeapon (a test holds the lists to the records' properties, so a new property is not left out of a save).
    internal static readonly string[] CombatantFields =
    [
        "isPc", "level", "strength", "perception", "extraordinaryIntelligence", "damageBonus", "magickTechAptitude", "armorClass", "resistances", "conditions", "lightEmission",
        "lightLevel", "dodgeLevel", "dodgeTraining", "backstabLevel", "backstabTraining", "criticalHitChanceAdjustment", "criticalMissChanceAdjustment", "criticalEffectChanceAdjustment",
        "criticalMissEffectChanceAdjustment", "unarmedBodySpell", "criticalHitFateArmed", "criticalMissFateArmed", "material", "criticalBody", "constitution", "constitutionMaximum",
        "worn", "scarred",
    ];
    internal static readonly string[] CombatWeaponFields =
    [
        "kind", "damage", "range", "toHitBonus", "minimumStrength", "magickTechComplexity", "magicToHitAdjustment", "magicCriticalHitChance", "magicCriticalMissChance",
        "magicCriticalEffectChance", "magicCriticalMissEffectChance", "criticalHitChart", "criticalMissChart", "magicDamageAdjustment", "hp", "maxHp", "ignoresResistance",
        "damagesArmor", "uber", "artSubtype", "usesAmmunition", "ammoType", "ammoConsumption",
    ];
    private static readonly int ConditionMask = Enum.GetValues<CombatantConditions>().Aggregate(0, (mask, flag) => mask | (int)flag);
    private static readonly int WornMask = Enum.GetValues<CombatWornItems>().Aggregate(0, (mask, flag) => mask | (int)flag);

    private static void WriteFlights(Utf8JsonWriter w, SessionFlightState flights)
    {
        w.WriteStartObject("flights");
        w.WriteNumber("lastProjectile", flights.LastProjectile);
        w.WriteStartArray("projectiles");
        foreach (var projectile in flights.Projectiles.OrderBy(p => SessionProjectile.Number(p.Entity)))
        {
            w.WriteStartObject();
            w.WriteString("entity", projectile.Entity);
            w.WriteNumber("map", projectile.Map.RuntimeMap);
            WriteGoalCell(w, "cell", projectile.Cell);
            w.WriteNumber("offsetX", projectile.OffsetX);
            w.WriteNumber("offsetY", projectile.OffsetY);
            if (projectile.Flight is { } f)
            {
                w.WriteStartObject("flight");
                w.WriteNumber("shot", f.Shot.Id);
                WriteNullableString(w, "target", f.Launch.Target);
                w.WriteBoolean("critter", f.Launch.Critter);
                w.WriteBoolean("object", f.Launch.Object);
                WriteNullableString(w, "shooter", f.Launch.Shooter);
                WriteNullableString(w, "thrower", f.Launch.Thrower);
                WriteGoalCell(w, "destination", f.Destination);
                if (f.Art is { } art) w.WriteNumber("art", art); else w.WriteNull("art");
                WriteGoalCell(w, "lineStart", f.LineStart);
                WriteGoalCell(w, "lineEnd", f.LineEnd);
                w.WriteNumber("next", f.Next);
                w.WriteNumber("x", f.X);
                w.WriteNumber("y", f.Y);
                w.WriteNumber("steps", f.Steps);
                w.WriteBoolean("hit", f.State.Hit);
                w.WriteBoolean("freeFlight", f.State.FreeFlight);
                w.WriteNumber("range", f.State.Range);
                w.WriteBoolean("returning", f.State.Returning);
                w.WriteBoolean("targetless", f.Targetless);
                w.WriteBoolean("returns", f.Returns);
                w.WriteBoolean("sizeArc", f.SizeArc);
                w.WriteNumber("scale", f.Scale);
                w.WriteNumber("stepsPerTick", f.StepsPerTick);
                w.WriteNumber("frames", f.Frames);
                w.WriteNumber("ticks", f.Ticks);
                w.WriteEndObject();
            }
            else w.WriteNull("flight");
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteNumber("nextShot", flights.NextShot);
        w.WriteStartArray("heroShots");
        foreach (var shot in flights.HeroShots.OrderBy(s => s.Shot.Id))
        {
            w.WriteStartObject();
            w.WriteNumber("shot", shot.Shot.Id);
            WriteCombatWeapon(w, "weapon", shot.Weapon);
            w.WriteString("skill", shot.Skill.ToString());
            w.WriteNumber("skillLevel", shot.SkillLevel);
            w.WriteString("training", shot.Training.ToString());
            w.WriteNumber("reach", shot.Reach);
            WriteCarriedCheck(w, shot.Check);
            w.WriteBoolean("hit", shot.Hit);
            WriteNullableString(w, "entity", shot.Entity);
            WriteNullableString(w, "object", shot.Object);
            WriteThrownShot(w, shot.Thrown);
            WriteNullableString(w, "weaponItem", shot.WeaponItem);
            w.WriteBoolean("struck", shot.Struck);
            w.WriteBoolean("boomerang", shot.Boomerang);
            w.WriteBoolean("returning", shot.Returning);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("npcThrows");
        foreach (var shot in flights.NpcThrows.OrderBy(s => s.Shot.Id))
        {
            w.WriteStartObject();
            w.WriteNumber("shot", shot.Shot.Id);
            w.WriteString("thrower", shot.Thrower);
            WriteThrownShot(w, shot.Thrown);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("npcArrows");
        foreach (var shot in flights.NpcArrows.OrderBy(s => s.Shot.Id))
        {
            w.WriteStartObject();
            w.WriteNumber("shot", shot.Shot.Id);
            w.WriteString("shooter", shot.Shooter);
            var v = shot.Values;
            w.WriteStartObject("values");
            WriteCombatant(w, "combatant", v.Combatant);
            WriteCombatWeapon(w, "weapon", v.Weapon);
            w.WriteString("skill", v.Skill.ToString());
            w.WriteNumber("skillLevel", v.SkillLevel);
            w.WriteString("training", v.Training.ToString());
            w.WriteNumber("weaponSpeed", v.WeaponSpeed);
            w.WriteNumber("attackCost", v.AttackCost);
            w.WriteNumber("speed", v.Speed);
            w.WriteNumber("range", v.Range);
            w.WriteBoolean("supported", v.Supported);
            w.WriteEndObject();
            WriteCarriedCheck(w, shot.Check);
            w.WriteBoolean("hit", shot.Hit);
            w.WriteString("target", shot.Target);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteNumber("lastMissile", flights.LastMissile);
        w.WriteStartArray("missiles");
        foreach (var missile in flights.Missiles.OrderBy(m => SessionMissile.Number(m.Entity)))
        {
            w.WriteStartObject();
            w.WriteString("entity", missile.Entity);
            w.WriteNumber("map", missile.Map.RuntimeMap);
            WriteGoalCell(w, "cell", missile.Cell);
            w.WriteNumber("offsetX", missile.OffsetX);
            w.WriteNumber("offsetY", missile.OffsetY);
            if (missile.Flight is { } f)
            {
                w.WriteStartObject("flight");
                w.WriteString("caster", f.Caster);
                w.WriteNumber("run", f.Run);
                w.WriteNumber("spell", f.Spell);
                WriteNullableString(w, "target", f.Target);
                w.WriteNumber("art", f.Art);
                w.WriteNumber("stepsPerTick", f.StepsPerTick);
                w.WriteNumber("paceMs", f.PaceMs);
                WriteGoalCell(w, "lineStart", f.LineStart);
                WriteGoalCell(w, "lineEnd", f.LineEnd);
                w.WriteNumber("next", f.Next);
                w.WriteNumber("x", f.X);
                w.WriteNumber("y", f.Y);
                w.WriteNumber("lineSteps", f.LineSteps);
                w.WriteNumber("steps", f.Steps);
                w.WriteNumber("frames", f.Frames);
                w.WriteNumber("legs", f.Legs);
                w.WriteEndObject();
            }
            else w.WriteNull("flight");
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void WriteThrownShot(Utf8JsonWriter w, SessionThrownShot? thrown)
    {
        if (thrown is not { } t) { w.WriteNull("thrown"); return; }
        w.WriteStartObject("thrown");
        w.WriteString("item", t.Item);
        w.WriteNumber("range", t.Range);
        w.WriteEndObject();
    }

    private static void WriteCarriedCheck(Utf8JsonWriter w, SessionCarriedCheck check)
    {
        w.WriteStartObject("check");
        w.WriteString("refusal", check.Refusal.ToString());
        w.WriteString("outcome", check.Outcome.ToString());
        w.WriteString("missCause", check.MissCause.ToString());
        w.WriteString("hitLocation", check.HitLocation.ToString());
        w.WriteBoolean("aimed", check.Aimed);
        w.WriteBoolean("weaponMagicCancelled", check.WeaponMagicCancelled);
        w.WriteString("fate", check.Fate.ToString());
        w.WriteBoolean("criticalSelfHit", check.CriticalSelfHit);
        w.WriteEndObject();
    }

    private static void WriteCombatant(Utf8JsonWriter w, string name, Combatant c)
    {
        w.WriteStartObject(name);
        w.WriteBoolean("isPc", c.IsPc);
        w.WriteNumber("level", c.Level);
        w.WriteNumber("strength", c.Strength);
        w.WriteNumber("perception", c.Perception);
        w.WriteBoolean("extraordinaryIntelligence", c.ExtraordinaryIntelligence);
        w.WriteNumber("damageBonus", c.DamageBonus);
        w.WriteNumber("magickTechAptitude", c.MagickTechAptitude);
        w.WriteNumber("armorClass", c.ArmorClass);
        w.WriteStartArray("resistances");
        w.WriteNumberValue(c.Resistances.Normal);
        w.WriteNumberValue(c.Resistances.Poison);
        w.WriteNumberValue(c.Resistances.Electrical);
        w.WriteNumberValue(c.Resistances.Fire);
        w.WriteEndArray();
        w.WriteNumber("conditions", (int)c.Conditions);
        w.WriteString("lightEmission", c.LightEmission.ToString());
        w.WriteNumber("lightLevel", c.LightLevel);
        w.WriteNumber("dodgeLevel", c.DodgeLevel);
        w.WriteString("dodgeTraining", c.DodgeTraining.ToString());
        w.WriteNumber("backstabLevel", c.BackstabLevel);
        w.WriteString("backstabTraining", c.BackstabTraining.ToString());
        w.WriteNumber("criticalHitChanceAdjustment", c.CriticalHitChanceAdjustment);
        w.WriteNumber("criticalMissChanceAdjustment", c.CriticalMissChanceAdjustment);
        w.WriteNumber("criticalEffectChanceAdjustment", c.CriticalEffectChanceAdjustment);
        w.WriteNumber("criticalMissEffectChanceAdjustment", c.CriticalMissEffectChanceAdjustment);
        w.WriteString("unarmedBodySpell", c.UnarmedBodySpell.ToString());
        w.WriteBoolean("criticalHitFateArmed", c.CriticalHitFateArmed);
        w.WriteBoolean("criticalMissFateArmed", c.CriticalMissFateArmed);
        w.WriteString("material", c.Material.ToString());
        w.WriteNumber("criticalBody", c.CriticalBody);
        w.WriteNumber("constitution", c.Constitution);
        w.WriteNumber("constitutionMaximum", c.ConstitutionMaximum);
        w.WriteNumber("worn", (int)c.Worn);
        w.WriteBoolean("scarred", c.Scarred);
        w.WriteEndObject();
    }

    private static void WriteCombatWeapon(Utf8JsonWriter w, string name, CombatWeapon c)
    {
        w.WriteStartObject(name);
        w.WriteString("kind", c.Kind.ToString());
        w.WriteStartArray("damage");
        foreach (var range in new[] { c.Damage.Normal, c.Damage.Poison, c.Damage.Electrical, c.Damage.Fire, c.Damage.Fatigue })
        {
            w.WriteStartArray();
            w.WriteNumberValue(range.Min);
            w.WriteNumberValue(range.Max);
            w.WriteEndArray();
        }
        w.WriteEndArray();
        w.WriteNumber("range", c.Range);
        w.WriteNumber("toHitBonus", c.ToHitBonus);
        w.WriteNumber("minimumStrength", c.MinimumStrength);
        w.WriteNumber("magickTechComplexity", c.MagickTechComplexity);
        w.WriteNumber("magicToHitAdjustment", c.MagicToHitAdjustment);
        w.WriteNumber("magicCriticalHitChance", c.MagicCriticalHitChance);
        w.WriteNumber("magicCriticalMissChance", c.MagicCriticalMissChance);
        w.WriteNumber("magicCriticalEffectChance", c.MagicCriticalEffectChance);
        w.WriteNumber("magicCriticalMissEffectChance", c.MagicCriticalMissEffectChance);
        w.WriteString("criticalHitChart", c.CriticalHitChart.ToString());
        w.WriteString("criticalMissChart", c.CriticalMissChart.ToString());
        w.WriteStartArray("magicDamageAdjustment");
        w.WriteNumberValue(c.MagicDamageAdjustment.Normal);
        w.WriteNumberValue(c.MagicDamageAdjustment.Poison);
        w.WriteNumberValue(c.MagicDamageAdjustment.Electrical);
        w.WriteNumberValue(c.MagicDamageAdjustment.Fire);
        w.WriteNumberValue(c.MagicDamageAdjustment.Fatigue);
        w.WriteEndArray();
        w.WriteNumber("hp", c.Hp);
        w.WriteNumber("maxHp", c.MaxHp);
        w.WriteBoolean("ignoresResistance", c.IgnoresResistance);
        w.WriteBoolean("damagesArmor", c.DamagesArmor);
        w.WriteBoolean("uber", c.Uber);
        w.WriteNumber("artSubtype", c.ArtSubtype);
        w.WriteBoolean("usesAmmunition", c.UsesAmmunition);
        WriteNullableInt(w, "ammoType", c.AmmoType);
        w.WriteNumber("ammoConsumption", c.AmmoConsumption);
        w.WriteEndObject();
    }

    // Shapes and ranges: the projectiles ascending by number, the shots ascending by id (the canonical order); which projectiles and shots belong together, the counters and the map are
    // Restore's.
    private static SessionFlightState Flights(JsonElement element)
    {
        const string p = "$.flights";
        var f = Fields(element, p, FlightsFields);
        var last = Int(f["lastProjectile"], p + ".lastProjectile", 0, int.MaxValue);
        var rows = Array(f["projectiles"], p + ".projectiles", SessionProjectile.MaxProjectiles);
        var projectiles = new SessionProjectile[rows.Length];
        var previous = 0;
        for (var i = 0; i < rows.Length; i++)
        {
            var at = $"{p}.projectiles[{i}]";
            var r = Fields(rows[i], at, ProjectileFields);
            var entity = Entity(r["entity"], at + ".entity");
            if (SessionProjectile.Number(entity) is not { } number) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, at + ".entity");
            if (number <= previous) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at + ".entity");
            previous = number;
            projectiles[i] = new SessionProjectile(entity, new(Int(r["map"], at + ".map", int.MinValue, int.MaxValue)), GoalCell(r["cell"], at + ".cell"),
                Int(r["offsetX"], at + ".offsetX", int.MinValue, int.MaxValue), Int(r["offsetY"], at + ".offsetY", int.MinValue, int.MaxValue))
            {
                Flight = r["flight"].ValueKind == JsonValueKind.Null ? null : ProjectileFlightOf(r["flight"], at + ".flight"),
            };
        }
        var nextShot = Long(f["nextShot"], p + ".nextShot", 0, long.MaxValue);
        var heroRows = Array(f["heroShots"], p + ".heroShots", GameSessionOwner.MaxShotsInFlight);
        var hero = new SessionSavedHeroShot[heroRows.Length];
        long previousShot = 0;
        for (var i = 0; i < heroRows.Length; i++)
        {
            var at = $"{p}.heroShots[{i}]";
            var r = Fields(heroRows[i], at, HeroShotFields);
            var shot = ShotId(r["shot"], at + ".shot", ref previousShot);
            hero[i] = new SessionSavedHeroShot(shot, CombatWeaponOf(r["weapon"], at + ".weapon"), Named<CombatSkill>(r["skill"], at + ".skill"),
                Int(r["skillLevel"], at + ".skillLevel", 0, int.MaxValue), Named<SkillTraining>(r["training"], at + ".training"), Int(r["reach"], at + ".reach", 0, int.MaxValue),
                CarriedCheckOf(r["check"], at + ".check"),
                Bool(r["hit"], at + ".hit"), NullableEntity(r["entity"], at + ".entity"), r["object"].ValueKind == JsonValueKind.Null ? null : String(r["object"], at + ".object"))
            {
                Thrown = ThrownShotOf(r["thrown"], at + ".thrown"), WeaponItem = NullableEntity(r["weaponItem"], at + ".weaponItem"), Struck = Bool(r["struck"], at + ".struck"),
                Boomerang = Bool(r["boomerang"], at + ".boomerang"), Returning = Bool(r["returning"], at + ".returning"),
            };
        }
        var throwRows = Array(f["npcThrows"], p + ".npcThrows", GameSessionOwner.MaxShotsInFlight);
        var throws = new SessionSavedNpcThrow[throwRows.Length];
        previousShot = 0;
        for (var i = 0; i < throwRows.Length; i++)
        {
            var at = $"{p}.npcThrows[{i}]";
            var r = Fields(throwRows[i], at, NpcThrowFields);
            var shot = ShotId(r["shot"], at + ".shot", ref previousShot);
            throws[i] = new SessionSavedNpcThrow(shot, Entity(r["thrower"], at + ".thrower"),
                ThrownShotOf(r["thrown"], at + ".thrown") ?? throw new DecodeException(SnapshotDecodeFailure.WrongType, at + ".thrown"));
        }
        var arrowRows = Array(f["npcArrows"], p + ".npcArrows", GameSessionOwner.MaxShotsInFlight);
        var arrows = new SessionSavedNpcArrow[arrowRows.Length];
        previousShot = 0;
        for (var i = 0; i < arrowRows.Length; i++)
        {
            var at = $"{p}.npcArrows[{i}]";
            var r = Fields(arrowRows[i], at, NpcArrowFields);
            var shot = ShotId(r["shot"], at + ".shot", ref previousShot);
            var vp = at + ".values";
            var v = Fields(r["values"], vp, NpcAttackValuesFields);
            var values = new SessionNpcAttackValues(CombatantOf(v["combatant"], vp + ".combatant"), CombatWeaponOf(v["weapon"], vp + ".weapon"),
                Named<CombatSkill>(v["skill"], vp + ".skill"), Int(v["skillLevel"], vp + ".skillLevel", 0, int.MaxValue), Named<SkillTraining>(v["training"], vp + ".training"),
                Int(v["weaponSpeed"], vp + ".weaponSpeed", int.MinValue, int.MaxValue), Int(v["attackCost"], vp + ".attackCost", int.MinValue, int.MaxValue),
                Int(v["speed"], vp + ".speed", int.MinValue, int.MaxValue), Int(v["range"], vp + ".range", int.MinValue, int.MaxValue), Bool(v["supported"], vp + ".supported"));
            var target = String(r["target"], at + ".target");
            arrows[i] = new SessionSavedNpcArrow(shot, Entity(r["shooter"], at + ".shooter"), values, CarriedCheckOf(r["check"], at + ".check"), Bool(r["hit"], at + ".hit"),
                target == SessionNpcShotFacts.HeroTarget ? target : Entity(r["target"], at + ".target"));
        }
        var lastMissile = Int(f["lastMissile"], p + ".lastMissile", 0, int.MaxValue);
        var missileRows = Array(f["missiles"], p + ".missiles", SessionMissile.MaxMissiles);
        var missiles = new SessionMissile[missileRows.Length];
        previous = 0;
        for (var i = 0; i < missileRows.Length; i++)
        {
            var at = $"{p}.missiles[{i}]";
            var r = Fields(missileRows[i], at, MissileFields);
            var entity = Entity(r["entity"], at + ".entity");
            if (SessionMissile.Number(entity) is not { } number) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, at + ".entity");
            if (number <= previous) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, at + ".entity");
            previous = number;
            missiles[i] = new SessionMissile(entity, new(Int(r["map"], at + ".map", int.MinValue, int.MaxValue)), GoalCell(r["cell"], at + ".cell"),
                Int(r["offsetX"], at + ".offsetX", int.MinValue, int.MaxValue), Int(r["offsetY"], at + ".offsetY", int.MinValue, int.MaxValue))
            {
                Flight = r["flight"].ValueKind == JsonValueKind.Null ? null : MissileFlightOf(r["flight"], at + ".flight"),
            };
        }
        return new(last, projectiles, nextShot, hero, throws, arrows) { LastMissile = lastMissile, Missiles = missiles };
    }

    private static SessionMissileFlight MissileFlightOf(JsonElement element, string path)
    {
        var f = Fields(element, path, MissileFlightFields);
        var caster = String(f["caster"], path + ".caster");
        return new SessionMissileFlight(caster == SessionSpellCaster.Hero ? caster : Entity(f["caster"], path + ".caster"), Int(f["run"], path + ".run", 0, int.MaxValue),
            Int(f["spell"], path + ".spell", 0, int.MaxValue), NullableEntity(f["target"], path + ".target"), UInt(f["art"], path + ".art"),
            Int(f["stepsPerTick"], path + ".stepsPerTick", int.MinValue, int.MaxValue), Int(f["paceMs"], path + ".paceMs", int.MinValue, int.MaxValue),
            GoalCell(f["lineStart"], path + ".lineStart"), GoalCell(f["lineEnd"], path + ".lineEnd"), Int(f["next"], path + ".next", 0, int.MaxValue),
            Long(f["x"], path + ".x", int.MinValue, int.MaxValue), Long(f["y"], path + ".y", int.MinValue, int.MaxValue), Long(f["lineSteps"], path + ".lineSteps", 0, long.MaxValue))
        {
            Steps = Long(f["steps"], path + ".steps", 0, long.MaxValue), Frames = Int(f["frames"], path + ".frames", int.MinValue, int.MaxValue),
            Legs = Int(f["legs"], path + ".legs", 0, int.MaxValue),
        };
    }

    private static SessionShot ShotId(JsonElement element, string path, ref long previous)
    {
        var id = Long(element, path, 1, long.MaxValue);
        if (id <= previous) throw new DecodeException(SnapshotDecodeFailure.NotCanonical, path);
        previous = id;
        return new(id);
    }

    private static SessionProjectileFlight ProjectileFlightOf(JsonElement element, string p)
    {
        var f = Fields(element, p, ProjectileFlightFields);
        var launch = new SessionProjectileLaunch(NullableEntity(f["target"], p + ".target"), Bool(f["critter"], p + ".critter"), Bool(f["object"], p + ".object"),
            NullableEntity(f["shooter"], p + ".shooter"), NullableEntity(f["thrower"], p + ".thrower"));
        return new SessionProjectileFlight(new(Long(f["shot"], p + ".shot", 1, long.MaxValue)), launch, GoalCell(f["destination"], p + ".destination"),
            f["art"].ValueKind == JsonValueKind.Null ? null : UInt(f["art"], p + ".art"), GoalCell(f["lineStart"], p + ".lineStart"), GoalCell(f["lineEnd"], p + ".lineEnd"),
            Int(f["next"], p + ".next", 0, int.MaxValue), Long(f["x"], p + ".x", int.MinValue, int.MaxValue), Long(f["y"], p + ".y", int.MinValue, int.MaxValue),
            Long(f["steps"], p + ".steps", 0, long.MaxValue),
            new ProjectileShot(Bool(f["hit"], p + ".hit"), Bool(f["freeFlight"], p + ".freeFlight"), Int(f["range"], p + ".range", 0, int.MaxValue), Bool(f["returning"], p + ".returning")))
        {
            Targetless = Bool(f["targetless"], p + ".targetless"),
            Returns = Bool(f["returns"], p + ".returns"),
            SizeArc = Bool(f["sizeArc"], p + ".sizeArc"),
            Scale = Int(f["scale"], p + ".scale", 0, 100),
            StepsPerTick = Int(f["stepsPerTick"], p + ".stepsPerTick", 1, 1_000),
            Frames = Int(f["frames"], p + ".frames", 0, int.MaxValue),
            Ticks = Long(f["ticks"], p + ".ticks", 0, long.MaxValue),
        };
    }

    private static SessionThrownShot? ThrownShotOf(JsonElement element, string p)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        var f = Fields(element, p, ThrownShotFields);
        return new SessionThrownShot(Entity(f["item"], p + ".item"), Int(f["range"], p + ".range", 0, int.MaxValue));
    }

    private static SessionCarriedCheck CarriedCheckOf(JsonElement element, string p)
    {
        var f = Fields(element, p, CarriedCheckFields);
        return new(Named<AttackRefusal>(f["refusal"], p + ".refusal"), Named<AttackOutcome>(f["outcome"], p + ".outcome"), Named<AttackMissCause>(f["missCause"], p + ".missCause"),
            Named<HitLocation>(f["hitLocation"], p + ".hitLocation"), Bool(f["aimed"], p + ".aimed"), Bool(f["weaponMagicCancelled"], p + ".weaponMagicCancelled"),
            Named<FateUse>(f["fate"], p + ".fate"), Bool(f["criticalSelfHit"], p + ".criticalSelfHit"));
    }

    private static Combatant CombatantOf(JsonElement element, string p)
    {
        var f = Fields(element, p, CombatantFields);
        int N(string name) => Int(f[name], p + "." + name, int.MinValue, int.MaxValue);
        var resistances = Array(f["resistances"], p + ".resistances", 4);
        if (resistances.Length != 4) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".resistances");
        int R(int i) => Int(resistances[i], $"{p}.resistances[{i}]", int.MinValue, int.MaxValue);
        return new Combatant
        {
            IsPc = Bool(f["isPc"], p + ".isPc"), Level = N("level"), Strength = N("strength"), Perception = N("perception"),
            ExtraordinaryIntelligence = Bool(f["extraordinaryIntelligence"], p + ".extraordinaryIntelligence"), DamageBonus = N("damageBonus"),
            MagickTechAptitude = N("magickTechAptitude"), ArmorClass = N("armorClass"), Resistances = new(R(0), R(1), R(2), R(3)),
            Conditions = (CombatantConditions)FlagBits(f["conditions"], p + ".conditions", ConditionMask), LightEmission = Named<LightEmission>(f["lightEmission"], p + ".lightEmission"),
            LightLevel = N("lightLevel"), DodgeLevel = N("dodgeLevel"), DodgeTraining = Named<SkillTraining>(f["dodgeTraining"], p + ".dodgeTraining"),
            BackstabLevel = N("backstabLevel"), BackstabTraining = Named<SkillTraining>(f["backstabTraining"], p + ".backstabTraining"),
            CriticalHitChanceAdjustment = N("criticalHitChanceAdjustment"), CriticalMissChanceAdjustment = N("criticalMissChanceAdjustment"),
            CriticalEffectChanceAdjustment = N("criticalEffectChanceAdjustment"), CriticalMissEffectChanceAdjustment = N("criticalMissEffectChanceAdjustment"),
            UnarmedBodySpell = Named<UnarmedBodySpell>(f["unarmedBodySpell"], p + ".unarmedBodySpell"),
            CriticalHitFateArmed = Bool(f["criticalHitFateArmed"], p + ".criticalHitFateArmed"), CriticalMissFateArmed = Bool(f["criticalMissFateArmed"], p + ".criticalMissFateArmed"),
            Material = Named<ObjectMaterial>(f["material"], p + ".material"), CriticalBody = N("criticalBody"), Constitution = N("constitution"),
            ConstitutionMaximum = N("constitutionMaximum"), Worn = (CombatWornItems)FlagBits(f["worn"], p + ".worn", WornMask), Scarred = Bool(f["scarred"], p + ".scarred"),
        };
    }

    private static CombatWeapon CombatWeaponOf(JsonElement element, string p)
    {
        var f = Fields(element, p, CombatWeaponFields);
        int N(string name) => Int(f[name], p + "." + name, int.MinValue, int.MaxValue);
        var damage = Array(f["damage"], p + ".damage", 5);
        if (damage.Length != 5) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".damage");
        var ranges = new DamageRange[5];
        for (var i = 0; i < 5; i++)
        {
            var pair = Array(damage[i], $"{p}.damage[{i}]", 2);
            if (pair.Length != 2) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, $"{p}.damage[{i}]");
            ranges[i] = new(Int(pair[0], $"{p}.damage[{i}][0]", int.MinValue, int.MaxValue), Int(pair[1], $"{p}.damage[{i}][1]", int.MinValue, int.MaxValue));
        }
        var magic = Array(f["magicDamageAdjustment"], p + ".magicDamageAdjustment", 5);
        if (magic.Length != 5) throw new DecodeException(SnapshotDecodeFailure.OutOfRange, p + ".magicDamageAdjustment");
        int M(int i) => Int(magic[i], $"{p}.magicDamageAdjustment[{i}]", int.MinValue, int.MaxValue);
        return new CombatWeapon
        {
            Kind = Named<WeaponKind>(f["kind"], p + ".kind"), Damage = new(ranges[0], ranges[1], ranges[2], ranges[3], ranges[4]), Range = N("range"),
            ToHitBonus = N("toHitBonus"), MinimumStrength = N("minimumStrength"), MagickTechComplexity = N("magickTechComplexity"),
            MagicToHitAdjustment = N("magicToHitAdjustment"), MagicCriticalHitChance = N("magicCriticalHitChance"), MagicCriticalMissChance = N("magicCriticalMissChance"),
            MagicCriticalEffectChance = N("magicCriticalEffectChance"), MagicCriticalMissEffectChance = N("magicCriticalMissEffectChance"),
            CriticalHitChart = Named<CriticalHitChart>(f["criticalHitChart"], p + ".criticalHitChart"),
            CriticalMissChart = Named<CriticalMissChart>(f["criticalMissChart"], p + ".criticalMissChart"),
            MagicDamageAdjustment = new(M(0), M(1), M(2), M(3), M(4)), Hp = N("hp"), MaxHp = N("maxHp"),
            IgnoresResistance = Bool(f["ignoresResistance"], p + ".ignoresResistance"), DamagesArmor = Bool(f["damagesArmor"], p + ".damagesArmor"),
            Uber = Bool(f["uber"], p + ".uber"), ArtSubtype = N("artSubtype"), UsesAmmunition = Bool(f["usesAmmunition"], p + ".usesAmmunition"),
            AmmoType = f["ammoType"].ValueKind == JsonValueKind.Null ? null : N("ammoType"), AmmoConsumption = N("ammoConsumption"),
        };
    }
}
