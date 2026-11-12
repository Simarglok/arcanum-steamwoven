using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-021: one attack's floater outcome (<see cref="CombatFloaterHit"/>) from the session's results: the resolution, who took the
/// damage and its life outcome, the critical item steps and the wear. Each field follows the original's condition for its message
/// (ledger CH12, CW2, CW3, CW7; the damage step 0x4B4390): the stun message whenever the stun bit meets a critter that was not stunned
/// and no knockdown bit (the original floats it also when the stun animation cannot start), unconsciousness when the knockout took
/// hold, the knockdown without a knockout bit, the scar also at the scar limit, the blinding and the crippling when new, an item drop
/// whenever the item was there to drop (the original floats it whether the drop lands or is refused), the armour and weapon steps
/// that met an item (an invulnerable weapon passes the critical weapon effects without a message). A target dead before the hit, a
/// refused attack, a miss and a prevented hit make no damage-step floater. Godot-free.
/// </summary>
public static class AreaCombatFloaters
{
    /// <summary>The floater outcome of a critter attack (<see cref="GameSessionOwner.AttackCritter"/> and its kin); null when it makes
    /// none (refused). <paramref name="target"/> and <paramref name="attacker"/> are the two critters' sides.</summary>
    public static CombatFloaterHit? Of(SessionAttackResult result, FloaterSide attacker, FloaterSide target)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Disposition == GameSessionDisposition.Rejected) return null;
        return Of(result.Resolution, result.Recipient, result.Life, result.Criticals, result.Wear, attacker, target, objectDamage: null);
    }

    /// <summary>The floater outcome of an attack on an object (<see cref="GameSessionOwner.AttackObject"/>): the object's damage, or a
    /// critical self-hit's step on the attacker; null when it makes none.</summary>
    public static CombatFloaterHit? Of(SessionObjectAttackResult result, FloaterSide attacker)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Disposition == GameSessionDisposition.Rejected) return null;
        // The original prints the damage after a portal's or container's material reduction, never below 0 (0x4B4390's object branch).
        int? damage = result.Object is { } hit ? Dealt(hit)
            : result.Item is not null && result.Resolution?.Damage is { Prevented: DamagePrevention.None } item && item.Recipient == DamageRecipient.Target
                ? Math.Max(item.HitPointDamage, 0) : null;
        return Of(result.Resolution, result.Life is null ? null : DamageRecipient.Attacker, result.Life, result.Criticals, result.Wear, attacker,
            FloaterSide.Other, damage);
    }

    /// <summary>
    /// ARC-COMBAT-1138: the floater outcome of damage that reached a door, window, container, scenery or trap and is no hero attack (a spell's,
    /// a trap's, a burn's, a script's; <see cref="AreaObjectHit"/>): the object's damage line, with no critter step. The damage function prints
    /// it whoever called it (CH12, CH13): at "text floaters" 2 only, in white.
    /// </summary>
    public static CombatFloaterHit Of(AreaObjectHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return new CombatFloaterHit { ObjectStep = true, HitPoints = Dealt(hit.Hit) };
    }

    /// <summary>ARC-COMBAT-1138 (OD16): the same line for damage that reached an item (<see cref="AreaItemHit"/>), its durability being its hit points.</summary>
    public static CombatFloaterHit Of(AreaItemHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return new CombatFloaterHit { ObjectStep = true, HitPoints = Math.Max(hit.Amount, 0) };
    }

    /// <summary>The damage a hit on an object dealt: what its damage grew by (the amount after the portal's or container's material
    /// reduction, never below 0), not the object's total damage.</summary>
    private static int Dealt(ObjectHitOutcome hit) => Math.Max(hit.Damage - hit.DamageBefore, 0);

    /// <summary>
    /// ARC-DEV-296 (B42): the floater outcome of a damage step that is no weapon swing or shot (a spell's, a trap's, a burn's, a script's;
    /// <see cref="SessionSpellDamageStep"/>). The original's damage function prints the same lines whoever called it (CH12): the damage
    /// amounts (the packet's, before the life setters adjust them, CH12), the multiplier, and the life effects that took hold. No critical
    /// is told (the roll belongs to an attack) and no item step ran. Null when the victim was dead already.
    /// </summary>
    public static CombatFloaterHit? Of(SessionSpellDamageStep step, FloaterSide struck)
    {
        ArgumentNullException.ThrowIfNull(step);
        var events = step.Life.Events;
        if (events.HasFlag(LifeEvents.AlreadyDead)) return null;
        var damage = step.Damage;
        var effects = damage.HitEffects;
        const CriticalHitEffects Down = CriticalHitEffects.Knockdown | CriticalHitEffects.Knockout;
        return new CombatFloaterHit
        {
            CritterStep = true,
            Struck = struck,
            Multiplier = damage.Multiplier switch
            {
                DamageMultiplier.Triple => FloaterMultiplier.Triple,
                DamageMultiplier.Double => FloaterMultiplier.Double,
                DamageMultiplier.OneAndHalf => FloaterMultiplier.OneAndHalf,
                _ => FloaterMultiplier.None,
            },
            Stunned = effects.HasFlag(CriticalHitEffects.Stun) && (effects & Down) == 0 && !step.Life.Before.Conditions.HasFlag(LifeConditions.Stunned),
            KnockedOut = events.HasFlag(LifeEvents.KnockedOut),
            KnockedDown = effects.HasFlag(CriticalHitEffects.Knockdown) && !effects.HasFlag(CriticalHitEffects.Knockout),
            Scarred = (events & (LifeEvents.Scarred | LifeEvents.ScarLimitReached)) != 0,
            Blinded = events.HasFlag(LifeEvents.Blinded),
            CrippledArm = (events & (LifeEvents.CrippledArm | LifeEvents.CrippledBothArms)) != 0,
            CrippledLegs = events.HasFlag(LifeEvents.CrippledLegs),
            // ARC-MAGIC-1232 (DW2): the weapon block's drop (Disarm) floats message 19 as an attack's critical drop does.
            WeaponDropped = step.Criticals.Any(c => c.Kind == SessionCriticalStepKind.DropWeapon && Tried(c.Outcome)),
            HitPoints = damage.HitPointDamage,
            Fatigue = damage.FatigueDamage,
            Poison = damage.PoisonDamage,
        };
    }

    private static CombatFloaterHit? Of(AttackResolution? resolution, DamageRecipient? recipient, LifeOutcome? life, IReadOnlyList<SessionCriticalStep> criticals,
        SessionAttackWearResult? wear, FloaterSide attacker, FloaterSide target, int? objectDamage)
    {
        if (resolution is null || resolution.Refusal != AttackRefusal.None) return null;
        if (resolution.Outcome == AttackOutcome.Miss)
            return resolution.MissCause == AttackMissCause.Dodged ? new CombatFloaterHit { Dodged = true } : null;
        var steps = wear?.Steps ?? [];
        var hit = new CombatFloaterHit
        {
            AttackerWeapon = steps.FirstOrDefault(s => s.Kind == SessionWearStepKind.AttackerWeapon && s.Skip == SessionWearSkip.None) is { } worn
                ? Item(worn) : FloaterItem.None,
            AttackerHands = steps.FirstOrDefault(s => s.Kind == SessionWearStepKind.AttackerHands && s.Skip == SessionWearSkip.None) is { } hands
                ? hands.Life?.HitPointDamage ?? hands.Amount : null,
        };
        if (objectDamage is { } amount && recipient != DamageRecipient.Attacker) return hit with { ObjectStep = true, HitPoints = amount };
        if (life is null || recipient is not { } who || life.Events.HasFlag(LifeEvents.AlreadyDead) || resolution.Damage is not { } damage) return hit;
        var effects = damage.HitEffects;
        var events = life.Events;
        const CriticalHitEffects Down = CriticalHitEffects.Knockdown | CriticalHitEffects.Knockout;
        bool Attempted(SessionCriticalStepKind kind) => criticals.Any(step => step.Kind == kind && step.Whose == who && Tried(step.Outcome));
        var armor = steps.FirstOrDefault(s => s.Kind == SessionWearStepKind.Armor && s.Wearer == who && s.Skip == SessionWearSkip.None);
        var weaponDamage = steps.FirstOrDefault(s => s.Kind == SessionWearStepKind.WeaponDamage && s.Wearer == who && s.Skip == SessionWearSkip.None);
        var destroyed = steps.Any(s => s.Kind == SessionWearStepKind.WeaponDestroy && s.Wearer == who && s.Skip == SessionWearSkip.None);
        return hit with
        {
            CritterStep = true,
            Struck = who == DamageRecipient.Attacker ? attacker : target,
            Critical = who == DamageRecipient.Attacker ? FloaterCritical.Miss : resolution.Outcome == AttackOutcome.CriticalHit ? FloaterCritical.Hit : FloaterCritical.None,
            Multiplier = damage.Multiplier switch
            {
                DamageMultiplier.Triple => FloaterMultiplier.Triple,
                DamageMultiplier.Double => FloaterMultiplier.Double,
                DamageMultiplier.OneAndHalf => FloaterMultiplier.OneAndHalf,
                _ => FloaterMultiplier.None,
            },
            Stunned = effects.HasFlag(CriticalHitEffects.Stun) && (effects & Down) == 0 && !life.Before.Conditions.HasFlag(LifeConditions.Stunned),
            KnockedOut = events.HasFlag(LifeEvents.KnockedOut),
            KnockedDown = effects.HasFlag(CriticalHitEffects.Knockdown) && !effects.HasFlag(CriticalHitEffects.Knockout),
            Scarred = (events & (LifeEvents.Scarred | LifeEvents.ScarLimitReached)) != 0,
            Blinded = events.HasFlag(LifeEvents.Blinded),
            CrippledArm = (events & (LifeEvents.CrippledArm | LifeEvents.CrippledBothArms)) != 0,
            ArmDropsWeapon = Attempted(SessionCriticalStepKind.ArmHitWeapon),
            ArmDropsShield = Attempted(SessionCriticalStepKind.ArmHitShield),
            CrippledLegs = events.HasFlag(LifeEvents.CrippledLegs),
            Armor = armor is null ? FloaterItem.None : Item(armor),
            HelmetDropped = Attempted(SessionCriticalStepKind.DropHelmet),
            Weapon = weaponDamage is null ? FloaterItem.None : Item(weaponDamage),
            WeaponDestroyed = destroyed,
            AmmunitionLost = Attempted(SessionCriticalStepKind.DestroyAmmo) || Attempted(SessionCriticalStepKind.LoseAmmo),
            WeaponDropped = Attempted(SessionCriticalStepKind.DropWeapon),
            // The original prints the step's amounts (after resistance, difficulty and the multiplier) before the life setters
            // adjust them (CH12): the packet's, not the life outcome's.
            HitPoints = damage.HitPointDamage,
            Fatigue = damage.FatigueDamage,
            Poison = damage.PoisonDamage,
        };
    }

    /// <summary>A drop or ammunition step the original ran on an item that was there (its message floats whatever the drop did):
    /// not an empty slot, an item the arm hit lets the critter keep, or one whose data the host did not pass.</summary>
    private static bool Tried(SessionCriticalStepOutcome outcome) =>
        outcome is not (SessionCriticalStepOutcome.NoItem or SessionCriticalStepOutcome.Kept or SessionCriticalStepOutcome.Unknown);

    /// <summary>An item's wear step: broken when its first or second break ran, else damaged.</summary>
    private static FloaterItem Item(SessionWearStep step) =>
        step.Item?.Break is ItemBreakStep.Broken or ItemBreakStep.Deleted ? FloaterItem.Broken : FloaterItem.Damaged;
}

