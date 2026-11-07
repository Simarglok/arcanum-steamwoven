using Arcanum.Data.Combat;
using Arcanum.Data.Magic;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Skills;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-088: an NPC's character sheet for the dialog action <c>ce</c> (the original's window mode 2, ledger CHW24): the main page's view, the
/// skills, tech and stats pages and the sheet the spells page is built from. Read-only: no + or − button anywhere.
/// </summary>
public sealed record AreaNpcSheetPages(string Entity, AreaCharacterSheetView View, AreaSkillsPageView Skills, AreaStatsPageView Stats, AreaTechPageView Tech,
    AdvancementSheet Sheet, AdvancementModifiers Modifiers)
{
    /// <summary>The spells page of <paramref name="college"/> (0..15) from the NPC's spell book.</summary>
    public AreaSpellsPageView Spells(SpellData? data, int college, System.Collections.Immutable.ImmutableArray<int> willpower) =>
        AreaSpellsPage.BuildFor(data, college, Sheet.Colleges, Sheet.MasteredCollege, Sheet.Race, willpower, null, Sheet.UnspentPoints);
}

/// <summary>
/// ARC-UI-088: builds an NPC's sheet from what the viewer has of it: its stored fields (<see cref="CombatFields"/>: the name's critter record,
/// experience, alignment, gender, stored resistances), its sheet as the session saved it or its map data loads it (level, points, stats, skills,
/// spell book, degrees; <see cref="AreaAdvancement.Npc"/>) with its effects' changes, and its life. Every mismatch with the original's sheet
/// (the worn armour's resistances, the portrait) is listed in the ledger (CHW24). Godot-free.
/// </summary>
public static class AreaNpcSheet
{
    // The original's stat numbers of the stored base stats (obj_f_critter_stat_base_idx).
    private const int ExperienceStat = 18, AlignmentStat = 19, GenderStat = 26;

