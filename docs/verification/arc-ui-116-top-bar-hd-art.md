# ARC-UI-116 — HD top strip and left buttons: local art and installation

```text
Work-Item: ARC-UI-116
Result: Record the local art, checks and installation of the 13 top-strip and left-button frames behind PR #882.
Components: local HD pack (not in git), native bar smoke
Human-Gates: owner permission to install (given)
Requirements: FR-PRODUCT-004
```

PR #882 added the recipe and the opt-in native smoke ([top strip and left buttons](../rendering/hd-interface-art.md#top-strip-and-left-buttons-arc-ui-116)).
This note keeps what only existed in the local working folder: where the 13 frames came from, how they were
checked and what was installed. No original or derived art is added to git; the frames, readbacks and manifests stay local
(`local/res095/topbar116`).

## Art

- Buttons Character 169 (`char_but`), Logbook 187 (`log_but`), Map 193 (`tmap_but`) and Inventory 186 (`invn_but`) come from
  the registered G01 redraw `G01_s08` through the registered `ready/cells` and the identical `ready/frames`. None is excluded
  by the old `done_arts` list and none uses an alias. The sheet holds only idle; Down and Hover are derived from the same
  frame with the existing RGB transform of the Legacy state difference.
- The top strip `IntTop` 185 is the RES-094 stitched redraw. Its five source tiles reproduced the registered tiles from the
  saved fits; outside the overlaps the tiles match the stitched frame byte for byte.
- Control imports: the default (locked) import reproduced all 13 installed PNGs byte for byte; the `lock: false` import
  differs only by dropping the colour correction to Legacy. Alpha, size and frame identity of every frame equal the installed
  ones. The other 1129 entries and their payload were compared by hash and carried into the new pack, including the 43
  `refinement-02` edits. `provenance.json` ties the 13 frames to their sources, registration, local plan, packs and runtime.

## Verification (macOS, 2026-11-09)

- Native bar smoke on the previous pack (queue 10080) and on the candidate (10083) passed: `ResolveSprite` chose HD for 13/13
  frames; 12 real button states and the normal button actions were checked; the readback geometry matched. The candidate run
  logged the known texture leak message at exit.
- Root locked restore, build and test (10079): 4,513 passed, 48 skipped. The DAT probe and oracle self-tests passed.
- An independent review of the exact diff and of the local builder found nothing that blocked publication.
- The local builder creates a new folder from the verified installed base and the two control imports; its receipt records the
  hashes of base and result. The installer checks running processes, both packs and the live hash before the swap.

## Installation (2026-11-09 11:16, after #882 merged)

Atomic replacement with the previous pack kept under `hd-pack-backups/ARC-UI-116-c5f7a6a47e40` (outside the discovery
folder). Manifest SHA-256 before: `c5f7a6a47e40a09dc51af1bff3feefc1533219d3dd57635c7b882a2464ee5894`; after:
`0fcb407d502a55d3e56ad1bf5d918e137440e022759e9c03ab5a8a903ad1270f`. Manifest and payload of the installed pack were
re-verified after the swap. Later local repairs build on this pack: see [the chain](hd-interface-local-repairs.md).