/// <summary>
/// ARC-UI-021: the floaters' message texts, <c>mes\combat.mes</c> 0–24 read from the installed game when it reads (the player's
/// language, at game start as the original's combat init), otherwise the project's English text with the reason in
/// <see cref="Detail"/>. Godot-free.
/// </summary>
public sealed class AreaCombatTexts
{
    public const string CombatPath = "mes/combat.mes";
    private const int MaxText = 64;
    // The project's own wording, used only when the game's file does not give a line.
    private static readonly string[] ProjectText =
    [
        "Poison", "HP", "Critical miss", "Scar", "Blinded", "Arm crippled", "Leg crippled", "Armour damaged", "Weapon damaged",
        "Ammunition lost", "Fatigue", "Dodge", "Critical hit", "x1.5 damage", "x2 damage", "x3 damage", "Stunned", "Unconscious",
        "Knocked down", "Weapon dropped", "Item dropped", "Armour destroyed", "Weapon destroyed", "Weapon broken", "Armour broken",
    ];
    private readonly IReadOnlyDictionary<int, string> _game;

    private AreaCombatTexts(IReadOnlyDictionary<int, string> game, bool fromGame, string detail) { _game = game; FromGame = fromGame; Detail = detail; }

    public bool FromGame { get; }
    public string Detail { get; }
    public int GameCount => _game.Count;
    public static int Count => ProjectText.Length;

