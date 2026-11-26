# ADR: what to do with the disputed code of the dead-code audit

Status: **proposed, awaiting the owner** (work item ARC-DEV-3404,
[#3404](https://github.com/Simarglok/arcanum-steamwoven/issues/3404), 2026-11-26). Nothing is removed by this page;
each accepted verdict is carried out by its own follow-up issue (listed under [Follow-up work](#follow-up-work)), and
an accepted page gets its row in [decisions](decisions.md). Epic
[#3377](https://github.com/Simarglok/arcanum-steamwoven/issues/3377). Requirements: none (code organisation).

## Context

The [dead-code audit](../research/dead-code-audit-2026-11-26.md) (#3381) and its Mac pass removed what was dead
(PR #3398, PR #3403). The rows that PR #3403 kept, because they are live in some way or need the owner, are the subject
here:

- from [#3386](https://github.com/Simarglok/arcanum-steamwoven/issues/3386): the manual viewer modes `terrain-only`,
  `combined` and `actor-diagnostic`; the ARC-RENDER-008 build fallback with the old-task guards around it;
  `tools/steamwoven_ui/image_rescue.py`;
- from [#3387](https://github.com/Simarglok/arcanum-steamwoven/issues/3387): the 237 methods that a name scan found
  referenced only by tests; the support for older save versions (`GameSessionSnapshotUpgrade.cs`).

The owner asked for a plan that keeps what is needed in use and stops keeping what is not, instead of deleting at once.

## Principles

These decide every verdict below and are proposed as standing rules for later clean-ups.

1. **A member is dead only by compiler-level evidence.** A name scan does not see a call through an interface, a
   virtual override or a method group; the 237-method list below shows how far off it is (101 of the 237 are called through an interface).
   Removal lists come from the analyzers or from find-references, never from a name scan alone.
2. **Production API has a production caller.** A `public` member of `src/` is called by `src/` or `tools/`, or an
   open issue names the work that will call it. Otherwise it is a test seam.
3. **A test seam is `internal`, not public.** Every test project but `Presentation.Tests` and `Input.Tests` already
   sees its assembly's internals (`InternalsVisibleTo`), so a seam that reads private state stays where the state is
   and becomes `internal`. A seam that needs only public API moves into the test project's fixture class
   (`SessionFixtures`, `AreaOwnerFixture` and the like, the existing convention).
4. **One rule, one entry.** A wrapper whose rule production reaches through another entry point is deleted, and its
   tests move to the entry point production uses, so a test never checks a path the game does not take.
5. **A launcher mode, export target or tool exists for a current workflow.** History is kept by git, not by a mode
   nobody launches.

## Decisions

### 1. Manual viewer modes `terrain-only`, `combined`, `actor-diagnostic` — delete

Evidence: `./arcanum` offers only `world`, `area` and `map`; the three modes are reached only by a manual
`tools/arcanum-local launch --mode X` (`launch.py` `MODE_ROLES`, `arcanum-local` choices and its 12 `--actor-*`
preview flags). No smoke stage uses them. `world` (world-diagnostic: terrain plus statics) and `area` cover what
`terrain-only` and `combined` showed; `actor-diagnostic` draws one synthetic actor marked "placement parity: NOT
VERIFIED", superseded by the area viewer's real actors. On the viewer side they are the enum values
`TerrainOnly`, `CombinedStaticDiagnostic`, `ActorDiagnostic` (`TerrainViewerMode.cs`), with branches in
`TerrainViewerArguments`, `TerrainLoadCoordinator`, `TerrainCachePolicy`, `ActorDiagnosticSource`, the HUD and
`TerrainViewerApp`, a self-test step and `SyntheticActorFixture` that exist only to check the modes themselves, about
ten C# and Python tests, and 16 lines of `docs/rendering/terrain-viewer.md` plus `docs/workflow/local-development.md`.

Verdict: **delete the three modes in the launcher and in the viewer**, together with their tests, the self-test step,
the fixture and the doc text (one PR). `TerrainOnly` is also the parser's default when `--mode` is missing: the viewer
then refuses a missing mode (the launcher always passes one) instead of picking a new default. Considered and rejected:
removing only the launcher rows (leaves viewer code no launcher reaches, the same problem one level down); keeping them
as "debug modes" (no workflow names them, and `world`/`area` are the debug views in use).

### 2. ARC-RENDER-008 fallback and the old-task guards — delete the fallback, keep the rest

Evidence: `./arcanum` `MAP_MODE` launches `ARC-DEV-015` (the universal build) and falls back to an `ARC-RENDER-008`
(passability overlay) build only when no universal build is registered. Both are registered today; the fallback
would start a much older, narrower viewer, which hides a missing export instead of reporting it. The other rows are
live: `TASK_ROLES`/`TASK_MODES` are used by the Crash Site smoke and `area_validate.py`; `SUPPORTED_TOOLS` lists the
task ids of builds that are registered on disk, and retention, `clean` and the config read it, so dropping an id
would make a registered build unresolvable; `AREA_MODE_COMMIT` still refuses builds on disk that predate the area
mode.

Verdict: **delete the `MAP_MODE` fallback** (with its messages, its test lines and the
`docs/workflow/local-development.md` paragraph): `map` without a universal build says to export `ARC-DEV-015`.
**Keep** `TASK_ROLES`/`TASK_MODES`, `SUPPORTED_TOOLS` and `AREA_MODE_COMMIT`. Exit rule for the kept rows: an old
task id leaves `SUPPORTED_TOOLS`, and a commit guard leaves `./arcanum`, in the PR after `./arcanum clean` has retired
the last registered build that needs it (build retention decides, never a hand delete of `artifacts/`). Whether the
`ARCANUM_TERRAIN_EXPORT_TASK=ARC-RENDER-008` export should stay possible is not decided here: it is documented and
harmless, and goes with the same exit rule.

### 3. `tools/steamwoven_ui/image_rescue.py` — move the recurring part into `image_archive.py`, delete the one-off part

Evidence: 367 lines from ARC-DEV-3277 (#3293). Two jobs: (a) the one-off rescue that copies task images left only in
worktrees, session scratchpads, temp folders and the Codex output into `local/steamwoven-ui/archive/<TASK>/`, guessing
the task from the path, the branch or a transcript; (b) registering the in-place pipeline files (outbox, codex-work,
masters) as `location: pipeline` rows. `image_archive.py` only reads and verifies such rows, so `image_rescue.py` is
their only writer, which contradicts SWI-PRV-5 of the [interface TRD](../ui/steamwoven-interface-trd.md) ("the only
writer" is `image_archive.py`). No code or skill calls it; `test_image_archive.py` imports two helpers.

Verdict: **move (b)** into `image_archive.py` as its own subcommand with its tests, so the archive tool is the one
writer, as the TRD says. **Delete (a)** after one last `image_rescue.py plan` on the Mac shows nothing left to rescue
(the mandatory archive rule now saves every image before it is posted, so there is no new backlog). If the plan still
lists files, the follow-up runs `run` once, then deletes. Rejected: keeping (a) as a standing safety net (its task
guessing from transcripts is the fragile part, and it duplicates a rule the threads already follow).

### 4. The 237 methods referenced only by tests — mostly keep; seams become `internal`; wrappers and dead ends go

A second pass (declarations read, interface base lists and callers through interfaces checked; the full mapping is
in a [comment on #3404](https://github.com/Simarglok/arcanum-steamwoven/issues/3404#issuecomment-7192483619)) splits the list:

| Group | n | What it is | Examples | Verdict |
|---|---:|---|---|---|
| F1 interface or port implementation | 101 | called by production through the interface (`IScrQueries`, `IAiDoorWorld`, `IAiExecutorWorld`, `ISpellCastMap`, `ISpellRunHost`, dialog ports, `IGoalStartPorts`, …) | `GlobalFlag`, `StoryState`, `IsJilted` (via `ScrInterpreter`), `DoorBusy` (via `ExecutorDoorGoals`), `IsDestroyed`, `GatePasses` | **keep**: live, not a finding |
| F2 other scan errors | 5 | same-named member, a type or an attribute | `DamageObject` (called by `AreaSpellCastMap`), `SpellBookRules.IsKnown`, `RouteFollower.PlanWalk`, `RestockSet`, `OriginalFunctionAttribute` | **keep** |
| S1 virtual seam | 5 | `internal virtual` members of `TerrainLoadAdmission` that the load coordinator calls and tests override | `AcceptCensus`, `InspectTraversal` | **keep**: designed seam |
| X stale row | 1 | file already removed by #3398 | `RefreshOffers` | nothing to do |
| Q session read seams | 39 | public `Query*` getters on `GameSessionOwner` returning an immutable copy; tests assert state with them; many are named in `docs/world/*` | `QueryDurability`, `QueryDangerRecords`, `QueryScriptState`, `QueryResidentsIn`, `QueryObjects` | **keep, make `internal`** (principle 3); a seam an open issue will call stays public and names the issue; doc lists say "test seam" |
| M session mutators | 21 | public versioned `Add*`/`Set*`/`Lock*` entries over a rule path that production reaches through another entry (e.g. `AddDangerEnemy` over the danger plan, `ApplyAttackWear` over the attack's wear steps) or a feature not wired yet | `AddDangerEnemy`, `LockDangerTarget`, `AddHeroTimedEffect`, `BreakItem`, `LockSound`, `ApplyAttackWear` | per method: **delete** the wrapper and move its tests to the production entry when that entry exists (principle 4); **keep public** with a named wiring issue when the feature is unwired; otherwise **`internal`** |
| R rule helpers | 29 | static rule methods in Simulation/Scripting with no caller; part are facades over a general rule production uses, part are ported original functions not yet wired (about 10 carry `[OriginalFunction]`, which the function-registry tests read) | `AttackRules.CriticalHitChance` (a facade over `SkillCheckRules.CriticalSuccessChance`), `CriticalMissChance`, `DodgeEffectiveness`, `HostilityRules.FindTarget`, `ItemDurabilityRules.BlocksUse`, `HotkeyRules.EntryOfScancode` | facades: **delete**, tests to the general rule; unwired ports: **keep** with a wiring issue, else **delete** with the registry row |
| D data accessors | 23 | readers and lookups in Data, Executable, Input, Presentation that production reaches another way | `AreaDataOwner.AcquireSelectedFrame`/`CopyRgba` (production uses the batch path), `AreaRoofGrid.IsFaded`, `BackgroundTable.BaseMoney`, `PeImage.SectionAt`, the `FromName` parsers of the rule profiles | **move to a test fixture** when it needs only public API, else **`internal`**; delete when no test needs it |
| W world host helpers | 7 | helpers in `Arcanum.World` with no caller | `AreaScenePlan.UprightOrder`, `AreaWallCheck.FadeOf`, `AreaPortals.OpenCrossed` | **delete** or **`internal`**, per method |
| T internal test hooks | 4 | already `internal`, test-only | `LoseGoalStepForTests`, `QueryEventClocks`, `UnwaitUnforced`, `CatalogueNumbers` | **keep** as they are |
| E dead everywhere | 2 | no hit, tests included | `HdRgbImage.FromRgba`, `AutoLevelRules.SkillCap` | **delete** |

So 116 of the 237 (F1, F2, S1, X, T) need nothing, and about 121 methods (roughly 750 lines of production code) get the verdicts
above. The codecs `ControlSchemeSettingsCodec` and `RuleModeSettingCodec`, which the audit also named, are test-only as
well (`LauncherSettings` only mentions the second in a comment): #3408 checks whether the launcher settings replaced
them (delete, principle 4) or they wait for wiring (keep with a named issue). The 54 unused parameters (IDE0060) stay with
[#3397](https://github.com/Simarglok/arcanum-steamwoven/issues/3397).

To keep it from coming back (principle 2): the analyzer configuration of #3387 is kept as a documented, uncommitted
recipe and re-run at each audit; a public member added for a test is a review finding.

### 5. Older save versions — keep the mechanism, move the floor only at points the owner names

Evidence: `GameSessionSnapshotUpgrade.cs` (720 lines) reads saves from schema 56 (the first packaged build,
ARC-DEV-047) up to the current 106, one small step per schema (mostly one added field with the value an older session
means); below 56 and above the current schema a load fails with the typed `UnsupportedSchema` ("incompatible
version"). A test fails when a schema bump has no step, and per-feature older-save tests in 33 test files use the
`SchemaNN` helpers. The rule "a bump adds a step, or moves the floor with the owner's agreement" is written only in the
code comment and [game session runtime](../world/game-session-runtime.md#older-saves-arc-ui-037). The owner's
packaged-game save folder holds saves of schemas 57, 70, 90 and 106 (read on 2026-11-26), so old versions are in real
use.

Verdict: **keep** the upgrade chain; it is live code with a typed refusal, and a step costs a few lines. Record the
rule as an owner decision: **the floor (`OldestReadableSchemaVersion`) moves only at a point the owner names** (for
example a new alpha the owner hands out, or a fresh playthrough); a floor move deletes the steps below it and the older-save
tests that use them, in one PR. Not proposed: dropping old saves until release (it would break saves the owner plays
on every schema bump, about two a day since 2026-10-28). The owner's question for now: **move the floor today** (to 90, which keeps the saves of 2026-11-11 and later, or
to 106, which keeps those of 2026-11-21 and later) or keep 56?

## Follow-up work

Filed as blocked issues, each waiting on the owner's verdict on this page; an amended verdict amends its issue.

| Decision | Issue | Work |
|---|---|---|
| 1 | [#3405](https://github.com/Simarglok/arcanum-steamwoven/issues/3405) | delete the three manual viewer modes (launcher, viewer, tests, docs) |
| 2 | [#3406](https://github.com/Simarglok/arcanum-steamwoven/issues/3406) | delete the `MAP_MODE` ARC-RENDER-008 fallback |
| 3 | [#3407](https://github.com/Simarglok/arcanum-steamwoven/issues/3407) | move pipeline-row registration into `image_archive.py`, last rescue plan, delete `image_rescue.py` |
| 4 | [#3408](https://github.com/Simarglok/arcanum-steamwoven/issues/3408) | test-only methods: groups Q, M, R, D, W, E by the verdicts above (may split per group) |
| 5 | [#3409](https://github.com/Simarglok/arcanum-steamwoven/issues/3409) | record the save-floor rule; move the floor if the owner says so |
