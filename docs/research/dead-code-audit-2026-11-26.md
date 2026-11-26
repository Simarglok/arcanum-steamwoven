# Dead code and leftovers of old iterations: research report (ARC-RES-3381)

Research for epic #3377 (issue #3381, 2026-11-26). Nothing was deleted or changed except this page. Scope: `src/`, `tests/`, `tools/`, `experiments/`, `docs/`, `.github/` and the root files. Out of scope and untouched: `local/`, `research-private/`, `artifacts/`, `.worktrees/`, `t_<id>-arc-*` branches, `toolkit/`, the owner's HD pack.

## Method and limits

1. **Where it ran.** A cloud clone of `main` (27d9c28) with `rg`, `git`, `python3` and no .NET SDK. Compiler analyzers (IDE0051, IDE0052, CA1811 and similar) could not run, so every symbol-level verdict is "no reference by name", and the analyzer pass is filed as #3387.
2. **Projects.** All 42 `*.csproj` were checked against the solution, `ProjectReference`, linked sources, `.targets` imports and documented commands.
3. **Symbols.** A token scan over tracked files (comments stripped) listed public and internal types and static methods with no reference outside their own file. Each hit was checked by `git grep -w`. Test-only references are reported separately from production references. A name hit counts as "used", so the scan under-reports dead code.
4. **Scripts.** Each tracked script or module under `tools/` was searched by file name and module name across `tools/`, `tests/`, `docs/`, `./arcanum`, `tools/local_dev/launch.py` (role tables), `CLAUDE.md` and `AGENTS.md`.
5. **Godot.** `.tscn`, `.gdshader`, `project.godot` and `.uid` files were matched to references and to their `.cs` files.
6. **Docs.** Relative links, anchors and backticked `src|tools|tests|docs|experiments|.github` paths were checked (plus `python3 -B tools/docs_check.py`, 0 problems), as were banners, orphans and catalogue rows.
7. **Switches.** Environment variables read by code were compared with the ones named in docs, both directions.
8. **Reflection and role tables.** `src/` has no `Activator`, `GetType` or service collection; no launcher role or smoke table names any type listed below. Items reachable through the launcher role tables, the queue, the smoke runner or the oracle are marked "needs double check".
9. **History.** The repository has 50 commits dated 2026-11-25 and 2026-11-26, so `git log` dates carry no signal and are omitted. In-document dates are cited instead.

Result in one line: the repository is clean of old-iteration markers (no `[Obsolete]`, no `#if false`, no always-false constants, no `Legacy*`/`Old*`/`V1` type names, no `[Ignore]`d tests, no unreferenced Godot scene or resource, 0 broken doc links). The real leftovers are 5 orphan files, about 6 unreferenced methods, a handful of test-only runtime types, one superseded viewer project and two superseded labs, a few launcher guards, and stale doc lines.

