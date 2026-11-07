using Arcanum.Session;
using Arcanum.Simulation.Character;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The page of the character sheet a + or − belongs to.</summary>
public enum AreaSheetPage { Stat, Skill, Spell, Tech }

/// <summary>
/// ARC-UI-078: which <c>charedit.mes</c> message (17..39) the original's character editor shows for a refused + or − of each page, from the
/// ledger (CP3–CP7, SP7, SG6, TD7, TD8, CHW23). Godot-free. The original shows a message only for the player while the editor is open; a refusal
/// is a message-window entry (type 4: its sound 3004 and its 3 s hint delay, ML2), a success shows none.
/// </summary>
public static class AreaSheetRefusals
{
    public const int NotEnoughPoints = 18, SkillAtMaximum = 21, SkillKeyStatDefault = 22, SkillFloor = 23, SkillNoPoints = 24, SkillOther = 25,
        RaiseIntelligence = 26, SpellFloor = 27, SpellLevel = 30, StatMaximum = 17, StatFloor = 19, StatMinimum = 20, StatSkillPrerequisite = 28,
        IntelligenceSpellPrerequisite = 29, IntelligenceDegreePrerequisite = 31, WillpowerSpellPrerequisite = 39;

    /// <summary>"You must raise your &lt;stat&gt; first" of a skill's key stat (SP7): one message per stat, 22 for any other.</summary>
    public static int KeyStatMessage(CharacterStat? stat) => stat switch
    {
        CharacterStat.Strength => 32,
        CharacterStat.Dexterity => 33,
        CharacterStat.Constitution => 34,
        CharacterStat.Beauty => 35,
        CharacterStat.Intelligence => RaiseIntelligence,
        CharacterStat.Willpower => 36,
        CharacterStat.Perception => 37,
        CharacterStat.Charisma => 38,
        _ => SkillKeyStatDefault,
    };

    /// <summary>The message number of a refused press, or null when the original shows none (a success, a silent refusal, or a refusal the
    /// page cannot reach because it hides the button). <paramref name="pressed"/> is the stat of a stat page's row (null for hit points and fatigue).</summary>
    public static int? Number(AreaSheetPage page, bool spend, SessionPointResult result, CharacterStat? pressed = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Disposition == GameSessionDisposition.Changed || result.Failure != GameSessionFailure.PointRefused || result.Change is not { } change) return null;
        var status = change.Status;
        return page switch
        {
            AreaSheetPage.Skill => spend
                ? status switch
                {
                    PointChangeStatus.NotEnoughPoints => NotEnoughPoints,
                    PointChangeStatus.AtMaximum => SkillAtMaximum,
                    PointChangeStatus.KeyStatTooLow => KeyStatMessage(change.Stat),
                    _ => null,
                }
                : status switch
                {
                    PointChangeStatus.AtFloor => SkillFloor,
                    PointChangeStatus.AtMinimum => SkillNoPoints,
                    PointChangeStatus.KeyStatTooLow => SkillOther,
                    _ => null,
                },
            AreaSheetPage.Spell => spend
                ? status switch
                {
                    PointChangeStatus.NotEnoughPoints => NotEnoughPoints,
                    PointChangeStatus.LevelTooLow => SpellLevel,
                    PointChangeStatus.IntelligenceTooLow => RaiseIntelligence,
                    PointChangeStatus.StatTooLow => KeyStatMessage(change.Stat ?? CharacterStat.Willpower),
                    _ => null,
                }
                : status == PointChangeStatus.AtFloor ? SpellFloor : null,
            // CP3, CP4, CHW23: a + names the maximum (17: the effective value at the race's maximum, or a read-back below the new value) before the points (18);
            // a − names the editor's snapshot (19, for hit points and fatigue too), the minimum (20), then the cause: a skill (28), a spell of intelligence
            // (29) or willpower (39), a tech degree (31); no other refusal shows a message. PROJECT: the cause is the session's own check on the effective
            // value, the original tests base − 2 (its flaw, CHW23 F0).
            AreaSheetPage.Stat => spend
                ? status switch { PointChangeStatus.AtMaximum => StatMaximum, PointChangeStatus.NotEnoughPoints => NotEnoughPoints, _ => null }
                : status switch
                {
                    PointChangeStatus.AtFloor => StatFloor,
                    PointChangeStatus.AtMinimum => StatMinimum,
                    PointChangeStatus.SkillNeedsStat => StatSkillPrerequisite,
                    PointChangeStatus.SpellNeedsStat => (change.Stat ?? pressed) == CharacterStat.Willpower ? WillpowerSpellPrerequisite : IntelligenceSpellPrerequisite,
                    PointChangeStatus.DegreeNeedsStat => IntelligenceDegreePrerequisite,
                    _ => null,
                },
            // TD7: too few points 18, then any refusal of the raise 26 (the degree 7 over-read ends in the same message); TD8: a refused lower is silent.
            AreaSheetPage.Tech => spend
                ? status switch
                {
                    PointChangeStatus.NotEnoughPoints => NotEnoughPoints,
                    PointChangeStatus.IntelligenceTooLow or PointChangeStatus.AtMaximum => RaiseIntelligence,
                    _ => null,
                }
                : null,
            _ => null,
        };
    }

    /// <summary>The text the art look shows for a press: the original's message when its number is known and the game folder gives its text,
    /// else <paramref name="projectMessage"/> for a refusal; null when the original shows nothing (a success, a silent refusal).</summary>
    public static string? Message(AreaCharacterSheetTexts texts, AreaSheetPage page, bool spend, SessionPointResult result, string projectMessage,
        CharacterStat? pressed = null)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Disposition == GameSessionDisposition.Changed) return null;
        if (Number(page, spend, result, pressed) is { } number) return texts.Refusal(number) ?? projectMessage;
        // A refusal the table does not list (or a silent one): silent for the tech − (TD8), the project's words otherwise.
        return page == AreaSheetPage.Tech && !spend ? null : projectMessage;
    }
}
