using System.Collections.Immutable;

namespace Arcanum.Simulation.Goals;

/// <summary>
/// The 87 goal types of the retail 1.0.7.4 executable (numbers 0..86), by the original's number, with the executable's own name (the
/// goal-name table 0x5A164C) in each comment. ARC-DEV-131 (MIG13 S2, docs/architecture/goal-table.md §3.1): the numbers are the saved
/// ones, so they never change. The 26 goals of the NPC executor's former <c>AiGoalType</c> kept their names here, and S4 (ARC-DEV-134)
/// replaced that enum by this one (<see cref="Ai.AiExecutorRules.InterpretedGoals"/>). Goals 13 and 22 have no descriptor in the executable (<see cref="GoalDescriptors.Of"/> answers null for them).
/// </summary>
public enum GoalType
{
    /// <summary>0 [ag_animate]: one animation played once on an object (the script's perform animation, the decoration of a dying critter).</summary>
    Animate = 0,
    /// <summary>1 [ag_animate_loop]: an animation played in a loop (priority 1, not counted, carried across a map).</summary>
    AnimateLoop = 1,
    /// <summary>2 [ag_anim_fidget]: the idle fidget (the idle restart, the combat-mode toggle, concealment, sector placement, the timed fidget event).</summary>
    Fidget = 2,
    /// <summary>3 [ag_move_to_tile]: the walk to a tile (the hero's tile click, the standpoint walks); a main walk is retargeted in place.</summary>
    MoveToTile = 3,
    /// <summary>4 [ag_run_to_tile]: the run to a tile (the back-off's run, the held run).</summary>
    RunToTile = 4,
    /// <summary>5 [ag_attempt_move]: the plain walk of a planned segment (the flight's and the run to a tile's).</summary>
    AttemptMove = 5,
    /// <summary>6 [ag_move_to_pause]: the one-second real-time pause before a run without a path ends.</summary>
    MoveToPause = 6,
    /// <summary>7 [ag_move_near_tile]: the walk to within a range of a tile (the script's walk to, the wanders' alias).</summary>
    MoveNearTile = 7,
    /// <summary>8 [ag_move_near_obj]: the walk to within the goal's range of its object (the follower's walk to its leader, the item use's and the pickup's approach).</summary>
    MoveNear = 8,
    /// <summary>9 [ag_move_straight]: the straight-line move.</summary>
    MoveStraight = 9,
    /// <summary>10 [ag_attempt_move_straight]: the step of the straight-line move.</summary>
    AttemptMoveStraight = 10,
    /// <summary>11 [ag_open_door]: the opening of a door (a portal turns the use goals 38 and 81 into this one).</summary>
    OpenDoor = 11,
    /// <summary>12 [ag_attempt_open_door]: the attempt to open a door; its node starts the door's own run (goal 70). A no-interrupt goal.</summary>
    AttemptOpenDoor = 12,
    /// <summary>13 [ag_unlock_door]: the unlocking of a door. The executable has no descriptor for it.</summary>
    UnlockDoor = 13,
    /// <summary>14 [ag_jump_window]: the jump through a window.</summary>
    JumpWindow = 14,
    /// <summary>15 [ag_pickup_item]: the walk onto an item's tile and its pickup.</summary>
    PickupItem = 15,
    /// <summary>16 [ag_attempt_pickup]: the pickup itself.</summary>
    AttemptPickup = 16,
    /// <summary>17 [ag_pickpocket]: the pickpocket goal.</summary>
    Pickpocket = 17,
    /// <summary>18 [ag_attack]: the attack goal.</summary>
    Attack = 18,
    /// <summary>19 [ag_attempt_attack]: one attack animation and its hit.</summary>
    AttemptAttack = 19,
    /// <summary>20 [ag_kill]: the kill goal (the attempt attack's alias).</summary>
    Kill = 20,
    /// <summary>21 [ag_talk]: the approach and the opening of the dialog.</summary>
    Talk = 21,
    /// <summary>22 [ag_pick_weapon]: the weapon pick. The executable has no descriptor and no starter for it.</summary>
    PickWeapon = 22,
    /// <summary>23 [ag_chase]: the chase.</summary>
    Chase = 23,
    /// <summary>24 [ag_follow]: following the leader.</summary>
    Follow = 24,
    /// <summary>25 [ag_flee]: the flight.</summary>
    Flee = 25,
    /// <summary>26 [ag_throw_spell]: the casting goal of a spell without a casting animation.</summary>
    CastSpell = 26,
    /// <summary>27 [ag_attempt_spell]: the cast's play (the caster's eye candy) and its release.</summary>
    AttemptSpell = 27,
    /// <summary>28 [ag_shoot_spell]: the spell missile (a run of the missile object).</summary>
    ShootSpell = 28,
    /// <summary>29 [ag_hit_by_spell]: the reaction to a spell hit (no code starts it).</summary>
    HitBySpell = 29,
    /// <summary>30 [ag_hit_by_weapon]: the reaction to a weapon hit (no code starts it).</summary>
    HitByWeapon = 30,
    /// <summary>31 [ag_dying]: the death; its node creates the dying body's decoration.</summary>
    Dying = 31,
    /// <summary>32 [ag_destroy_obj]: the destruction of an object.</summary>
    DestroyObject = 32,
    /// <summary>33 [ag_use_skill_on]: the approach to a skill's target and the choice of its attempt goal.</summary>
    UseSkill = 33,
    /// <summary>34 [ag_attempt_use_skill_on]: a skill's attempt (every skill but pick locks and pickpocket).</summary>
    AttemptSkill = 34,
    /// <summary>35 [ag_skill_conceal]: the conceal skill (its starter is dead in the executable).</summary>
    SkillConceal = 35,
    /// <summary>36 [ag_projectile]: a thrown or shot projectile in flight (a run of the projectile object).</summary>
    Projectile = 36,
    /// <summary>37 [ag_throw_item]: the throw of an item at a tile.</summary>
    ThrowItem = 37,
    /// <summary>38 [ag_use_object]: the hero's use of a wall, portal or scenery (a portal turns it into the door goals).</summary>
    UseObject = 38,
    /// <summary>39 [ag_use_item_on_object]: the approach to an item use's target and the use.</summary>
    UseItem = 39,
    /// <summary>40 [ag_use_item_on_object_with_skill]: the item use on an object with a skill (the script's use item).</summary>
    UseItemWithSkill = 40,
    /// <summary>41 [ag_use_item_on_tile]: the item use on a tile.</summary>
    UseItemOnTile = 41,
    /// <summary>42 [ag_use_item_on_tile_with_skill]: the item use on a tile with a skill.</summary>
    UseItemOnTileWithSkill = 42,
    /// <summary>43 [ag_knockback]: the knockback of a hit critter (priority 5).</summary>
    Knockback = 43,
    /// <summary>44 [ag_floating]: floating text over an object (a no-interrupt overlay, not counted).</summary>
    Floating = 44,
    /// <summary>45 [ag_eye_candy]: an eye-candy animation (a no-interrupt overlay).</summary>
    EyeCandy = 45,
    /// <summary>46 [ag_eye_candy_reverse]: an eye-candy animation played backwards (a no-interrupt overlay).</summary>
    EyeCandyReverse = 46,
    /// <summary>47 [ag_eye_candy_callback]: an eye-candy animation with a callback (a no-interrupt overlay).</summary>
    EyeCandyCallback = 47,
    /// <summary>48 [ag_eye_candy_reverse_callback]: an eye-candy animation played backwards with a callback (a no-interrupt overlay).</summary>
    EyeCandyReverseCallback = 48,
    /// <summary>49 [ag_close_door]: the closing of a door.</summary>
    CloseDoor = 49,
    /// <summary>50 [ag_attempt_close_door]: the attempt to close a door; its node starts the door's own closing run (goal 71). A no-interrupt goal.</summary>
    AttemptCloseDoor = 50,
    /// <summary>51 [ag_animate_reverse]: an animation played backwards.</summary>
    AnimateReverse = 51,
    /// <summary>52 [ag_move_away_from_obj]: the escape line away from the source.</summary>
    MoveAway = 52,
    /// <summary>53 [ag_rotate]: the turn toward a direction (the dialog's turn, the right button's release).</summary>
    Rotate = 53,
    /// <summary>54 [ag_unconceal]: the end of concealment (priority 4).</summary>
    Unconceal = 54,
    /// <summary>55 [ag_run_near_tile]: the run to within a range of a tile (the script's run to).</summary>
    RunNearTile = 55,
    /// <summary>56 [ag_run_near_obj]: the follower's run to its leader.</summary>
    RunNear = 56,
    /// <summary>57 [ag_animate_stunned]: the stunned animation (priority 1, carried).</summary>
    AnimateStunned = 57,
    /// <summary>58 [ag_eye_candy_end_callback]: an eye-candy animation with an end callback (a no-interrupt overlay).</summary>
    EyeCandyEndCallback = 58,
    /// <summary>59 [ag_eye_candy_reverse_end_callback]: an eye-candy animation played backwards with an end callback (a no-interrupt overlay).</summary>
    EyeCandyReverseEndCallback = 59,
    /// <summary>60 [ag_animate_kneel_magic_hands]: the kneeling animation with magic hands (the script's perform animation for a critter).</summary>
    AnimateKneelMagicHands = 60,
    /// <summary>61 [ag_attempt_move_near]: the melee approach's walk of a planned segment.</summary>
    MeleeApproach = 61,
    /// <summary>62 [ag_knock_down]: the knock-down (carried; a knock-down also stops a spell release).</summary>
    KnockDown = 62,
    /// <summary>63 [ag_anim_get_up]: the get-up after a knock-down.</summary>
    GetUp = 63,
    /// <summary>64 [ag_attempt_move_straight_knockback]: the knockback's straight step.</summary>
    AttemptMoveStraightKnockback = 64,
    /// <summary>65 [ag_wander]: the wander (the walk's alias).</summary>
    Wander = 65,
    /// <summary>66 [ag_wander_seek_darkness]: the wander that seeks darkness (the walk's alias).</summary>
    WanderSeekDarkness = 66,
    /// <summary>67 [ag_use_picklock_skill_on]: the pick-locks attempt.</summary>
    AttemptPickLocks = 67,
    /// <summary>68 [ag_please_move]: the order to step aside.</summary>
    PleaseMove = 68,
    /// <summary>69 [ag_attempt_spread_out]: the spread-out order's goal (a no-interrupt goal).</summary>
    SpreadOut = 69,
    /// <summary>70 [ag_animate_door_open]: the opening animation of a door (the door's own run).</summary>
    AnimateDoorOpen = 70,
    /// <summary>71 [ag_animate_door_closed]: the closing animation of a door (the door's own run).</summary>
    AnimateDoorClosed = 71,
    /// <summary>72 [ag_pend_closing_door]: the door's wait before it closes (every 1.5 s).</summary>
    PendClosingDoor = 72,
    /// <summary>73 [ag_throw_spell_friendly]: the casting goal of a friendly spell (a no-interrupt goal).</summary>
    CastSpellFriendly = 73,
    /// <summary>74 [ag_attempt_spell_friendly]: the friendly cast's play and release (a no-interrupt goal).</summary>
    AttemptSpellFriendly = 74,
    /// <summary>75 [ag_eye_candy_fire_dmg]: the fire-damage eye candy (a no-interrupt overlay, not counted).</summary>
    EyeCandyFireDamage = 75,
    /// <summary>76 [ag_eye_candy_reverse_fire_dmg]: the fire-damage eye candy played backwards (a no-interrupt overlay, not counted).</summary>
    EyeCandyReverseFireDamage = 76,
    /// <summary>77 [ag_animate_loop_fire_dmg]: the burning scenery's loop (priority 1, not counted).</summary>
    AnimateLoopFireDamage = 77,
    /// <summary>78 [ag_attempt_move_straight_spell]: a spell's straight step.</summary>
    AttemptMoveStraightSpell = 78,
    /// <summary>79 [ag_move_near_obj_combat]: the combat approach that plans the attack's path.</summary>
    CombatApproach = 79,
    /// <summary>80 [ag_attempt_move_near_combat]: the ranged approach's walk of a planned segment.</summary>
    RangedApproach = 80,
    /// <summary>81 [ag_use_container]: the hero's use of a container or a dead critter (the loot).</summary>
    UseContainer = 81,
    /// <summary>82 [ag_throw_spell_w_cast_anim]: the casting goal of a spell with a casting animation.</summary>
    CastSpellWithAnimation = 82,
    /// <summary>83 [ag_attempt_spell_w_cast_anim]: the cast's play (the caster's casting animation) and its release.</summary>
    AttemptSpellWithAnimation = 83,
    /// <summary>84 [ag_throw_spell_w_cast_anim_2ndary]: the casting run's secondary cast animation (a no-interrupt goal, carried).</summary>
    CastSpellWithAnimationSecondary = 84,
    /// <summary>85 [ag_back_off_from]: the back-off from an object (its starter is dead in the executable).</summary>
    BackOffFrom = 85,
    /// <summary>86 [ag_attempt_use_pickpocket_skill_on]: the pickpocket attempt.</summary>
    AttemptPickpocket = 86,
}

