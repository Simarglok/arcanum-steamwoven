using Arcanum.TerrainViewer.Core;
using Arcanum.Presentation.Hud;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-2306: the original's map note entry (bottom strip bar mode 6, ledger BM0, BM4; exe 0x564030, 0x564070, 0x5640C0, 0x564000, 0x5672A0, 0x5672D0, 0x567320, text
// entry 0x566F00). The quill of the world map window (Steamwoven rules: the retail executable has no way to enter its state, WM126) starts a note; the text is typed on
// the strip: art 200, the 50-character entry, Done (Enter), Clear, Cancel and the radio group of the marker. The bars stay up while the map is open, as the window lies
// between the strips in the original's screen. Escape leaves the entry (the menu does not open over it).
public partial class TerrainViewerApp
{
    private readonly MapNoteEntry _mapNote = new();
    private AreaMapNoteEntryView? _mapNoteView;
    private (object? Art, object? Font)? _mapNoteShown;
    private int _mapNoteShownRevision = -1;
    private int _mapNoteReleasesForSelfTest;

    internal MapNoteEntry MapNoteForSelfTest => _mapNote;
    internal AreaMapNoteEntryView? MapNoteViewForSelfTest => _mapNoteView is { Visible: true } ? _mapNoteView : null;
    internal int MapNoteReleasesForSelfTest => _mapNoteReleasesForSelfTest;

    // The entry can run when the strip's art and the marker font read (the bars themselves are up while the map is open).
    private bool MapNoteReady() => _worldMapArtLook?.Font is not null && Art(MapNoteEntry.BackgroundArt) is { Length: > 0 };

    /// <summary>Per frame while the bars show: the entry over the strip while the map window's quill has a note open; the entry follows the window's (opened by a press on
    /// the map, closed by the accept, Escape, a right press or the window going away).</summary>
    private void SyncMapNoteEntry()
    {
        if (_gameBarPanel is null) return;
        if (_mapNoteView is null)
        {
            _mapNoteView = new AreaMapNoteEntryView { Visible = false };
            _gameBarPanel.AddPart(AreaBarStrip.Bottom, _mapNoteView);
            _mapNoteView.ButtonPressed += _ => PlayBarSound(AreaGameBarLayout.PressSound, "note entry button");
            _mapNoteView.ButtonReleased += MapNoteButtonReleased;
            _mapNoteView.MarkerPressed += _ => PlayBarSound(AreaGameBarLayout.PressSound, "note entry marker");
            _mapNoteView.MarkerReleased += MapNoteMarkerReleased;
        }
        var panel = _worldMapPanel;
        var ready = MapNoteReady();
        if (panel is not null)
        {
            panel.NoteEntryAvailable = ready && WorldMapOpen;
            panel.NoteMarkerType = _mapNote.Marker;
        }
        var wanted = WorldMapOpen && panel is { EditingNote: true };
        if (wanted && !ready) { panel!.CancelNoteEdit(); wanted = false; }
        if (wanted && !_mapNote.IsOpen) OpenMapNote();
        else if (!wanted && _mapNote.IsOpen) { _mapNote.Close(); GD.Print("ARC-UI-2306 note entry closed"); }
        var shown = _mapNote.IsOpen;
        if (_mapNoteView.Visible != shown) _mapNoteView.Visible = shown;
        if (!shown) return;
        var font = _worldMapArtLook!.Font;
        var stamp = ((object?)_hotkeyBarArt, (object?)font);
        if (_mapNoteShown != stamp)
        {
            _mapNoteShown = stamp;
            _mapNoteView.Bind(new(First(Art(MapNoteEntry.BackgroundArt)), [.. MapNoteEntry.Buttons.Select(b => Art(b.Art))], [.. MapNoteEntry.Markers.Select(m => Art(m.Art))], font));
            _mapNoteShownRevision = -1;
        }
        if (_mapNoteShownRevision != _mapNote.Revision)
        {
            _mapNoteShownRevision = _mapNote.Revision;
            _mapNoteView.SetState(_mapNote.Text, _mapNote.Marker);
            panel!.SetNoteText(_mapNote.Text);
        }
    }

