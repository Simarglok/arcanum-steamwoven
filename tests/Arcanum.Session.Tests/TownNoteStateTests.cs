using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-UI-104 (ledger WM91, WM127, WM128, schema 80): the notes of a town map. Numbers and texts are invented.</summary>
[TestClass]
public sealed class TownNoteStateTests
{
    private static SessionTownNote Note(int id, string text = "Inn", int x = 640, int y = 320) => new(id, 2, x, y, 3, text);

    [TestMethod]
    public void Notes_are_kept_per_town_and_the_same_list_changes_nothing()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryTownNotes(7));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordTownNotes(owner.Version, 7, 202, [Note(200), Note(201, "Well", 650, 325)]).Disposition);
        Assert.AreEqual(1L, owner.Version.Revision, "one revision");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordTownNotes(owner.Version, 7, 202, [Note(200), Note(201, "Well", 650, 325)]).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordTownNotes(owner.Version, 7, 203, [Note(200), Note(201, "Well", 650, 325), Note(202, "Gate")]).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordTownNotes(owner.Version, 3, 201, [Note(200)]).Disposition, "another town keeps its own list");
        Assert.AreEqual(3, owner.QueryTownNotes(7)!.Notes.Count);
        Assert.AreEqual(203, owner.QueryTownNotes(7)!.NextId);
        Assert.AreEqual("Inn", owner.QueryTownNotes(3)!.Notes[0].Text);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordTownNotes(owner.Version, 3, 201, []).Disposition, "an empty list removes the town's entry");
        Assert.IsNull(owner.QueryTownNotes(3));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordTownNotes(owner.Version, 3, 201, []).Disposition);
    }

    [TestMethod]
    public void A_list_outside_the_limits_or_not_canonical_is_refused()
    {
        using var owner = Create();
        foreach (var (town, next, notes) in new (int, int, SessionTownNote[])[]
        {
            (0, 202, [Note(200)]),
            (SessionStoryLimits.LastTownMap + 1, 202, [Note(200)]),
            (7, 200, [Note(200)]),
            (7, 201, [Note(200), Note(201)]),
            (7, 205, [Note(201), Note(200)]),
            (7, 205, [Note(199)]),
            (7, 205, [Note(200, new string('x', 250))]),
            (7, 205, [Note(200, "tab\there")]),
            (7, 205, [Note(200, "Inn", -1)]),
            (7, 205, [Note(200, "Inn", 640, SessionStoryLimits.MaxTownNotePixel + 1)]),
            (7, 205, [new(200, 2, 1, 1, 8, "Inn")]),
            (7, 205, [new(200, -1, 1, 1, 3, "Inn")]),
            (7, 500, Enumerable.Range(200, SessionStoryLimits.MaxTownNotes + 1).Select(i => Note(i)).ToArray()),
        })
            Assert.AreEqual(GameSessionFailure.InvalidTownMap, owner.RecordTownNotes(owner.Version, town, next, notes).Failure, $"{town}/{next}/{notes.Length}");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RecordTownNotes(owner.Version with { Revision = 9 }, 7, 201, [Note(200)]).Failure);
        Assert.AreEqual(0L, owner.Version.Revision);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordTownNotes(owner.Version, 7, 200 + SessionStoryLimits.MaxTownNotes, Enumerable.Range(200, SessionStoryLimits.MaxTownNotes).Select(i => Note(i)).ToArray()).Disposition, "199 notes fit");
    }

    [TestMethod]
    public void The_notes_survive_the_snapshot_and_a_damaged_list_is_refused()
    {
        using var owner = Create();
        owner.RecordTownNotes(owner.Version, 12, 203, [Note(200, "Inn \"The Rest\"", 640, 320), Note(202, "Smith’s", 12, 34)]);
        owner.RecordTownNotes(owner.Version, 3, 201, [Note(200, "Gate")]);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"townNotes\":[{\"townMap\":3,\"nextId\":201,\"notes\":[{\"id\":200,\"flags\":2,\"x\":640,\"y\":320,\"type\":3,\"text\":\"Gate\"}]},{\"townMap\":12,\"nextId\":203,");
        var snapshot = Decode(text);
        Assert.AreEqual(2, snapshot.Story.TownNotes.Count);
        using var restored = Restore(snapshot);
        Assert.AreEqual("Smith’s", restored.QueryTownNotes(12)!.Notes[1].Text);
        Assert.AreEqual(203, restored.QueryTownNotes(12)!.NextId);
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");

        void Fails(string damaged, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(damaged);
            Assert.AreEqual((failure, path), (result.Failure, result.Path));
        }
        Fails(text.Replace("\"townMap\":3,", "\"townMap\":12,"), SnapshotDecodeFailure.NotCanonical, "$.story.townNotes[1].townMap");
        Fails(text.Replace("\"townMap\":3,", "\"townMap\":0,"), SnapshotDecodeFailure.OutOfRange, "$.story.townNotes[0].townMap");
        Fails(text.Replace("\"nextId\":201", "\"nextId\":200"), SnapshotDecodeFailure.OutOfRange, "$.story.townNotes[0].nextId");
        Fails(text.Replace("\"id\":200,\"flags\":2,\"x\":640,\"y\":320,\"type\":3,\"text\":\"Gate\"", "\"id\":201,\"flags\":2,\"x\":640,\"y\":320,\"type\":3,\"text\":\"Gate\""), SnapshotDecodeFailure.NotCanonical, "$.story.townNotes[0].notes[0].id");
        Fails(text.Replace("\"id\":202", "\"id\":200"), SnapshotDecodeFailure.NotCanonical, "$.story.townNotes[1].notes[1].id");
        Fails(text.Replace("\"id\":200,\"flags\":2,\"x\":640,\"y\":320,\"type\":3,\"text\":\"Gate\"", "\"id\":199,\"flags\":2,\"x\":640,\"y\":320,\"type\":3,\"text\":\"Gate\""), SnapshotDecodeFailure.OutOfRange, "$.story.townNotes[0].notes[0].id");
        Fails(text.Replace("\"text\":\"Gate\"", "\"text\":\"" + new string('x', 250) + "\""), SnapshotDecodeFailure.OutOfRange, "$.story.townNotes[0].notes[0].text");
        Fails(text.Replace("\"text\":\"Gate\"", "\"text\":\"a\\nb\""), SnapshotDecodeFailure.OutOfRange, "$.story.townNotes[0].notes[0].text");
        Fails(text.Replace("\"type\":3,\"text\":\"Gate\"", "\"type\":8,\"text\":\"Gate\""), SnapshotDecodeFailure.OutOfRange, "$.story.townNotes[0].notes[0].type");
        Fails(text.Replace("\"text\":\"Gate\"", "\"text\":5"), SnapshotDecodeFailure.WrongType, "$.story.townNotes[0].notes[0].text");
        Fails(text.Replace(",\"text\":\"Gate\"", ""), SnapshotDecodeFailure.MissingField, "$.story.townNotes[0].notes[0].text");
        Fails(text.Replace("\"notes\":[{\"id\":200,\"flags\":2,\"x\":640,\"y\":320,\"type\":3,\"text\":\"Gate\"}]", "\"notes\":[]"), SnapshotDecodeFailure.NotCanonical, "$.story.townNotes[0].notes");
        Fails(text.Replace("\"townNotes\":", "\"townNotez\":"), SnapshotDecodeFailure.UnknownField, "$.story.townNotez");
    }

    [TestMethod]
    public void The_seventy_nine_to_eighty_step_adds_the_empty_list_and_refuses_one_that_is_already_there()
    {
        var older = SnapshotUpgradeTests.Schema79(GoldenJson);
        StringAssert.Contains(older, "\"schemaVersion\":79,\"world\"");
        Assert.IsFalse(older.Contains("townNotes", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(0, upgraded.Story.TownNotes.Count, "no older build kept a note");
        Assert.AreEqual(GoldenJson, GameSessionSnapshotCodec.Encode(upgraded));
        // A schema 79 text that already carries the list is damage: the step adds it, never merges.
        var damaged = older.Replace(NoKillLog + "}", NoKillLog + ",\"townNotes\":[]}");
        var result = GameSessionSnapshotCodec.TryDecode(damaged);
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.story.townNotes"), (result.Failure, result.Path));
    }

    [TestMethod]
    public void At_most_the_limit_of_towns_hold_notes()
    {
        using var owner = Create();
        for (var town = 1; town <= SessionStoryLimits.MaxTownMapsWithCells && town <= SessionStoryLimits.LastTownMap; town++)
            Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordTownNotes(owner.Version, town, 201, [Note(200)]).Disposition, $"town {town}");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordTownNotes(owner.Version, 1, 202, [Note(200), Note(201, "More", 1, 1)]).Disposition, "a town that holds notes takes more");
    }
}
