# Which builds may be deleted (ARC-DEV-028)

**Rule: never delete `artifacts/`, a build folder in it or a file of a build by hand** (Finder, `rm`, a disk
cleaner). Builds there are not plain caches: the launcher and every export read them. Remove builds only with
`./arcanum clean --apply`, which removes nothing protected. On 2026-10-18 `artifacts/` (29 GB) was emptied by hand
on the advice that the builds "can be rebuilt"; the launcher lost every current build, the export lost its size
measurement and the launch selection named a missing build, and ARC-DEV-026 had to add a first-export path and a
registry repair before anything could be exported again.

    ./arcanum clean            # dry run: every build, worktree and build output with its class and reason; removes nothing
    ./arcanum clean --apply    # removes only DISPOSABLE build payloads (the .app); manifests and evidence stay
    ./arcanum clean --json     # the same report as JSON (schema arcanum-clean-report-v1)

`./arcanum clean` is `tools/arcanum-local builds clean --checkout <main checkout>` (`tools/local_dev/clean.py`). It
reads the default local home (`~/Library/Application Support/Arcanum Steamwoven`) or `$ARCANUM_LOCAL_HOME`. It and
`./arcanum setup` write an untracked `artifacts/README-DO-NOT-DELETE.md` that points here.

## Classes

| Class | What | Why | What to do |
| --- | --- | --- | --- |
| Authority | The local home: `config-v1.json`, `build-registry-v1.json`, locks; owned homes under `local/*` (for example `local/gpad005-pr-home`); prune receipts and intents in a build folder | They record which build is current, selected, accepted, measured and pruned | Never delete. Fix records only with `tools/arcanum-local builds repair-registry` (ARC-DEV-026) |
| **Protected** build | In a configured artifact root: each task's `current.json`/`current-v2.json` and the build it names; every launch selection (even a stale one); accepted and rollback builds; the export measurement build (registry `measurements`) | The launcher picks its build by the current pointer and the selection (never by date); the export estimates its output as the largest verified current/accepted build plus 10% and its temporary space from the measurement build | Never delete. `clean` lists them as PROTECTED with the role |
| Keep | In a configured root: a build that is not registered, failed, partial, unreadable or unsafe; one being exported; a registered build in review (`open-review`, `unmerged`) or with lifecycle `accepted` that is no longer the accepted or rollback build; an `exported` build that no later current build of its task in the same root supersedes (for example the only build of an older task), or whose later current build does not verify; every build of a tool whose pointer, selection or registry record is unsafe or malformed | Its ownership or need is not proved; a failed candidate is evidence and cannot be exported again | Keep. Owner decides case by case |
| **Disposable** build | A registered v2 build (lifecycle `exported`) that a current build of the same task in the same root supersedes: registered later, and its manifest, evidence and every payload file verify; a build already `retired` | Nothing reads it: the launcher and the export use the current build, which stays measurable | `./arcanum clean --apply` retires it and removes its `.app`; `manifest.json`, `evidence/` and a `payload-pruned-v1.json` receipt stay (PRUNED) |
| Not managed | Folders of `artifacts/` that are not a configured root of this home (an older layout such as `artifacts/builds`, a lab output such as `artifacts/audio-lab`), or a root of an owned home (`artifacts/gamepad-builds` of `local/gpad005-pr-home`); unknown task folders in a configured root that is a folder of `artifacts/` | This home cannot prove what reads them; another home may treat them as current | Keep. Run `ARCANUM_LOCAL_HOME=<that home> ./arcanum clean` for an owned home; anything else only on the owner's word |
| Worktree | `.claude/worktrees/*` (Claude sessions) and `.worktrees/*` (the retired task board) | A session may still work in one; `.worktrees` are evidence (CLAUDE.md §4) | Report only. REMOVABLE means it is not locked, its branch is in `origin/main` (as of the last fetch; a fresh worktree with no commits of its own qualifies too), it has no uncommitted or untracked files and no ignored files besides build outputs; remove it with `git worktree remove PATH` once no thread works in it, and only on the owner's word. `.worktrees` stay |
| Regenerable | `bin/`, `obj/`, `TestResults/` next to a `.csproj` and `.godot/` next to a `project.godot` in `src`, `tests`, `tools`, `experiments` of the main checkout | The next build, test or editor import recreates them (offline restore needs the NuGet cache) | Report only. Delete only while no thread builds, exports or tests |
| Outside the project | Time Machine local snapshots, `~/.cache/huggingface`, uv and pip caches, Godot editor caches | Not read by the launcher or the export | The owner's choice; not covered by this rule |

## What `--apply` does

Everything runs under one registry lock, so an export cannot register in between:

1. It reloads the configuration and registry and classifies again. If a viewer of an affected tool runs (the
   launcher's per-tool lock is held for the viewer's lifetime), it refuses before anything changes.
2. It runs `builds retire` for each superseded build (`retire` refuses any protected build) and saves the registry.
3. It plans and applies through the existing retention path (`builds plan` / `builds apply`, see
   [local workflow](local-development.md#retention-lifecycle)): before each removal apply rechecks the configuration and
   registry digests, every pointer (hash or absence), the selection, the manifest and payload identity, and refuses a
   build leased by the launcher or an export, or whose executable runs.
4. It removes only the `.app` payload and writes `payload-pruned-v1.json`; the next report lists the build as PRUNED,
   and a pruned build no longer counts as a surviving payload for the ARC-DEV-026 first export.

If a recheck fails part-way, the builds already removed stay removed and the retired ones stay retired (a later
`clean --apply` finishes them); the result names both and the reason. `--apply` never touches unregistered builds,
unmanaged folders, worktrees, regenerable outputs or anything outside the configured artifact roots; it writes only
the registry in the local home and the prune records in the builds. Close the viewers first.

## Freeing disk space

1. `./arcanum clean`, then `./arcanum clean --apply` for the DISPOSABLE total.
2. For owned homes under `local/`, the same with `ARCANUM_LOCAL_HOME=<home>`.
3. REMOVABLE worktrees and regenerable outputs from the report, with the owner's word, while no thread builds.
4. Outside the project (snapshots, model caches) as the owner decides.

If builds were already deleted by hand: `tools/arcanum-local builds repair-registry` (dry run, then `--apply`) and the
ARC-DEV-026 first export ([local workflow](local-development.md#retention-lifecycle)).
