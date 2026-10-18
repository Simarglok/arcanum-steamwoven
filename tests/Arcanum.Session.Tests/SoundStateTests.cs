using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// Music and ambient scheme slots in the session (ARC-WORLD-040). Expected states are written from the behaviour recorded in
// docs/research/audio-evidence.md SN10–SN12, SN15, SN17–SN20 (the original's scheme call, load, stop, combat music, save and
// load, map open, sector change); scheme ids are invented.
[TestClass]
public sealed class SoundStateTests
{
    private static readonly SessionCombatant Wolf = SessionCombatant.Critter("oid:wolf");

    private sealed class Schemes(params int[] silent) : ISessionSoundSchemes
    {
        public int Transition { get; init; } = -1;
        public bool Loads(int schemeId) => !silent.Contains(schemeId);
        public bool StartsTransition(int schemeId) => schemeId == Transition;
    }

    private static GameSessionOwner Owner(bool hero = false)
    {
        var result = GameSessionOwner.Create(hero ? Descriptor() with { Hero = HalfElf() } : Descriptor());
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionSoundState State(GameSessionOwner owner) => owner.QuerySound().State;

    private static void Changed(SessionSoundResult result, GameSessionOwner owner)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(owner.Version, result.Version);
    }

    [TestMethod]
    public void A_request_keeps_active_schemes_stops_the_rest_and_fills_free_slots()
    {
        using var owner = Owner();
        Assert.AreEqual(SessionSoundState.Empty, State(owner));
        var revision = owner.Version.Revision;
        Changed(owner.PlaySoundScheme(owner.Version, 1, 40), owner);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        Assert.AreEqual((1, 40, 0, 0), (State(owner).Slot0, State(owner).Slot1, State(owner).Restore0, State(owner).Restore1));

        Assert.AreEqual(GameSessionDisposition.NoChange, owner.PlaySoundScheme(owner.Version, 1, 40).Disposition, "both active: nothing");
        Changed(owner.PlaySoundScheme(owner.Version, 1, 41), owner);
        Assert.AreEqual((1, 41, 40), (State(owner).Slot0, State(owner).Slot1, State(owner).Restore1), "music kept, the other slot stopped and refilled");
        Changed(owner.PlaySoundScheme(owner.Version, 2, 41), owner);
        Assert.AreEqual((2, 41, 1), (State(owner).Slot0, State(owner).Slot1, State(owner).Restore0), "ambient kept in slot 1, music loads into the freed slot 0");
        Changed(owner.PlaySoundScheme(owner.Version, 5, 5), owner);
        Assert.AreEqual((5, 0), (State(owner).Slot0, State(owner).Slot1), "an equal ambient becomes 0; neither active stops both");
        Changed(owner.PlaySoundScheme(owner.Version, 0, 0), owner);
        Assert.AreEqual((0, 0), (State(owner).Slot0, State(owner).Slot1), "0/0 silences both");
        Assert.AreEqual(GameSessionDisposition.Rejected, owner.PlaySoundScheme(owner.Version, -1, 0).Disposition);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.PlaySoundScheme(new(owner.Identity, 0), 1, 2).Failure);
    }

    [TestMethod]
    public void A_scheme_without_a_list_base_leaves_its_slot_empty_and_a_transition_arms_the_restore()
    {
        using var owner = Owner();
        owner.AttachSoundSchemes(new Schemes(29) { Transition = 7 });
        owner.PlaySoundScheme(owner.Version, 1, 29);
        Assert.AreEqual((1, 0), (State(owner).Slot0, State(owner).Slot1), "29 (\"No Sound\") loads nothing");
        owner.PlaySoundScheme(owner.Version, 7, 40);
        Assert.AreEqual((7, 40, true, 1, 0), (State(owner).Slot0, State(owner).Slot1, State(owner).RestorePending, State(owner).Restore0, State(owner).Restore1));
        owner.PlaySoundScheme(owner.Version, 3, 40);
        Assert.AreEqual((3, 40, 1), (State(owner).Slot0, State(owner).Slot1, State(owner).Restore0), "a pending restore keeps the replaced pair");
        Changed(owner.FinishSoundTransition(owner.Version), owner);
        Assert.AreEqual((1, 0, false), (State(owner).Slot0, State(owner).Slot1, State(owner).RestorePending), "the replaced pair plays again");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.FinishSoundTransition(owner.Version).Disposition);
    }

    [TestMethod]
    public void Combat_music_starts_with_an_npc_and_restores_the_latest_request_when_the_pc_leaves()
    {
        using var owner = Owner(hero: true);
        owner.PlaySoundScheme(owner.Version, 1, 40);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, SessionCombatant.Hero, true, SessionCombatModeContext.RealTime).Disposition);
        Assert.IsFalse(State(owner).CombatMusic, "the PC entering starts nothing");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, Wolf, true,
            new() { Facts = new() { Speed = 7, Life = new() } }).Disposition);
        var combat = owner.QuerySound();
        Assert.AreEqual((true, 0, 0, 1, 40, 1L), (combat.State.CombatMusic, combat.State.Slot0, combat.State.Slot1, combat.State.PreCombat0, combat.State.PreCombat1, combat.CombatStarts));
        owner.PlaySoundScheme(owner.Version, 2, 41);
        Assert.AreEqual((2, 41, 0, 0), (State(owner).PreCombat0, State(owner).PreCombat1, State(owner).Slot0, State(owner).Slot1), "a request during combat becomes the restore target");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetCombatMode(owner.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime).Disposition);
        Assert.AreEqual((false, 2, 41), (State(owner).CombatMusic, State(owner).Slot0, State(owner).Slot1));
        Assert.AreEqual(1L, owner.QuerySound().CombatStarts);
    }

    [TestMethod]
    public void A_map_opens_by_stopping_everything_and_playing_its_default_and_a_sector_skips_zero_pairs()
    {
        using var owner = Owner();
        owner.PlaySoundScheme(owner.Version, 1, 40);
        Changed(owner.EnterMapSound(owner.Version, 3, 29), owner);
        Assert.AreEqual((3, 29), (State(owner).Slot0, State(owner).Slot1));
        Changed(owner.EnterMapSound(owner.Version, null, null), owner);
        Assert.AreEqual((0, 0), (State(owner).Slot0, State(owner).Slot1), "no SoundScheme line: silence until a sector or script asks");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.EnterSectorSound(owner.Version, 0, 0).Disposition, "a 0/0 sector changes nothing");
        Changed(owner.EnterSectorSound(owner.Version, 0, 42), owner);
        Assert.AreEqual((42, 0), (State(owner).Slot0, State(owner).Slot1), "one id 0 leaves that slot empty");
    }

    [TestMethod]
    public void A_movie_lock_drops_requests_and_unlock_plays_the_saved_pair()
    {
        using var owner = Owner();
        owner.PlaySoundScheme(owner.Version, 1, 40);
        Changed(owner.LockSound(owner.Version), owner);
        owner.LockSound(owner.Version);
        Assert.AreEqual((2, 0, 0), (owner.QuerySound().LockDepth, State(owner).Slot0, State(owner).Slot1));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.PlaySoundScheme(owner.Version, 5, 6).Disposition, "dropped, not deferred");
        owner.UnlockSound(owner.Version);
        Assert.AreEqual((0, 0), (State(owner).Slot0, State(owner).Slot1));
        owner.UnlockSound(owner.Version);
        Assert.AreEqual((0, 1, 40), (owner.QuerySound().LockDepth, State(owner).Slot0, State(owner).Slot1));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.UnlockSound(owner.Version).Disposition);
    }

    [TestMethod]
    public void A_save_round_trips_and_a_combat_save_follows_the_rule_setting()
    {
        using var owner = Owner(hero: true);
        owner.PlaySoundScheme(owner.Version, 1, 40);
        var calm = GameSessionSnapshotCodec.Encode(owner.Capture());
        using (var restored = Restore(Decode(calm))) Assert.AreEqual(State(owner), State(restored));

        owner.SetCombatMode(owner.Version, Wolf, true, new() { Facts = new() { Speed = 7, Life = new() } });
        var snapshot = Decode(GameSessionSnapshotCodec.Encode(owner.Capture()));
        Assert.AreEqual(new SessionSoundState(0, 0, false, 1, 40, true, 1, 40), snapshot.Sound);
        using (var steamwoven = Restore(snapshot))
        {
            Assert.AreEqual(snapshot.Sound, State(steamwoven), "Steamwoven keeps the pre-combat pair");
            Assert.AreEqual(0L, steamwoven.QuerySound().CombatStarts, "a load never replays the stinger");
        }
        using var original = Restore(snapshot, RestoreOptions() with { Rules = RuleMode.Original });
        Assert.AreEqual(new SessionSoundState(0, 0, false, 1, 40, true, 0, 0), State(original), "the original overwrites it with the empty slots");
    }

    [TestMethod]
    public void Invalid_saved_sound_is_refused()
    {
        using var owner = Owner();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        foreach (var bad in new[] { "\"slots\":[3,3]", "\"slots\":[-1,0]" })
        {
            var edited = text.Replace("\"slots\":[0,0]", bad, StringComparison.Ordinal);
            var decoded = GameSessionSnapshotCodec.TryDecode(edited);
            if (decoded.Snapshot is { } snapshot)
                Assert.AreEqual(GameSessionFailure.InvalidSound, GameSessionOwner.Restore(snapshot, RestoreOptions()).Failure, bad);
            else Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, decoded.Failure, bad);
        }
        var combatWithSlots = text.Replace("\"slots\":[0,0]", "\"slots\":[1,0]", StringComparison.Ordinal).Replace("\"combatMusic\":false", "\"combatMusic\":true", StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.InvalidSound, GameSessionOwner.Restore(Decode(combatWithSlots), RestoreOptions()).Failure);
    }

    private static GameSessionSnapshot Decode(string text)
    {
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        Assert.IsNotNull(decoded.Snapshot, $"{decoded.Failure} {decoded.Path}");
        return decoded.Snapshot!;
    }
}
