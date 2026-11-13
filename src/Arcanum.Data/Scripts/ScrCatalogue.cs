namespace Arcanum.Data.Scripts;

/// <summary>How a number operand is read: its kind byte selects where the value comes from and
/// the 32-bit value is the index (or the number itself).</summary>
public enum ScrValueKind : byte
{
    /// <summary>One of the attachment's four byte counters, index 0..3.</summary>
    Counter = 0,
    /// <summary>A game-wide variable, index 0..1999.</summary>
    GlobalVariable = 1,
    /// <summary>A variable of this run, index 0..9, zero when the run starts.</summary>
    LocalVariable = 2,
    /// <summary>The value itself.</summary>
    Number = 3,
    /// <summary>A game-wide flag, index 0..3199, read as 0 or 1.</summary>
    GlobalFlag = 4,
    /// <summary>A variable of a PC; the value holds a focus kind above 16 bits and the index below.</summary>
    PcVariable = 5,
    /// <summary>A flag of a PC, encoded as <see cref="PcVariable"/>.</summary>
    PcFlag = 6,
}

/// <summary>How an object operand is read: a single object, or a group that the clause tests or
/// acts on as a whole ("every") or for at least one member ("any").</summary>
public enum ScrFocusKind : byte
{
    Triggerer = 0, Attachee = 1, EveryFollower = 2, AnyFollower = 3,
    EveryoneInParty = 4, AnyoneInParty = 5, EveryoneInTeam = 6, AnyoneInTeam = 7,
    EveryoneInVicinity = 8, AnyoneInVicinity = 9, CurrentLoopedObject = 10,
    /// <summary>An object variable of this run, index 0..9.</summary>
    LocalObject = 11,
    ExtraObject = 12, EveryoneInGroup = 13, AnyoneInGroup = 14,
    EverySceneryInVicinity = 15, AnySceneryInVicinity = 16, EveryContainerInVicinity = 17, AnyContainerInVicinity = 18,
    EveryPortalInVicinity = 19, AnyPortalInVicinity = 20, Player = 21, EveryItemInVicinity = 22, AnyItemInVicinity = 23,
}

/// <summary>What an operand slot of a type reads.</summary>
public enum ScrOperandRole : byte
{
    /// <summary>The slot is never read; its bytes mean nothing.</summary>
    Unused,
    /// <summary>A number operand (<see cref="ScrValueKind"/>).</summary>
    Number,
    /// <summary>An object operand (<see cref="ScrFocusKind"/>).</summary>
    Object,
}

/// <summary>The SCR type catalogue: 53 condition types (0..52) and 142 action types (0..141) with
/// the operand signature of each, which slots take a number and which an object, in order. The
/// numbers and signatures are format facts read from the source's interpreter (docs/research/scr-census.md) and cross-checked
/// against the ScriptEd 1.50 editor data and the installed scripts (ARC-RES-045); only the types
/// the project reads by name have named constants. Signatures are the editor's; for action 86
/// (float newspaper headline) the interpreter reading takes the object from slot 1 instead (census anomaly X9), so
/// a slice that admits 86 must decide which slot it checks.</summary>
public static class ScrCatalogue
{
    public const int ConditionTypes = 53, ActionTypes = 142, ValueKinds = 7, FocusKinds = 24;

    // 'n' number, 'o' object, '-' no operands.
    private static readonly string[] ConditionSignatures =
        ("- - on n nn nn onn nn on on oo o o o o o o on oo on on on o o oo o o o oo oo o o onn on onnn on o o o o on n o n oon nn n o on on - o o").Split(' ');
    private static readonly string[] ActionSignatures =
        ("- - - n n - n nnoo n n nn nnn nnn nnn nnn oo onn nn o - - oo o no nn no no no no oon oon oon on non on on oo nnn on on n noo n n onnn onn onn non ono no no no no on nnoon nnoon o o o on onn no on on no no no o o onn on oon on on onn on on onn on ooonn nooon onno n no on nn o nn o on n noon on o on onn n n n onn n n nnnonnn nnnn no n nn onn onn o n on on n no oo no o ono on no ono ono n no no no o on o no n - on on no no non onn on onnn onnn").Split(' ');

    public static bool IsKnownCondition(int type) => type is >= 0 and < ConditionTypes;
    public static bool IsKnownAction(int type) => type is >= 0 and < ActionTypes;
    public static bool IsKnownValueKind(byte kind) => kind < ValueKinds;
    public static bool IsKnownFocusKind(byte kind) => kind < FocusKinds;

    /// <summary>The role of each of the eight operand slots, or null for an unknown type.</summary>
    public static IReadOnlyList<ScrOperandRole>? Signature(ScrClauseSide side, int type)
    {
        var table = side == ScrClauseSide.Condition ? ConditionRoles : ActionRoles;
        return type >= 0 && type < table.Length ? table[type] : null;
    }

    private static readonly IReadOnlyList<ScrOperandRole>[] ConditionRoles = Roles(ConditionSignatures), ActionRoles = Roles(ActionSignatures);

    private static IReadOnlyList<ScrOperandRole>[] Roles(string[] signatures) => signatures.Select(text =>
    {
        var roles = new ScrOperandRole[ScrClause.Operands];
        if (text != "-")
            for (var i = 0; i < text.Length; i++) roles[i] = text[i] == 'n' ? ScrOperandRole.Number : ScrOperandRole.Object;
        return (IReadOnlyList<ScrOperandRole>)Array.AsReadOnly(roles);
    }).ToArray();