Filed issues (all children of #3377): #3382, #3383, #3384, #3385, #3386, #3387, #3388.

## Findings

Confidence: **certain** = no reference anywhere (by name, strings, scenes, docs, role tables); **probably** = no production reference, but a judgement or a tool (Mac) is still needed. Size is lines or files. "Issue" is the removal or decision issue.

### No reference by name; delete after the Mac check (issue #3382)

"Certain" here means no reference by name in code, strings, scenes, docs or role tables. No compiler ran, so reflection, string and scene references were searched by name only. The `.uid` files are certain by file listing alone.

| Item | Evidence | Depends on it | Size | Risk | Action |
|---|---|---|---|---|---|
| `tools/terrain-viewer/Godot/{SteamwovenBoardView,SteamwovenEdgeView,TerrainViewerGeneratedInterfaceSmoke,TerrainViewerGeneratedPaneSmoke,TerrainViewerGeneratedWindowSmoke}.cs.uid` | `.cs` files removed with generated chrome (#3143); `git ls-files '*.uid'` with a missing `.cs` gives exactly these 5; class names have 0 code hits | nothing | 5 files, 1 line each | none | delete |
| `src/Arcanum.Godot/FrameCanvas.cs:191` `SetTexture` (shim comment at :190) | `git grep -w SetTexture` = 1 hit (declaration); comment says "Temporary source-compatibility shim for ARC-RENDER-001" | nothing | 7 lines | none | delete |
| `src/Arcanum.World/AreaLoadOperation.cs:181` `VerifyTraversal` | internal static, 1 hit (declaration) | nothing | ~15 lines | none | delete |
| `AreaMissileHost.OfRun`, `DialogText.IsBlank` (internal), `GameSessionCreatedObjects.QueryCreateSummonedCritterRefusal` | each `-w` = 1 hit, the declaration | nothing | 3-10 lines each | low | delete after the Mac analyzer pass |
| `PremadeHero.cs:39` comment "kept for compatibility, not reached" | `ItemPrototypeMissing` is returned at `PremadeHero.cs:194,198` and consumed at `AreaStartingHero.cs:284` | nothing | 1 comment | none | fix the comment |
| `AreaInventory` enum values `UnlistedInventoryItem`, `GoldHandleMismatch` | unused in `src`; `docs/world/area-inventory-view.md:77` says "stays in the enum, unused" | docs | 1 value each | none | keep or delete (owner may keep) |

Needs the owner (not deleted by default, and the claims below were not independently checked): `AutoLevelRules.SkillCap` (docs say "pending") and `HdRgbImage.FromRgba` (public record API).

### Documentation (issue #3383, docs only)

| Item | Evidence | Action |
|---|---|---|
| `docs/rendering/area-viewer.md:241,317` | name `tools/area_owner_seam_oracle.py`, `tools/area_motion_oracle.py`; the real path is `tools/terrain-viewer/Godot/tools/*.py` (lines 252, 345 already use it) | fix 2 paths |
| `docs/rendering/hd-pack-4x3-audit.md:22,51,68`, `hd-interface-art.md:417` | name `InterfaceChrome` / `InterfaceChromeRenderer`; `git grep InterfaceChrome` finds no code; removed with generated chrome (#3143, 2026-11-25); other pages already carry a banner | add "(removed)" or a banner |
| `docs/world/game-event-queue-runtime.md:369` | `SessionEventTests` without `.cs` | cosmetic fix |
| `docs/requirements/coverage/ui-media-lifecycle.md:15` | cites smoke `ARC_AREA_MENU_STEAMWOVEN=1`; no code reads it; `terrain-viewer.md:2235` says ARC-UI-2926 removed it | fix the row |
| `docs/research/audio-evidence.md:15` | "Steamwoven plays no sound yet"; stale since ARC-UI-040..044 (`status/audio.md`) | dated note |
| `docs/project/owner-test-checklist-2026-11-14.md` | 1 KB tombstone; only inbound link is `owner-recheck-list.md:6` ("only a pointer now") | delete and drop the link, or keep the URL (ask) |
| Switches read by code, named in no doc | about 45 `ARC_AREA_*_CAPTURE/PROBE/REPORT` and `ARC_*_CAPTURE`, `RENDER006_{CANDIDATE,HOST,TASK}`, `WORLD003_GRID_VALIDATE`, `ARC_DEV203_CAPTURE`, `ARC_DEV_206_LOG_ALL`, `ARC_SMOKE_THROW_SEED`, `ARC_STEAMWOVEN_FIT_WINDOW`, `ARC_WORLD_INPUT_TRACE`, `ARC_BUG_REPORT_CAPTURE`, `ARC_SAVE_HD_VERIFY`, `ARC_IMAGE_ARCHIVE_QUIET` | document in a table; check the oldest task-named ones |

Kept on purpose, verified: the "superseded" banners of `gamepad-prototype.md`, `npc-dialog-entry-runtime.md`, `hd-interface-art.md`, `screen-size-and-ui-layout.md`; the retired-Hermes mentions (historical provenance); `docs/verification/triage/*.csv` (cited as evidence); `docs/formats/hd-pack.md` (`./arcanum hd-build` and `hd-import` are a deferred plan, ARC-DEV-003); `.github/ISSUE_TEMPLATE`, `README.md`, `CONTRIBUTING.md`, `THIRD-PARTY-NOTICES.md`, `Directory.*` (all resolve; there is no CI workflow). `docs/requirements/scene.md`, `dialog-evidence.md`, `hd-upscale-research.md` name external files (`src/Unit1.pas`, `src/main.cpp`), not repository paths.

### Owner decisions: runtime types that only tests use (issue #3384)

| Item | Evidence | Depends on it | Size | Conf. | Risk |
|---|---|---|---|---|---|
| `Scripting/Dialog/DialogSession.cs` (`DialogTransitionRuntime`, `DialogSession`) and start/offer types in `DialogRuntimeTypes.cs` | `git grep -lw DialogSession -- src` lists only these 2 files; `DialogTransitionRuntime` 0 non-test hits; production path is `DialogConversation` via `World/AreaConversation.cs` | ~51 test refs, `docs/scripting/dialog-transition-runtime.md` | 435 lines + part of 199 | probably | medium |
| `Scripting/NpcDialog/NpcDialogEntryResolver.cs`, `NpcDialogEntryTypes.cs` | 0 non-test hits; `AreaConversation.cs` uses `NpcDialogStateVersion` only; doc banner "superseded in the AREA viewer by the SCR dispatcher" | 46 test hits, doc | ~515 lines | probably | medium |
| `Scripting/GlobalFlags/GlobalFlagAdapter.cs` | 2 src hits, both in its own file | 14 test hits, `global-flag-state-runtime.md` | 145 lines | probably | low |
| `Data/Technology/TechDeviceCatalogue.cs` | 0 non-test refs | tests, `status/technology.md` | 111 lines | probably | low |
| `Data/Hd/HdPackImport.cs`, `Data/Dialogue/DlgReferenceValidator.cs` | 0 non-test refs; documented library API (`hd-pack.md:391`, `dlg-reader.md`) | tests, docs | 200 lines | probably | low |

Question: are these planned script-lane work (not yet wired) or replaced runtimes?

### Owner decisions: whole projects and labs (issue #3385)

| Item | Evidence | Depends on it | Size | Conf. | Action |
|---|---|---|---|---|---|
| `src/Arcanum.Godot` (HD review viewer, ARC-RENDER-001/015) | no `ProjectReference` to it; in the solution; no launcher; named in README.md:39, CLAUDE.md:140, THIRD-PARTY-NOTICES.md:13, `docs/workflow/toolchain.md:1179`, `docs/formats/hd-pack.md:233`, `docs/architecture/steamwoven-model.md`, `status/data.md`, `status/viewer.md` and `experiments/display-ui-lab/tools/validation_support.py:42`; `smoke_zones.py:103` "its own project, no smoke"; superseded by `tools/terrain-viewer`; `SyntheticFixture`, `HdReviewSelfTest` unused outside the folder | solution, `docs/project/components/Arcanum.Godot.md`, `test_smoke_zones.py:114,135` | 3938 lines, 19 files | probably; Mac check (scene, self-test) | owner: keep or retire |
| `experiments/hd-ui-density-lab` (ARC-RES-122) | concept rejected (`steamwoven-interface-adr.md:94-95`; `hd-interface-density.md:3`, #3143); `DensityScale\|DeviceGrid\|UiMetrics` outside the lab = 0 | docs only | 1339 lines, 25 files | probably | archive or delete after OK |
| `experiments/audio-lab` (ARC-UI-002) | production audio in `src/Arcanum.Presentation/Audio/*` and `ViewerAudioBackend.cs`; no lab type is used | `docs/audio/audio-lab.md`, `audio-evidence.md:16`, `build-retention.md:26` | 1244 lines, 26 files | probably | archive or delete after OK; keep for decoder policy? |

Not dead (kept, with reason): `experiments/relight-lab` (`NormalMapGenerator.cs` is compiled into `tools/terrain-viewer/Godot/Arcanum.TerrainViewer.csproj:11`), `experiments/gamepad-lab` (`src/Arcanum.Input` imports its `ExportLocks.targets`; 8 files in `src/Arcanum.Input` still carry `namespace GamepadLab.Core`; gate scripts in `tools/local_dev`), `experiments/display-ui-lab` (UI-scale/DPI findings not absorbed, FR-UI-007). `Arcanum.Input`, `TerrainViewer.Tests` and the 8 `tools/research/*Probe` projects are outside the solution but referenced or documented. No orphan project exists.

### Launcher and tooling guards (issue #3386, needs double check on the Mac)

| Item | Evidence | Depends on it | Size | Conf. | Risk |
|---|---|---|---|---|---|
| `tools/local_dev/run_queue.py` `LEGACY_WINDOW_LOCK`, `legacy_lock()`, `_legacy_hold()`; env `ARCANUM_QUEUE_LEGACY_LOCK` | docstring "retired `lockf` rule"; env var read by tests only, in no doc; `toolchain.md:502,567` describe the lock | `run_queue`, `daily.py:39`, 4 test files | ~60 lines | probably | medium (queue) |
| `launch.py` modes `terrain-only`, `combined`, `actor-diagnostic` | `./arcanum` exposes world/area/map only; reached through `arcanum-local`, `test_local_dev.py:614-650`, `test_launcher_sync.py`; viewer still parses them (`src/Arcanum.World/TerrainViewerArguments.cs:62`) | launcher, tests, viewer | ~15 lines + tests | probably | medium (role table) |
| `TASK_ROLES`/`TASK_MODES`, `MAIN_MENU_COMMIT`, `DEBUG_MODE_COMMIT`, `AREA_MODE_COMMIT`, `MAP_MODE` fallback, `manifests.SUPPORTED_TOOLS` old ids | guards for builds exported before those commits | launcher, tests | small | probably | medium |
| `tools/local_dev/smoke_known_failures.json` (ARC-COMBAT-026) | task id in no doc; symptom text still at `TerrainViewerRangedSmoke.cs:523`, so maybe still real | `smoke.py:285`, `smoke_verify.py` | 8 lines | uncertain | low |
| `tools/steamwoven_ui/image_rescue.py` | docstring "One-off and repeatable rescue" (ARC-DEV-3277); no code caller; 1 doc line | `test_image_archive.py` | 367 lines | probably | low |

### Test-only public seams and old-save support (issue #3387, needs the Mac analyzer)

| Item | Evidence | Conf. |
|---|---|---|
| About 120 public or internal methods with no production reference (mostly `GameSession*.Query*/Set*/Add*`, e.g. `QueryFocusWipes`: 1 src declaration, 11 test hits; `GameSessionGoals.LoseGoalStepForTests` is explicitly test-only); `ControlSchemeSettingsCodec`, `RuleModeSettingCodec`, `GameEventDelays.TryFromRealSeconds` (declaration plus tests) | name scan, no compiler | no production reference by name (not compiler-verified); whether to drop public seams is a design call |
| `Session/GameSessionSnapshotUpgrade.cs` (720 lines, 50 upgrade steps, schema 56 to 106) | live code, covered by tests, keeps old saves of an unreleased game readable | question only: drop old save versions? |

### Gap found on the way (issue #3388)

The batched `run_tests.py` reads only `tools/local_dev/test_*.py`. 7 of the 34 `tools/steamwoven_ui/test_*.py` have no shim there (`cutouts`, `fixtures`, `kit_check`, `label_parts`, `prompt_library`, `sheet_parts`, `tone_check`), and `tools/research/audio_resource_probe_tests.py` matches no pattern, so none of them run in the root check set.

### Checked and alive (so nobody re-checks)

- Every `tools/` script and module (about 180 outside the HD tools) has a non-test caller, import or doc reference. The `tools/research/*.py` evidence tools (`area_topology_probe`, `game_clock_policy_probe`, `exe_export_extras`, `audio_resource_probe`) and the 7 research probe projects have exactly one doc each: alive as evidence tools.
- The 27 `tools/local_dev/test_steamwoven_*.py` are 3-line re-export shims (ARC-DEV-2460), intentional.
- `tools/steamwoven_ui/prompts/*.txt` and `zone_specs/*.json` are all referenced. Large files (`kit.json`, `oracle_functions.py`, `run_queue.py`, `smoke.py`) are all referenced.
- Test fixtures and data (`ScriptTriggerRefusalBaseline.txt`, `dat_probe_adapter.py`, `tests/Shared/MethodIlHash.cs`) are used; no `[Ignore]`, `[Skip]` or `[Explicit]` tests; `Assert.Inconclusive` appears only as the opt-in guard for real data.
- Godot: 8 `.tscn`, 1 `.gdshader`, 6 `project.godot`, no `.gd`/`.tres`; every scene and shader is referenced.
- "Legacy" in code means the Original look / old HD vocabulary, not dead code. Env vars described as removed in docs (`ARC_AREA_MENU_STEAMWOVEN`, `ARC_STEAMWOVEN_LAYOUT`) are the only documented-but-unread ones.
- `.gitignore` entries `/game-data/` and `/tests/fixtures/real/` have no reference but guard copyrighted data: keep.

## Not covered

- Instance members, properties and fields of live types, private and nested types, unused packages and `ProjectReference`s, dead branches inside the 588 terrain-viewer `.cs` files (needs the analyzer pass, #3387).
- Whether `tools/original-oracle` and `tools/hd_font` are still needed beyond their import wiring; `tools/hd_pack` and `tools/hd-interface-import` were checked at reference level only (the HD pack is never touched).
- Semantic duplication among the 783 test files; doc bodies beyond links, paths, banners and keyword scans; `docs/verification/*.csv` registries.
- Nothing was built, run or smoked. Whether the labs still build was not checked.

## Owner questions

1. Keep `src/Arcanum.Godot` as the HD review tool, or retire it? (#3385)
2. Archive or delete `hd-ui-density-lab`; keep `audio-lab` for its decoder-policy findings? (#3385)
3. Are the test-only dialog, NPC-entry, global-flag adapter and tech-catalogue runtimes planned lane work, or replaced? (#3384)
4. Is any lane or old build still using `lockf`, the pre-commit build guards or the three extra launch modes? Is ARC-COMBAT-026 still open? Is the image rescue done? (#3386)
5. Drop old save versions (schema 56 to 105)? Remove public test-only seams? (#3387)
6. Keep the `owner-test-checklist-2026-11-14.md` tombstone URL? (#3383)
