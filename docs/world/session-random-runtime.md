# Session randomness runtime

Status: **Implemented bounded component**, ARC-WORLD-019. The [game session](game-session-runtime.md) owns one seeded
gameplay generator. Every roll names what it decides (a label), the generator's seed and position are saved in snapshot
schema 7, and the same calls after a restore return the same rolls. The [attack resolver](attack-resolver-runtime.md)
can draw from it through `GameSessionOwner.CombatRolls`. It covers the state part of
[FR-CONTENT-007](../requirements/content-product.md#fr-content-007). No host calls it yet: the viewer makes no gameplay
rolls, and combat is not wired.

## What the original does

Read in the original's random, item and dialog functions and checked in the executable (ledger row Random (R1–R6)).

- One global generator: a 32-bit linear congruential step (Numerical Recipes constants) whose bits 8..22 are the
  15-bit output (the generator step, 0x4CEF50). The range draw `between(lower, upper)` returns `lower + rand % span`, so the low
  values of a range that does not divide 32,768 come up slightly more often (for a d100, 1..68 have 328 chances in
  32,768 and 69..100 have 327), and spans above 32,768 are not covered. Equal bounds draw nothing; reversed bounds
  are swapped (the range draw, 0x4CEF00).
- The seed is the wall-clock time at start-up (the seed functions 0x4CEEB0, 0x4CEEC0, 0x4CEED0, 0x4CEEE0). The "Random" game module registers only
  init and exit, no save or load function (module table, module fields), so a saved game does not
  keep the generator, and a load continues from whatever state the running process had.
- Two callers re-seed the shared generator. Generating a spawn-once inventory seeds it from the object's location, the
  product of the PC's race and background, the PC's gender and pairwise character products of the PC's examine text
  (an item function, 0x463E20). A dialog re-seeds it from the time when it first builds its replies, stores that seed, and
  re-seeds with it when it rebuilds them (a dialog function, 0x414E60). The generator then continues from that seed.

The original sequence therefore cannot be replayed: its seeds come from the time, and nothing saves the state.

## Project decision

The session keeps a **project generator**, not the original LCG (decision of this task, open to the owner):

- **SplitMix64** (Steele, Lea and Flood, 2014; the public-domain reference by S. Vigna): draw `k` is a fixed mix of
  `seed + k × 0x9E3779B97F4A7C15`. Draw `k` is a pure function of the seed and `k`, so the whole state is the seed and
  the count of draws made, and a generator rebuilt from both continues the same sequence. It passes the usual
  statistical batteries and needs no table.
- **Uniform ranges**: an inclusive range of `span` values takes one 64-bit draw and keeps `draw mod span`, after
  rejecting the draws below `2^64 mod span` (under 2^-32 of draws) so every value is equally likely. The original's
  modulo bias (about 0.3 % relative for a d100) is not reproduced. Equal bounds draw nothing, as in the source.
- **One sequence for the session**: the label names a roll; it does not select a stream or change the value. So the
  order of rolls matters, as in the original's single generator, and a roll added before another shifts it.
- **The host seeds it**: `GameSessionDescriptor.RandomSeed` (default 0). The owner never reads a clock or OS entropy;
  a host that wants a new sequence per new game passes a new seed (for example from the time). The viewer does
  (ARC-DEV-080, below).
- **Saved with the session**: a restored session continues exactly where the saved one stood, so a load does not
  re-roll (FR-CONTENT-007 acceptance: with a given seed and command sequence, the rolls are the same). This is the
  Steamwoven setting and an owner decision (gap review GI6, 2026-11-04): a reload repeats the same results, so
  save-scumming does not help.
- **Original setting at a load** (ARC-DEV-077, switch `LifecycleRuleProfile.LoadKeepsRunningRandom`): as the original
  keeps its process generator across a load (PS32), the restored session goes on with the generator the host is
  running, `GameSessionRestoreOptions.LoadRandom` (the viewer's quick load passes the current session's
  `QueryRandom()`, the main menu's placeholder session included), and every later roll differs from the save's own
  continuation. Without one it starts the default seed 0 at draw 0, as a new session without a seed; the owner still
  reads no clock. The saved `random` section is decoded and checked in both modes, and the next capture writes the
  generator the session went on with. The original's one saved roll, the skill module's pre-rolled gambling value
  (PS14, the `GamblingRoll` of a skill check), is a separate datum that no session domain holds yet: the slice that
  brings gambling into the session saves it and restores it in both modes, outside this switch.

- **The viewer seeds from the clock** (ARC-DEV-080, GI6 follow-up): the original seeds its one process generator once at
  start-up with the C runtime's clock (the random module's init function 0x4CEEB0 calls the clock seed function 0x4CEEE0,
  which seeds with `time(NULL)`: whole seconds since 1970 in 32 bits; read in the retail 1.0.7.4 executable, SHA-256
  `9bd80511…3d52`) and never reseeds it at a new game or a load; the only other seed points of the single-player game
  are the spawn-once inventory and the dialog (above), and a multiplayer message handler (0x49F7F2, not modelled). The
  viewer's host code (`AreaRandomSeed`) gives every session the player starts (the scene's first session and the main
  menu's placeholder, a new game, the opening shop's session) the seed of the clock's whole Unix seconds, wrapped to 32
  bits like the original's `time` result. The seed is drawn per new session, where the original continues one process
  generator across a new game; the difference is not visible to a player, as both are unpredictable. An explicit test or
  smoke launch (`SelfTestBootstrap` or the native smoke) and every test keep seed 0, which their known sequences and
  draw counts rely on (`AreaTransitionCoordinator.CreateSession` takes the seed, default 0). The editor's draft session,
  which never rolls, stays at 0. A load under Steamwoven restores the saved generator, so its seed does not matter there.

