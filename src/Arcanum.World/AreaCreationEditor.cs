using Arcanum.Data.Magic;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;

namespace Arcanum.World;

/// <summary>One press of the character editor in creation mode that changed the new character: its track and index, spend or
/// refund, and the refund's click (a basic skill's; the other tracks ignore it).</summary>
public readonly record struct AreaCreationPress(AdvancementTrack Track, int Index, bool Spend,
    SkillRefundClick Click = SkillRefundClick.UntilLevelChanges);

/// <summary>
/// ARC-UI-034 (N10b slice 3, FR-CHAR-001, FR-CHAR-015, FR-CHAR-020): the original's character editor in creation mode, the window
/// (14) that the new-character window's Accept opens, Godot-free. Read in the retail 1.0.7.4 executable (ledger "Character editor
/// in creation mode", CM1–CM8): window 14 has no art, two buttons (Accept, Cancel) and no execute; its refresh opens the ordinary
/// character editor (0x5597C0) on the new PC in mode 0 (creation; mode 3 on a later opening), whose open takes the snapshot every
/// refund stops at, so the five starting points can be spent and taken back on the stats, skills and spells pages while the window
/// stays open. Accept (or Return) goes on to the opening shop (window 15, ARC-UI-036: <see cref="AreaOpeningKit"/>), whose Back
/// returns here with the draft and its floor unchanged (the original's mode-3 reopening, CM4); Cancel (or Escape) goes back to
/// window 12, whose opening makes a new PC (NW1).
/// Steamwoven makes the PC's session when Accept starts the game, possibly after the start scene loaded (ARC-UI-012). So the editor
/// works on a draft: a session made from the window's character exactly as the start makes it (<see cref="AreaNewCharacter.ApplyChoices"/>),
/// with its editor open; each press that changed it is recorded in <see cref="AreaNewCharacter.Presses"/>, and the start replays
/// them on the real session (<see cref="AreaNewCharacter.Apply"/>) through the same point pool, from the same sheet and floor.
/// Disposing the editor disposes the draft. Caller-serialized.
/// </summary>
public sealed class AreaCreationEditor : IDisposable
{
    /// <param name="creation">The new-character window's character, with no presses recorded yet.</param>
    /// <param name="draft">A session made from <see cref="AreaNewCharacter.Hero"/> with <see cref="AreaNewCharacter.ApplyChoices"/>
    /// applied; the editor owns it from now on (a refused draft is disposed before the constructor throws).</param>
    public AreaCreationEditor(AreaNewCharacter creation, GameSessionOwner draft)
    {
        Creation = creation ?? throw new ArgumentNullException(nameof(creation));
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        if (creation.Presses.Count != 0) { draft.Dispose(); throw new ArgumentException("the character already has editor presses", nameof(creation)); }
        // The original's creation-mode open (0x5597C0, mode 0): the snapshot of the new PC, the floor of every refund (CP1, CM3).
        var opened = draft.OpenHeroEditor();
        if (opened.Disposition != GameSessionDisposition.Changed)
        {
            draft.Dispose();
            throw new ArgumentException("the draft's editor did not open: " + opened.Failure, nameof(draft));
        }
    }

    public AreaNewCharacter Creation { get; }
    /// <summary>The draft session the pages are built from and the presses act on.</summary>
    public GameSessionOwner Draft { get; }
    public bool Disposed { get; private set; }

    /// <summary>The sheet of the draft's hero (the values the presses change), or null when it does not show.</summary>
    public AreaCharacterSheetView? Sheet() => AreaCharacterSheet.Build(Draft).View;

    public AreaStatsPageView? Stats() => AreaStatsPage.Build(Draft);

    public AreaSkillsPageView? Skills(SkillTexts? texts, MesTextProfile profile) => AreaSkillsPage.Build(Draft, texts, profile);

    public AreaSpellsPageView? Spells(SpellData? data, int college) => AreaSpellsPage.Build(Draft, data, college);

    /// <summary>+ (<paramref name="spend"/>) or − on a stats row (<see cref="AreaStatsPage"/>).</summary>
    public AreaStatPress PressStat(int row, bool spend)
    {
        var press = spend ? AreaStatsPage.Spend(Draft, row) : AreaStatsPage.Refund(Draft, row);
        var (track, index) = AreaStatsPage.Track(row);
        if (press.Changed) Creation.Record(new(track, index, spend));
        return press;
    }

    /// <summary>+ (<paramref name="spend"/>) or − on a skills row (<see cref="AreaSkillsPage"/>, the original's click).</summary>
    public AreaSkillPress PressSkill(int row, bool spend)
    {
        var press = spend ? AreaSkillsPage.Spend(Draft, row) : AreaSkillsPage.Refund(Draft, row);
        var (track, index) = AreaSkillsPage.Track(row);
        if (press.Changed) Creation.Record(new(track, index, spend, AreaSkillsPage.RefundClick));
        return press;
    }

    /// <summary>+ (<paramref name="learn"/>) or − of a college on the spells page (<see cref="AreaSpellsPage"/>).</summary>
    public AreaSpellPress PressSpell(int college, bool learn, SpellData? data)
    {
        var press = learn ? AreaSpellsPage.Learn(Draft, college, data) : AreaSpellsPage.Unlearn(Draft, college, data);
        if (press.Changed) Creation.Record(new(AdvancementTrack.College, college, learn));
        return press;
    }

    /// <summary>
    /// ARC-MAGIC-043: + (<paramref name="spend"/>) or − of a technological discipline's degree on the tech page: the pool's technology
    /// lane on the draft (the original's degree page of the same editor, CM8; refused below the snapshot the editor opened on, TC10),
    /// recorded when it changed the character. The caller words the result (<c>AreaTechPage</c>).
    /// </summary>
    public SessionPointResult PressTech(int discipline, bool spend)
    {
        var result = spend ? Draft.SpendHeroPoint(Draft.Version, AdvancementTrack.TechDiscipline, discipline)
            : Draft.RefundHeroPoint(Draft.Version, AdvancementTrack.TechDiscipline, discipline, AreaSkillsPage.RefundClick);
        if (result.Disposition == GameSessionDisposition.Changed) Creation.Record(new(AdvancementTrack.TechDiscipline, discipline, spend));
        return result;
    }

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        Draft.Dispose();
    }
}