/// <summary>The goal-type numbers and the executable's own names for them.</summary>
public static class GoalTypes
{
    /// <summary>The number of goal types: 0..86 (the descriptor table has 87 slots, 85 with a descriptor).</summary>
    public const int Count = 87;

    // The goal-name table 0x5A164C of the retail 1.0.7.4 executable, read from its file image (ARC-DEV-131, docs/architecture/goal-table.md, "The S2 read").
    private static readonly ImmutableArray<string> OriginalNames =
    [
        "ag_animate", "ag_animate_loop", "ag_anim_fidget", "ag_move_to_tile", "ag_run_to_tile", "ag_attempt_move", "ag_move_to_pause",
        "ag_move_near_tile", "ag_move_near_obj", "ag_move_straight", "ag_attempt_move_straight", "ag_open_door", "ag_attempt_open_door",
        "ag_unlock_door", "ag_jump_window", "ag_pickup_item", "ag_attempt_pickup", "ag_pickpocket", "ag_attack", "ag_attempt_attack",
        "ag_kill", "ag_talk", "ag_pick_weapon", "ag_chase", "ag_follow", "ag_flee", "ag_throw_spell", "ag_attempt_spell", "ag_shoot_spell",
        "ag_hit_by_spell", "ag_hit_by_weapon", "ag_dying", "ag_destroy_obj", "ag_use_skill_on", "ag_attempt_use_skill_on", "ag_skill_conceal",
        "ag_projectile", "ag_throw_item", "ag_use_object", "ag_use_item_on_object", "ag_use_item_on_object_with_skill", "ag_use_item_on_tile",
        "ag_use_item_on_tile_with_skill", "ag_knockback", "ag_floating", "ag_eye_candy", "ag_eye_candy_reverse", "ag_eye_candy_callback",
        "ag_eye_candy_reverse_callback", "ag_close_door", "ag_attempt_close_door", "ag_animate_reverse", "ag_move_away_from_obj", "ag_rotate",
        "ag_unconceal", "ag_run_near_tile", "ag_run_near_obj", "ag_animate_stunned", "ag_eye_candy_end_callback",
        "ag_eye_candy_reverse_end_callback", "ag_animate_kneel_magic_hands", "ag_attempt_move_near", "ag_knock_down", "ag_anim_get_up",
        "ag_attempt_move_straight_knockback", "ag_wander", "ag_wander_seek_darkness", "ag_use_picklock_skill_on", "ag_please_move",
        "ag_attempt_spread_out", "ag_animate_door_open", "ag_animate_door_closed", "ag_pend_closing_door", "ag_throw_spell_friendly",
        "ag_attempt_spell_friendly", "ag_eye_candy_fire_dmg", "ag_eye_candy_reverse_fire_dmg", "ag_animate_loop_fire_dmg",
        "ag_attempt_move_straight_spell", "ag_move_near_obj_combat", "ag_attempt_move_near_combat", "ag_use_container",
        "ag_throw_spell_w_cast_anim", "ag_attempt_spell_w_cast_anim", "ag_throw_spell_w_cast_anim_2ndary", "ag_back_off_from",
        "ag_attempt_use_pickpocket_skill_on",
    ];

    /// <summary>Whether <paramref name="number"/> is one of the 87 goal numbers.</summary>
    public static bool IsDefined(int number) => number is >= 0 and < Count;

    /// <summary>The executable's own name of a goal type (the goal-name table), for logs and overlays.</summary>
    public static string OriginalName(GoalType type)
    {
        if (!IsDefined((int)type)) throw new ArgumentOutOfRangeException(nameof(type));
        return OriginalNames[(int)type];
    }
}