`Arcanum.Simulation.Randomness.SeededRandom` is the generator itself (Godot-free, no session types), so a component
that needs its own replayable sequence can use it without a session.

## API

| Entry | Contract |
| --- | --- |
| `GameSessionDescriptor.RandomSeed` | 64-bit seed of a new session; its generator starts at draw 0. |
| `GameSessionOwner.Roll(label, min, max)` | One uniform integer in `min..max` inclusive. Returns `SessionRollResult` with a `SessionRoll(Label, Min, Max, Value, Position)`, where `Position` is the draws made after this roll. The label is 1..64 ASCII letters, digits and `. _ : -` (for example `combat.AttackCheck`, `loot:gold`). A bad label or `min > max` is `InvalidRoll`; a generator at its last draw (2^63 − 1) is `RandomExhausted`; a refused roll draws nothing. |
| `GameSessionOwner.QueryRandom()` | `SessionRandomState(Seed, Draws)`. |
| `GameSessionRestoreOptions.LoadRandom` | The host's running generator (ARC-DEV-077). Under the Original setting the restored session takes it in place of the saved seed and position (null: seed 0 at draw 0); Steamwoven ignores it. A negative position is `InvalidDescriptor` in both modes. |
| `AreaRandomSeed.FromClock(now)` / `ForNewSession(testLaunch, now)` | The viewer host's seed (ARC-DEV-080, `tools/terrain-viewer/Core`): the clock's whole Unix seconds in 32 bits, or 0 for a test or smoke launch. |
| `GameSessionOwner.CombatRolls` | The session generator as the attack resolver's `ICombatRollSource`; each roll is labelled `combat.` and the `CombatRollPurpose` name. The source interface has no refusal channel: `min > max` throws `ArgumentOutOfRangeException` (the interface's precondition, as in `CombatRollLog`), and an exhausted generator throws `InvalidOperationException`. The label is checked but not recorded: the resolver's `CombatRollLog` keeps the trace by purpose. |
| `GameSessionOwner.SpellRolls` | The session generator as the magic rules' `ISpellRollSource` (ARC-MAGIC-005); each roll is labelled `magic.` and the `SpellRollPurpose` name (`ResistanceD100`, `SaveD20`, `FailChanceD20`; `InvocationAptitudeD100` from ARC-MAGIC-006); refusals throw as for `CombatRolls`. |
| `SeededRandom(seed, draws)` / `TryNext(min, max, out value)` / `Output(seed, index)` | The generator: position, one range roll (false when exhausted, with no draw), and draw `index` (1-based) of a seed. |

Rolls are caller-serialized like every session call. Like clock and flag changes, a roll takes no expected version and
does not move the session revision; the position is saved with the snapshot. Only an explicit `Roll` (or a call through
`CombatRolls`) draws, so rendering, the camera, frame rate or a visual mode cannot spend a gameplay roll unless a host
asks for one.