    /// <summary>The sheet of an NPC, or null when its fields give no admissible sheet.</summary>
    /// <param name="loaded">The NPC's sheet and effects as its map data loads them (<see cref="AreaAdvancement.Npc"/>); the session's saved record replaces the sheet.</param>
    /// <param name="life">The NPC's current and maximum hit points and fatigue (null: its hit and fatigue points show 0).</param>
    public static AreaNpcSheetPages? Build(GameSessionOwner session, string name, CombatFields fields, (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers) loaded,
        AreaLife? life, SkillTexts? skillTexts, MesTextProfile profile)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Critter is not { } critter) return null;
        var entity = loaded.Loaded.Entity;
        var sheet = (session.QueryNpcAdvancement(entity) ?? loaded.Loaded).Sheet;
        var modifiers = loaded.Modifiers;
        var stored = critter.BaseStats;
        int Stored(int index) => index < stored.Count ? stored[index] : 0;
        var experience = Stored(ExperienceStat);
        var table = session.Experience?.Levels;
        var primary = AreaStatsPage.StatOrder.Select(stat => new AreaSheetValue(AreaCharacterSheet.Label(stat), AutoLevelRules.EffectiveStat(sheet, modifiers, stat),
            Mark(sheet, modifiers, stat))).ToArray();
        var secondary = new[]
        {
            CharacterStat.CarryWeight, CharacterStat.DamageBonus, CharacterStat.AcAdjustment, CharacterStat.Speed, CharacterStat.HealRate,
            CharacterStat.PoisonRecovery, CharacterStat.ReactionModifier, CharacterStat.MaxFollowers,
        }.Select(stat => new AreaSheetValue(AreaCharacterSheet.Label(stat), AutoLevelRules.DerivedStat(sheet, modifiers, stat))).ToArray();
        var aptitude = AutoLevelRules.DerivedStat(sheet, modifiers, CharacterStat.MagickTechAptitude);
        // The stored resistances (normal, fire, electrical, poison, magic) and the constitution bonus, as 0x43D6D0 shows them; the worn armour of an NPC is not read (CHW24).
        var resist = fields.Object.Resistances;
        int Resist(int index) => index < resist.Count ? resist[index] : 0;
        var shown = CombatantRules.Resistances(new(Resist(0), Resist(3), Resist(2), Resist(1)), EquipmentResistances.None,
            AutoLevelRules.EffectiveStat(sheet, modifiers, CharacterStat.Constitution), sheet.Monstrous);
        AreaSheetValue Resistance(string label, int value) => new(label, Math.Clamp(value, 0, AreaCharacterSheet.MaxResistance));
        var resistances = new[]
        {
            Resistance("Normal", shown.Normal), Resistance("Magick", Resist(4)), Resistance("Fire", shown.Fire),
            Resistance("Poison", shown.Poison), Resistance("Electrical", shown.Electrical),
        };
        var view = new AreaCharacterSheetView(name, sheet.Race, Stored(GenderStat) == 0 ? CharacterGender.Female : CharacterGender.Male, sheet.Level, experience,
            table is null ? null : ExperienceRules.ToNextLevel(table, Math.Clamp(sheet.Level, 1, ExperienceRules.MaxLevel), experience), sheet.UnspentPoints,
            Stored(AlignmentStat) / 10, Math.Max(0, aptitude), Math.Max(0, -aptitude), life?.Health ?? 0, life?.MaxHealth ?? 0, life?.Fatigue ?? 0, life?.MaxFatigue ?? 0,
            primary, secondary, resistances) { ShowRaceGender = !sheet.Monstrous };
        var skills = AreaSkillsPage.BuildFor(session.QueryNpcSkills(new SessionNpcAdvancement(entity, sheet, loaded.Loaded.AutoLevelScheme), modifiers), skillTexts, profile,
            sheet.UnspentPoints, null);
        var rows = new AreaStatRow[AreaStatsPage.Rows];
        rows[0] = new(0, AdvancementTrack.HitPoints, null, "Hit points", sheet.HitPointPoints, life?.MaxHealth ?? 0, CharacterPointRules.PoolPointCost, sheet.HitPointPoints);
        rows[1] = new(1, AdvancementTrack.Fatigue, null, "Fatigue", sheet.FatiguePoints, life?.MaxFatigue ?? 0, CharacterPointRules.PoolPointCost, sheet.FatiguePoints);
        for (var row = 2; row < AreaStatsPage.Rows; row++)
        {
            var stat = AreaStatsPage.StatOrder[row - 2];
            rows[row] = new(row, AdvancementTrack.Stat, stat, stat.ToString(), sheet.Stats[(int)stat], primary[row - 2].Value, CharacterPointRules.NextStatCost(sheet, stat),
                sheet.Stats[(int)stat]);
        }
        var stats = new AreaStatsPageView(rows, sheet.UnspentPoints, false);
        var intelligence = AutoLevelRules.EffectiveStat(sheet, modifiers, CharacterStat.Intelligence);
        var tech = new AreaTechPageView([.. Enumerable.Range(0, AreaCharacterSheetPagesLayout.Disciplines).Select(discipline =>
        {
            var degree = TechDegreeRules.Degree(sheet, discipline);
            return new AreaTechRow(discipline, degree, TechDegreeRules.CountingDegree(degree, intelligence),
                TechDegreeRules.EffectiveExpertise(degree, intelligence, []), degree);
        })], false);
        return new(entity, view, skills, stats, tech, sheet, modifiers);
    }

    /// <summary>The mark of a stat's colour (0x55B2A0): the effects' changes of the base value, green above it, red below.</summary>
    private static AreaSheetMark Mark(AdvancementSheet sheet, AdvancementModifiers modifiers, CharacterStat stat)
    {
        var value = sheet.Stats[(int)stat];
        var adjusted = EffectModifiers.Apply(value, modifiers.Stats.Where(m => m.Stat == stat).Select(m => new EffectModifier(m.Operator, m.Value)).ToArray());
        return adjusted > value ? AreaSheetMark.Raised : adjusted < value ? AreaSheetMark.Lowered : AreaSheetMark.None;
    }
}