    public static AreaCombatTexts Project(string reason) => new(new Dictionary<int, string>(), false, reason ?? throw new ArgumentNullException(nameof(reason)));

    /// <summary>Texts over a lookup by message number (tests); what it does not answer takes the project's text.</summary>
    public static AreaCombatTexts From(IReadOnlyDictionary<int, string> messages, string detail) =>
        new(messages ?? throw new ArgumentNullException(nameof(messages)), true, detail ?? throw new ArgumentNullException(nameof(detail)));

    /// <summary>Reads <c>mes\combat.mes</c> from the game folder of <paramref name="modulePath"/>; any failure gives the project's text.</summary>
    public static AreaCombatTexts Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Project($"{CombatPath} not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Project($"{CombatPath} not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        if (AreaStartingHero.Table(stack, CombatPath, ContentLoadPhase.GameStart, required: true, out var detail) is not { } table)
            return Project($"{CombatPath} not read ({detail})");
        var kept = new Dictionary<int, string>();
        for (var number = 0; number < ProjectText.Length; number++)
            if (table.Find(number) is { } message && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 and <= MaxText } text })
                kept[number] = text;
        return new(kept, kept.Count > 0, $"{CombatPath}: {kept.Count} of {ProjectText.Length} texts");
    }

    public string Text(int message) => _game.GetValueOrDefault(message)
        ?? (message >= 0 && message < ProjectText.Length ? ProjectText[message] : $"[{message}]");
}