## Snapshot schema 7

`random` is encoded between `inventory` and `lighting`:

```json
"random":{"seed":"0123456789abcdef","draws":3}
```

`seed` is exactly 16 lowercase hex digits (one spelling per seed, and no JSON number precision limit); `draws` is
0..2^63 − 1. Decoding refuses a seed in another spelling, such as uppercase (`NotCanonical`), any other seed text or
draw count (`OutOfRange`), a non-object or a seed that is not a string (`WrongType`), and missing or unknown fields.
Any seed and position restore, including the last one: a hand-edited save at draw 2^63 − 1 then refuses every roll that
needs a draw, and through `CombatRolls` that refusal is an exception out of the resolver, after any earlier rolls of
that attack. Normal play cannot reach it; a typed path is part of wiring combat.

**Migration decision: none.** Schemas 1..6 are `UnsupportedSchema` (and schema 7 since ARC-COMBAT-002 added
[`life`](life-state-runtime.md#snapshot-schema-8)). A schema-6 quick save loads as "written by an
incompatible version" with the running session kept, and the next F7 overwrites it. No host made gameplay rolls
before this schema, so nothing is lost.

## Tests

- [`SeededRandomTests`](../../tests/Arcanum.Simulation.Tests/Randomness/SeededRandomTests.cs): the published SplitMix64
  outputs of seeds 0 and 1234567, hand-worked range values, equal bounds, the widest range, continuation from seed and
  position, a rejected draw (seed −gamma makes the first draw 0), the last draw with and without a rejection, and an
  even die over 60,000 rolls.
- [`SessionRandomTests`](../../tests/Arcanum.Session.Tests/SessionRandomTests.cs): labelled rolls without a revision
  move, the label not changing values, refused labels and ranges with no draw, the same rolls after encode, decode and
  restore, four attacks resolved through `CombatRolls` replayed exactly from a save, combat labels and ranges, an
  exhausted generator, decode failures of the `random` section, a hand-built negative position, disposal; the GI6
  switch (ARC-DEV-077): Steamwoven keeps the saved generator and ignores a running one, Original goes on with the
  running one (or seed 0) and saves it next, a negative running position refused in both modes and a damaged saved one
  under Original too.
- [`AreaRandomSeedTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaRandomSeedTests.cs) (ARC-DEV-080): the clock seed is
  whole Unix seconds wrapped to 32 bits, a test launch keeps 0 and a player launch takes the clock, a session made with
  and without a seed, two launches at different times roll differently and one launch the same, and an Original quick load
  right after a relaunch goes on with that launch's clock seed instead of 0.
- [`AreaQuickSaveTests`](../../tests/Arcanum.TerrainViewer.Tests/AreaQuickSaveTests.cs)
  `QuickLoadTakesTheGeneratorTheRuleModeSays`: the viewer's quick load under each setting.

```sh
"$DOTNET" test tests/Arcanum.Simulation.Tests/Arcanum.Simulation.Tests.csproj --no-build --filter "FullyQualifiedName~Randomness"
"$DOTNET" test tests/Arcanum.Session.Tests/Arcanum.Session.Tests.csproj --no-build --filter "FullyQualifiedName~SessionRandom"
```

## Not implemented / remaining work

- Combat is not wired to a session by every host. The life
  state's knockout and second-arm rolls (`combat.KnockoutFatigue`, `combat.CrippleBothArms`, ARC-COMBAT-002) draw from
  the same generator.
- The original's other draws (AI reactions, sounds, effect stat checks) are not modelled, so a session fight is not an
  original fight draw for draw.
- The original's re-seeding for spawned inventories and dialog replies; a derived, independent sequence per purpose (if
  the owner wants rolls of one kind not to shift those of another).
- A legacy LCG profile, if the owner wants the original's 15-bit modulo distribution.
- A trace of recent rolls kept by the session (callers keep their own, like the resolver's `CombatRollLog`).
- **Original load after a relaunch (GI6)** is closed by ARC-DEV-080: the viewer's sessions are seeded from the clock (above),
  so under Original a save loaded right after a start of the viewer goes on with that start's seed, as in the retail game.
  Smokes and tests stay on seed 0; a smoke or test cannot show the clock's seed itself, only a player launch does.