    private void OpenMapNote()
    {
        _mapNote.Open(_worldMapPanel!.NoteEntryOpenedText);
        GD.Print($"ARC-UI-2306 note entry opened text=\"{_mapNote.Text}\" marker={_mapNote.Marker}");
    }

    /// <summary>The smoke's typing: the open entry gets <paramref name="text"/> in place of its own, as the keys would type it.</summary>
    internal void TypeMapNoteForSelfTest(string text)
    {
        if (!_mapNote.IsOpen) OpenMapNote();
        _mapNote.Clear();
        foreach (var c in text) _mapNote.Type(c);
        _worldMapPanel!.SetNoteText(_mapNote.Text);
    }

    /// <summary>The bars went away (the menu, a window): the entry ends as Escape does, and its pane goes.</summary>
    private void HideMapNoteEntry()
    {
        if (_worldMapPanel is { EditingNote: true }) _worldMapPanel.CancelNoteEdit();
        if (_mapNote.Close()) GD.Print("ARC-UI-2306 note entry closed: the bars went away");
        if (_mapNoteView is not null) _mapNoteView.Visible = false;
        if (_worldMapPanel is not null) _worldMapPanel.NoteEntryAvailable = false;
    }

    /// <summary>A key while the window's note entry is open: Enter accepts (0x5640C0), Escape leaves, the rest is the entry's (0x566F00). The entry owns every key; true always.</summary>
    private bool HandleMapNoteKey(InputEventKey key)
    {
        var panel = _worldMapPanel!;
        var code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (!key.Pressed) return true;
        if (!_mapNote.IsOpen) OpenMapNote();
        if (code is Key.Enter or Key.KpEnter) { if (!key.Echo) AcceptMapNote(); return true; }
        if (code == Key.Escape) { if (!key.Echo) { panel.SetNoteText(_mapNote.Text); panel.CancelNoteEdit(); } return true; }
        AreaStripEntryKeys.Edit(key, _mapNote.Type, _mapNote.Press);
        panel.SetNoteText(_mapNote.Text);
        return true;
    }

    /// <summary>Enter or the Done button (0x567320 → 0x5640C0): the typed text goes to the note list, the entry ends.</summary>
    private void AcceptMapNote()
    {
        var panel = _worldMapPanel!;
        panel.SetNoteText(_mapNote.Text);
        panel.AcceptNoteEdit();
        GD.Print($"ARC-UI-2306 note entry accepted text=\"{_mapNote.Text}\"");
        _mapNote.Close();
    }

    private void MapNoteButtonReleased(MapNoteAction action, bool over)
    {
        if (!over || !_mapNote.IsOpen || _worldMapPanel is not { EditingNote: true }) return;
        PlayBarSound(AreaGameBarLayout.ReleaseSound, "note entry button");
        _mapNoteReleasesForSelfTest++;
        GD.Print($"ARC-UI-2306 note entry button={action}");
        switch (action)
        {
            case MapNoteAction.Done: AcceptMapNote(); break;
            case MapNoteAction.Clear: _mapNote.Clear(); _worldMapPanel.SetNoteText(_mapNote.Text); break;
            case MapNoteAction.Cancel: _mapNote.Cancel(); _worldMapPanel.SetNoteText(_mapNote.Text); break;
        }
    }

    private void MapNoteMarkerReleased(int type, bool over)
    {
        if (!over || !_mapNote.IsOpen) return;
        PlayBarSound(AreaGameBarLayout.ReleaseSound, "note entry marker");
        _mapNoteReleasesForSelfTest++;
        if (_mapNote.Select(type)) GD.Print($"ARC-UI-2306 note entry marker={type}");
    }
}
