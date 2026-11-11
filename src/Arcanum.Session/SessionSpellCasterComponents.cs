using Arcanum.Data.Magic;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-358 (magic audit MA-1): the dispatch of <see cref="SessionSpellCaster.Apply"/> as a table by component type, so each component group is one handler (and one partial file)
/// and a slice that adds a component adds one line here instead of another test in a shared if-chain. The original's component table (0x5B0BA8) is the same shape: one function per
/// component type. A handler answers the outcome of a component it took, or null when it leaves the target undone (a type it does not model, an object it does not take): the caller
/// then logs <see cref="SpellCastEventKind.ComponentNotReady"/>.
/// </summary>
public sealed partial class SessionSpellCaster
{
    private delegate SpellComponentOutcome? ComponentHandler(SessionSpellCaster caster, SpellRun run, SessionSpellComponent component, SpellRunTarget target);

    // One line per component type with a handler. The groups: damage and heal (SessionSpellCaster.cs), destroy, eye candy and the goal components (SessionSpellCaster.cs), Summon
    // (SessionSpellCasterSummon.cs), ObjFlag (SessionSpellCasterObjectFlags.cs), Effect (SessionSpellCasterEffects.cs). Another slice's group adds its line and its own partial file.
    private static readonly IReadOnlyDictionary<SpellComponentType, ComponentHandler> ComponentHandlers = new Dictionary<SpellComponentType, ComponentHandler>
    {
        [SpellComponentType.Damage] = static (c, run, component, target) => c.DamageStep(run, component, target, heal: false),
        [SpellComponentType.Heal] = static (c, run, component, target) => c.DamageStep(run, component, target, heal: true),
        [SpellComponentType.Destroy] = static (c, run, _, target) => c.DestroyComponent(run, target) ? SpellComponentOutcome.None : null,
        [SpellComponentType.EyeCandy] = static (c, run, component, target) => c.EyeCandy(run, component, target),
        [SpellComponentType.Summon] = static (c, run, component, target) => c.SummonComponent(run, component, target) ? SpellComponentOutcome.None : null,
        [SpellComponentType.AGoal] = static (c, run, component, target) => c.GoalComponent(run, component, target),
        [SpellComponentType.AGoalTerminate] = static (c, run, component, target) => c.GoalTerminateComponent(run, component, target),
        [SpellComponentType.ObjFlag] = static (c, run, component, target) => c.ObjectFlag(run, component, target) ? SpellComponentOutcome.None : null,
        [SpellComponentType.Effect] = static (c, run, component, target) => c.EffectComponent(run, component, target) ? SpellComponentOutcome.None : null,
    };

    // Damage and Heal: the component's data on its target object; without the target object the component does nothing and is not "not ready" (its data is there).
    private SpellComponentOutcome? DamageStep(SpellRun run, SessionSpellComponent component, SpellRunTarget target, bool heal)
    {
        if (component.Damage is not { } data) return null;
        if (target.Object is not { } obj) return SpellComponentOutcome.None;
        if (heal) ApplyHeal(run, data, obj);
        else ApplyDamage(run, data, obj);
        return SpellComponentOutcome.None;
    }

    // The knockback goal 43 and the floating goal 44 of an AGoal component; any other goal is not ready.
    private SpellComponentOutcome? GoalComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.AnimationGoal == KnockbackGoal) Knockback(run, target);
        else if (component.AnimationGoal == HoverGoal) Hover(run, target, true);
        else return null;
        return SpellComponentOutcome.None;
    }

    // The end of the floating goal 44 (AGoalTerminate); any other terminated goal is not ready.
    private SpellComponentOutcome? GoalTerminateComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.TerminatedGoal != HoverGoal) return null;
        Hover(run, target, false);
        return SpellComponentOutcome.None;
    }
}
