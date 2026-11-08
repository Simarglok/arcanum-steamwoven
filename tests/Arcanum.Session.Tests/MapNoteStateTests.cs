using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-UI-110 (Steamwoven rules, schema 82): the notes of the world map's map view and continent view. Numbers and texts are invented.</summary>
[TestClass]
public sealed class MapNoteStateTests
{
    private const int MapView = SessionStoryLimits.MapNoteView, ContinentView = SessionStoryLimits.ContinentNoteView;
    private static SessionTownNote Note(int id, string text = "Camp", int x = 1200, int y = 800) => new(id, 2, x, y, 3, text);

    [TestMethod]
    public void Notes_are_kept_per_view_and_the_same_list_changes_nothing()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryMapNotes(MapView));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordMapNotes(owner.Version, MapView, 202, [Note(200), Note(201, "Pass", 1250, 820)]).Disposition);
        Assert.AreEqual(1L, owner.Version.Revision, "one revision");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordMapNotes(owner.Version, MapView, 202, [Note(200), Note(201, "Pass", 1250, 820)]).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordMapNotes(owner.Version, MapView, 203, [Note(200), Note(201, "Pass", 1250, 820), Note(202, "Ford")]).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordMapNotes(owner.Version, ContinentView, 201, [Note(200, "Sea", 40, 30)]).Disposition, "the other view keeps its own list");
        Assert.AreEqual(3, owner.QueryMapNotes(MapView)!.Notes.Count);
        Assert.AreEqual(203, owner.QueryMapNotes(MapView)!.NextId);
        Assert.AreEqual("Sea", owner.QueryMapNotes(ContinentView)!.Notes[0].Text);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordMapNotes(owner.Version, ContinentView, 201, []).Disposition, "an empty list removes the view's entry");
        Assert.IsNull(owner.QueryMapNotes(ContinentView));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordMapNotes(owner.Version, ContinentView, 201, []).Disposition);
        Assert.IsNull(owner.QueryTownNotes(1), "the towns' notes are a separate list");
    }

    [TestMethod]
    public void A_list_outside_the_limits_or_not_canonical_is_refused()
    {
        using var owner = Create();
        foreach (var (view, next, notes) in new (int, int, SessionTownNote[])[]
        {
            (0, 202, [Note(200)]),
            (SessionStoryLimits.LastMapNoteView + 1, 202, [Note(200)]),
            (MapView, 200, [Note(200)]),
            (MapView, 201, [Note(200), Note(201)]),
            (MapView, 205, [Note(201), Note(200)]),
            (MapView, 205, [Note(199)]),
            (MapView, 205, [Note(200, new string('x', 250))]),
            (MapView, 205, [Note(200, "tab\there")]),
            (MapView, 205, [Note(200, "Camp", -1)]),
            (MapView, 205, [Note(200, "Camp", 1200, SessionStoryLimits.MaxMapNotePixel + 1)]),
            (MapView, 205, [new(200, 2, 1, 1, 8, "Camp")]),
            (MapView, 205, [new(200, -1, 1, 1, 3, "Camp")]),
            (MapView, 500, Enumerable.Range(200, SessionStoryLimits.MaxTownNotes + 1).Select(i => Note(i)).ToArray()),
        })
            Assert.AreEqual(GameSessionFailure.InvalidMapNotes, owner.RecordMapNotes(owner.Version, view, next, notes).Failure, $"{view}/{next}/{notes.Length}");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RecordMapNotes(owner.Version with { Revision = 9 }, MapView, 201, [Note(200)]).Failure);
        Assert.AreEqual(0L, owner.Version.Revision);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordMapNotes(owner.Version, MapView, 200 + SessionStoryLimits.MaxTownNotes, Enumerable.Range(200, SessionStoryLimits.MaxTownNotes).Select(i => Note(i)).ToArray()).Disposition, "199 notes fit");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordMapNotes(owner.Version, ContinentView, 201, [Note(200, "Edge", SessionStoryLimits.MaxMapNotePixel, SessionStoryLimits.MaxMapNotePixel)]).Disposition, "the picture's last pixel fits");
    }

    [TestMethod]
    public void The_notes_survive_the_snapshot_and_a_damaged_list_is_refused()
    {
        using var owner = Create();
        owner.RecordMapNotes(owner.Version, ContinentView, 203, [Note(200, "Port \"North\"", 640, 320), Note(202, "Smith’s", 12, 34)]);
        owner.RecordMapNotes(owner.Version, MapView, 201, [Note(200, "Gate")]);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"mapNotes\":[{\"view\":1,\"nextId\":201,\"notes\":[{\"id\":200,\"flags\":2,\"x\":1200,\"y\":800,\"type\":3,\"text\":\"Gate\"}]},{\"view\":2,\"nextId\":203,");
        var snapshot = Decode(text);
        Assert.AreEqual(2, snapshot.Story.MapNotes.Count);
        using var restored = Restore(snapshot);
        Assert.AreEqual("Smith’s", restored.QueryMapNotes(ContinentView)!.Notes[1].Text);
        Assert.AreEqual(203, restored.QueryMapNotes(ContinentView)!.NextId);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        const string Gate = "\"id\":200,\"flags\":2,\"x\":1200,\"y\":800,\"type\":3,\"text\":\"Gate\"";
        Fails(text.Replace("\"view\":1,", "\"view\":2,"), SnapshotDecodeFailure.NotCanonical, "$.story.mapNotes[1].view");
        Fails(text.Replace("\"view\":1,", "\"view\":0,"), SnapshotDecodeFailure.OutOfRange, "$.story.mapNotes[0].view");
        Fails(text.Replace("\"view\":2,", "\"view\":3,"), SnapshotDecodeFailure.OutOfRange, "$.story.mapNotes[1].view");
        Fails(text.Replace("\"nextId\":201", "\"nextId\":200"), SnapshotDecodeFailure.OutOfRange, "$.story.mapNotes[0].nextId");
        Fails(text.Replace(Gate, Gate.Replace("\"id\":200", "\"id\":201")), SnapshotDecodeFailure.NotCanonical, "$.story.mapNotes[0].notes[0].id");
        Fails(text.Replace("\"id\":202", "\"id\":200"), SnapshotDecodeFailure.NotCanonical, "$.story.mapNotes[1].notes[1].id");
        Fails(text.Replace(Gate, Gate.Replace("\"id\":200", "\"id\":199")), SnapshotDecodeFailure.OutOfRange, "$.story.mapNotes[0].notes[0].id");
        Fails(text.Replace(Gate, Gate.Replace("\"x\":1200", "\"x\":4097")), SnapshotDecodeFailure.OutOfRange, "$.story.mapNotes[0].notes[0].x");
        Fails(text.Replace("\"text\":\"Gate\"", "\"text\":\"" + new string('x', 250) + "\""), SnapshotDecodeFailure.OutOfRange, "$.story.mapNotes[0].notes[0].text");
        Fails(text.Replace("\"text\":\"Gate\"", "\"text\":\"a\\nb\""), SnapshotDecodeFailure.OutOfRange, "$.story.mapNotes[0].notes[0].text");
        Fails(text.Replace("\"type\":3,\"text\":\"Gate\"", "\"type\":8,\"text\":\"Gate\""), SnapshotDecodeFailure.OutOfRange, "$.story.mapNotes[0].notes[0].type");
        Fails(text.Replace("\"text\":\"Gate\"", "\"text\":5"), SnapshotDecodeFailure.WrongType, "$.story.mapNotes[0].notes[0].text");
        Fails(text.Replace(",\"text\":\"Gate\"", ""), SnapshotDecodeFailure.MissingField, "$.story.mapNotes[0].notes[0].text");
        Fails(text.Replace("\"notes\":[{" + Gate + "}]", "\"notes\":[]"), SnapshotDecodeFailure.NotCanonical, "$.story.mapNotes[0].notes");
        Fails(text.Replace("\"mapNotes\":", "\"mapNotez\":"), SnapshotDecodeFailure.UnknownField, "$.story.mapNotez");
    }

    [TestMethod]
    public void The_eighty_one_to_eighty_two_step_adds_the_empty_list_and_refuses_one_that_is_already_there()
    {
        var older = SnapshotUpgradeTests.Schema81(GoldenJson);
        StringAssert.Contains(older, "\"schemaVersion\":81,\"world\"");
        Assert.IsFalse(older.Contains("mapNotes", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(0, upgraded.Story.MapNotes.Count, "no older build kept a note");
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(upgraded));
        // A schema 81 text that already carries the list is damage: the step adds it, never merges.
        var damaged = older.Replace(NoTownNotes + "}", NoTownNotes + ",\"mapNotes\":[]}");
        var result = GameSessionSnapshotCodec.TryDecode(damaged);
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.story.mapNotes"), (result.Failure, result.Path));
    }
}
