using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-034 (N10b slice 3): the character editor in creation mode, the original's window 14 that the new-character window's Accept
// opens (ledger CM1–CM8). It is the character sheet in its creation mode (AreaCharacterSheetPanel with Accept and Cancel) above the
// main menu's backdrop, over a draft session of the new character (AreaCreationEditor): the stats, skills and spells pages' + and −
// spend the five starting points and take them back down to the sheet the editor opened on. Accept (or Return) starts the game with
// the presses replayed on the real session (the opening shop the original goes on to is not built); Cancel (or Escape) goes back to
// the new-character window, which starts a new character as the original's does. The menu owns the keys; the pages' buttons take
// the pointer.
public partial class TerrainViewerApp
{
    private AreaCharacterSheetPanel? _editorPanel;
    private int _editorCollege;

    private bool CharacterEditorShown => _editorPanel?.IsOpen == true;

    /// <summary>A draft session of <paramref name="creation"/>: made as the start makes the new character's session (the scene's
    /// new-game recipe, the window's name and background); null with the reason when it cannot be made.</summary>
    private GameSessionOwner? MakeCreationDraft(AreaNewCharacter creation, out string? refused)
    {
        refused = null;
        if (creation.Hero(out var why) is not { Status: AreaHeroStatus.Ready } hero) { refused = "the new character cannot be made (" + why + ")"; return null; }
        if (_newGameRecipe is not { } recipe) { refused = "this scene has no new-game start to make the character's session from"; return null; }
        var created = AreaTransitionCoordinator.CreateSession(recipe.Clock, Guid.NewGuid(), recipe.Module, recipe.Spawn, recipe.RuntimeMap, hero, recipe.Reputations);
        if (created.Owner is not { } draft) { refused = $"the session was not created ({created.Failure})"; return null; }
        if (creation.ApplyChoices(draft, hero.Effects ?? (_ => null)) is { } choices) { draft.Dispose(); refused = choices; return null; }
        return draft;
    }

    /// <summary>Shows the editor while the menu is at window 14 (rebuilt from the draft after every change), hides it otherwise.</summary>
    private void RenderCharacterEditor()
    {
        if (_mainMenu is not { Window: AreaMenuWindow.CharacterEditor, QuitPending: false, Editor: { Disposed: false } editor } menu) { CloseCharacterEditor(); return; }
        if (editor.Sheet() is not { } view) { CloseCharacterEditor(); return; }
        if (_editorPanel is null)
        {
            _editorPanel = new AreaCharacterSheetPanel(creation: true); AddChild(_editorPanel);
            _editorPanel.AcceptRequested += () => MainMenuCommand(() => _mainMenu!.Accept());
            _editorPanel.CloseRequested += () => MainMenuCommand(() => { _mainMenu!.Back(); return AreaMenuCommand.Nothing; });
            _editorPanel.StatSpendRequested += row => PressEditor(e => e.PressStat(row, spend: true).Message, $"stat spend row={row}");
            _editorPanel.StatRefundRequested += row => PressEditor(e => e.PressStat(row, spend: false).Message, $"stat refund row={row}");
            _editorPanel.SkillSpendRequested += row => PressEditor(e => e.PressSkill(row, spend: true).Message, $"skill spend row={row}");
            _editorPanel.SkillRefundRequested += row => PressEditor(e => e.PressSkill(row, spend: false).Message, $"skill refund row={row}");
            _editorPanel.SpellLearnRequested += college => PressEditor(e => e.PressSpell(college, learn: true, _spellData?.Data).Message, $"spell learn college={college}");
            _editorPanel.SpellUnlearnRequested += college => PressEditor(e => e.PressSpell(college, learn: false, _spellData?.Data).Message, $"spell unlearn college={college}");
            _editorPanel.CollegeSelected += college =>
            {
                if (college is < 0 or >= AreaSpellsPage.Colleges) return;
                _editorCollege = college; RenderCharacterEditor();
            };
        }
        AdoptSkillTexts(); AdoptSpellData();
        var skills = editor.Skills(_skillTexts?.Texts, AreaHoverIndex.TextProfile);
        var spells = editor.Spells(_spellData?.Data, _editorCollege);
        if (!_editorPanel.IsOpen) _editorPanel.Open(view, skills, spells, editor.Stats());
        else _editorPanel.Render(view, skills, spells, editor.Stats());
        if (menu.Notice is { } notice) _editorPanel.ShowSkillMessage(notice);
    }

    private void CloseCharacterEditor()
    {
        if (_editorPanel?.IsOpen != true) return;
        _editorPanel.CloseView();
    }

    /// <summary>One + or − press of the editor: the draft changes (and the press is recorded) or the refusal is the message.</summary>
    private void PressEditor(Func<AreaCreationEditor, string> press, string what)
    {
        if (_mainMenu is not { Window: AreaMenuWindow.CharacterEditor, QuitPending: false, Editor: { Disposed: false } editor }) return;
        var message = press(editor);
        _mainMenu.Notice = null;
        GD.Print($"ARC-UI-034 editor {what}: {message} presses={editor.Creation.Presses.Count} unspent={editor.Draft.QueryHeroExperience()?.UnspentPoints}");
        RenderCharacterEditor();
        _editorPanel!.ShowSkillMessage(message);
    }
}