    /// <summary>Condition types the project reads by name.</summary>
    public static class Condition
    {
        public const int True = 0, LocalFlag = 3, Equal = 4, LessOrEqual = 5, PcQuestState = 6, HasItemNamed = 17, FollowingPc = 18, IsNamed = 20,
            IsWieldingItem = 21, IsDead = 22, IsAnimal = 37, IsInCombat = 31, IsSwitchedOff = 27, HasReputation = 33, Jilted = 39, GlobalFlag = 43, Identified = 47, ItemsBeingRewielded = 50,
            HasBodySpell = 14, InfluencedBySpell = 35, KnowsSpell = 48, MasteredSpellCollege = 49,
            Daytime = 1, GlobalQuestState = 7, HasBlessing = 8, HasCurse = 9, RumorKnown = 40, RumorQuelled = 41,
            ObjIsOpen = 36, SectorIsBlocked = 45, MonsterGenDisabled = 46,
            MetPcBefore = 10, IsInvisible = 15, IsSurrendered = 25, IsInDialog = 26, CanSeeObject = 28, CanHearObject = 29, IsInvulnerable = 30, IsUndead = 38, IsProwling = 51;
    }

    /// <summary>Action types the project reads by name.</summary>
    public static class Action
    {
        public const int DoNothing = 0, ReturnSkipDefault = 1, ReturnRunDefault = 2, Goto = 3, Dialog = 4, RemoveThisScript = 5, ChangeThisScript = 6, CallScript = 7,
            SetLocalFlag = 8, ClearLocalFlag = 9, Assign = 10, Add = 11, Subtract = 12, Multiply = 13, Divide = 14, PrintLine = 24,
            LoopFor = 18, LoopEnd = 19, LoopBreak = 20, GetStoryState = 42, SetStoryState = 43, Teleport = 44,
            CallScriptIn = 54, CallScriptAt = 55, CallScriptEx = 81, PlaySound = 82, PlaySoundOn = 83, PlaySoundScheme = 87, SetGlobalFlag = 100, ClearGlobalFlag = 101, FadeAndTeleport = 102, Fade = 103,
            RemoveScriptCall = 125, UseAmmoUnits = 135, StartSchematicUi = 129, SetAutoLevelScheme = 139, ShowBook = 64, ShowPicture = 65, ShowPlaque = 114,
            GetSkill = 47, UseItemWithSkill = 79, AssignObject = 15, GetObjectType = 34,
            SetPcQuestState = 16, MarkMapLocation = 49, SetRumor = 50, AddReputation = 72, RemoveReputation = 73,
            AddEffect = 77, RemoveEffect = 78, GetMagickTechAdjustment = 80, MagickTechAdjustment = 91, PlayScriptEyeCandy = 120, Random = 37, CreateObject = 52,
            ToggleState = 56, Kill = 58, DistanceBetween = 71, SetFaction = 134, FloatLine = 23, GetArea = 84, Destroy = 68, StopAttacking = 109, GetStat = 33, GetReaction = 29,
            UnfogTownMap = 113, WalkTo = 69, RunTo = 74, HealFatigue = 76, PerformAnimation = 62, StealCoins = 115, BecomeProne = 93,
            CastSpell = 48, CastSpellOn = 61, CastFreeSpell = 118, CastUnresistableSpellBy = 121, CastFreeUnresistableSpell = 122, CastUnresistableSpellOn = 136,
            Damage = 60, DamageUnresistable = 138, HealHitPoints = 75, GiveSpellMastery = 112,
            MakeFollow = 21, Attack = 36, TransformObject = 40,
            SetGlobalQuestState = 17, AddBlessing = 25, RemoveBlessing = 26, AddCurse = 27, RemoveCurse = 28, QuellRumor = 51, GiveQuestExperience = 63,
            GetDaySinceStartup = 96, GetCurrentHour = 97, GetCurrentMinute = 98, GetHoursSinceStartup = 105, GrantFatePoint = 117, SetPcQuestUnbotched = 119,
            CritterDisband = 22, SetReaction = 30, AdjustReaction = 31, SetDayStandpoint = 45, SetNightStandpoint = 46, ChangeArtNumber = 59, ChangeScript = 99, SetDayStandpointEx = 140, SetNightStandpointEx = 141,
            TransferItem = 41, SetLockState = 53, CreateItem = 66, ToggleOpenClosed = 88, SetWrittenStart = 94, ToggleSectorBlocked = 106, ToggleMonsterGenerator = 110, TouchArt = 123,
            DestroyItemNamed = 126, ToggleItemInventoryDisplay = 127, GetLocation = 95, StopSpellOnObject = 130, GetHitPoints = 107, GetFatiguePoints = 108, HealPoison = 128, AdjustStat = 137,
            GetSocialClass = 38, GetOrigin = 39, ToggleInvulnerability = 57, GetFaction = 89, Rename = 92;
    }

    /// <summary>Script file flags.</summary>
    public static class FileFlag
    {
        public const uint NonmagicalTrap = 0x1, MagicalTrap = 0x2, AutoRemoving = 0x4, DeathSpeech = 0x8, SurrenderSpeech = 0x10,
            RadiusTwo = 0x20, RadiusThree = 0x40, RadiusFive = 0x80, TeleportTrigger = 0x100;
    }

    public const int LocalFlags = 32, Counters = 4, LocalSlots = 10, GlobalFlags = 3200, GlobalVariables = 2000;

    /// <summary>Script numbers are 1..99999 (five-digit file names); 0 is "no script".</summary>
    public const int MaxScriptNumber = 99999;
}

/// <summary>Which clause of a line.</summary>
public enum ScrClauseSide { Condition, OnTrue, OnFalse }
