namespace Arcanum.World;

/// <summary>ARC-DEV-080 (GI6 follow-up): the seed the viewer gives a new session's gameplay generator. The original seeds its one
/// process generator once at start-up from the C runtime's `time` call (the random module's init function 0x4CEEB0 calls the clock
/// seed function 0x4CEEE0, which seeds with `time(NULL)`, whole seconds since 1970 in 32 bits), and a load never reseeds it,
/// so under the Original setting a load right after a relaunch must not start from a fixed seed. The session owner reads no clock
/// (<see cref="Arcanum.Session.GameSessionDescriptor.RandomSeed"/>); this host helper does. Tests and smokes ask for seed 0
/// (they roll and compare against known sequences); every start the player makes asks for the clock.</summary>
public static class AreaRandomSeed
{
    /// <summary>The original's seed for the moment <paramref name="now"/>: whole Unix seconds, wrapped to 32 bits (`time` returns a
    /// 32-bit count). The session generator takes the 64-bit seed as given.</summary>
    public static ulong FromClock(DateTimeOffset now) => unchecked((uint)now.ToUnixTimeSeconds());

    /// <summary>The seed of a new viewer session: 0 for an explicit test or smoke launch, the clock's otherwise.</summary>
    public static ulong ForNewSession(bool testLaunch, DateTimeOffset now) => testLaunch ? 0UL : FromClock(now);
}
