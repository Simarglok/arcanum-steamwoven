# ARC-UI-114 — HD арты страниц персонажа без посторонних подложек

Work-Item: ARC-UI-114
Result: Character-page HD repair candidate, preserving button states and HD grey icons.
Components: HD import, character-page drawing, local character-art pipeline
Human-Gates: none
Requirements: FR-PRODUCT-004

Owner authority: explicit delegated implementation request of 2026-11-08. No publication, merge, live installation or original-install changes authorized.
Base: origin/main including ARC-UI-111 (#872). ARC-UI-112 (#873) remains open and owns filtering; do not duplicate it.
Workspace: isolated res095-character-hd-repair checkout. Existing root packages.lock.json edit is unrelated and preserved.

AC1. Exact and derived import frames respect lock:false; synthetic regression checks both paths.
AC2. Grey spell icons use the active HD frame, existing luminance weights and Legacy layout dimensions; synthetic checks cover HD off/on and pack replacement.
AC3. Four skill-tab pilot variants and their available states have explicit local removal masks applied after derivation; original generations remain unchanged.
AC4. The confirmed treatment is applied only to affected skills/technology/magic page elements. Existing square sockets and original holes survive; no blanket circular clipping.
AC5. A separate local candidate pack passes hashes, key/size checks and runtime ResolveSprite for the bounded character-page frame set, without Legacy fallback for repaired frames.
AC6. Local compositions cover four skill groups, eight disciplines, sixteen schools with five spell rows, and selected ranks 0–5 plus available button states; provenance and limitations accompany them.
AC7. Mandatory root and affected native checks pass through the launch queue; local original-derived evidence stays ignored. No original images enter git.

Owner visual acceptance is a later manual assessment, outside automatic acceptance. No claim of acceptance or live-game installation.

## Findings within the accepted scope

The owner's fresh Original/HD comparison reproduced the rectangular patches.
Runtime resolution alone was insufficient: the same pack passed resolution but
showed the patches in the native sheet smoke. `AreaArtTextDraw.Prepare` assigns
PremultAlpha to the sheet canvases; the HD images supplied straight RGB,
including deliberately bled colour at alpha zero. The fix supplies premultiplied
HD pixels only for this sheet's non-font textures. It changes neither the alpha
mask nor the font material. Native regression includes both zero-alpha corners
and a fractional edge compared with ordinary alpha blending.

Local variants additionally remove the reviewed detached single-pixel components
of four skill tabs after derivation, preserve the other button silhouettes and
holes, and correct the saved extraction rectangles of the spell/tech plus and
minus cells. Only twelve skill-tab frames use the existing soft slice policy;
the remaining interface entries retain legacySmooth. The local candidate is not
installed or accepted by the owner. Coverage remains partial; summary counts do
not change.

## Automatic evidence (2026-11-08)

- Root locked restore/build/tests: PASS, 4,496 passed, 48 reported skipped,
  RealData excluded. Build: zero warnings/errors. Launch-queue receipt #9861.
- DAT probe and original-oracle self-tests: PASS; oracle integration ran, not
  skipped. No original functions or rule modes changed.
- Viewer build and synthetic suite: PASS, 1,435 passed, zero skipped (#9916,
  #9923). Earlier incorrect solution-path invocation was corrected; the final
  solution is `tools/terrain-viewer/Godot/Arcanum.TerrainViewer.sln`.
- Native self-test: PASS (#9920), with actual framebuffer assertions for
  zero/fractional alpha on PremultAlpha and HD grey copies across pack changes.
  Expected malformed-save refusal messages occur in the negative tests.
- Final separate pack: 1,142 entries pass integrity; all 311 non-font character
  frames resolve as HD against the original device data (#9924). No Legacy
  fallback in that bounded set. The same resolution check alone did not catch
  the original blend defect, hence the native framebuffer regression.
- Local art compositions: four skills groups, eight disciplines with seven
  learned rows, sixteen schools with five spell rows; selected ranks 0–5 and
  a 32-family × three-frame button atlas. Images, inputs, manifests and local
  provenance remain ignored. They are not a claim of owner visual acceptance.

No export/deployment, original-install change, PR publication or merge by this
work item. Independent review has not been requested; the candidate is held for
owner review. The previously modified lock file in the main checkout remains
unchanged by this task.

Final native real-data scoped smoke: PASS (#9928),
`./arcanum smoke pr --stages map --hero elf-female --no-build` with explicit
candidate pack, `ARC_AREA_SHEET_HD_VERIFY=1` and local capture path. The sheet
smoke resolved 311/311 non-font character frames, exercised the skills, tech,
spells and NPC sheet paths and saved readbacks. Visual inspection of the three
hero pages confirms the rectangular RGB patches and transparent-gauge blocks
are absent; the square sockets remain. The ordinary bar stage also passed
(#9888). Shutdown texture-leak diagnostics remain in the native log, also
observed in the pre-fix capture run (#9903); leak freedom is not asserted.
The original owner installation remains unchanged by this task. Final code and
local pack are separate deliverables: the pack alone cannot fix the blend.

## Owner amendment — integration (2026-11-08)

Owner requested: "Добавляй в игру". Integrate with current main and ARC-UI-112
filtering; prepare the reviewed code for PR and build/update the local player
application and HD pack. No agent merge: the owner merges the PR.

A parallel session used ARC-UI-113 for #875 after this candidate allocated the
same number. This work is renamed ARC-UI-114 after checking history, reserved
codes, remote branches and PRs. Earlier evidence labelled ARC-UI-113 belongs to
this candidate's pre-integration run, not #875. New checks use ARC-UI-114.
Base for integration: main 55d836abe, including #873 and #875. Premultiply occurs
before mip generation; the new mask support and filter remain intact.

Integration update: while the owner closed the game, another session installed
main d5e5e56bb (#876). The candidate was rebased onto that commit to preserve
its saved-hero changes; the patch is byte-identical before/after rebase.
Owner explicitly selected a Codex sub-agent review instead of Claude Sonnet.
The independent read-only review found no actionable defects (PASS); no code
was sent to Claude. The new installation stage retains the current pack's 873
other entries and replaces only the same 269 character frames. It also corrects
the registered source rectangle of the skill gauge, whose left margin contained
the sheet gutter; original generations remain unchanged.
