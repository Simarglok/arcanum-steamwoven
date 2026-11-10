# ARC-UI-131 — Fate Points ring centred in the top strip (local art)

```text
Work-Item: ARC-UI-131
Result: Record the two local versions of the Fate Points ring repair (v1 not installed, v2 installed).
Components: local HD pack (not in git), native bar smoke
Human-Gates: owner approval of v2 on 2026-11-09 (done)
Requirements: FR-PRODUCT-004
```

Local label before renumbering: ARC-UI-120 (that number belongs to the [Combat press feedback](arc-ui-120-combat-press-feedback.md);
the Fate and Sleep press feedback is [ARC-UI-121](arc-ui-121-top-bar-press-feedback.md)). Local sources:
`local/res095/topbar120` and its `v2`. No original or derived art is in git.

## Diagnosis

In Legacy and in both ARC-UI-116 imports the `IntTop` hole is centred at (168.50, 19.50) in strip coordinates, and locked and
unlocked imports share one alpha. Art 137 has its own gold rim on the right. The colour change of ARC-UI-116 made the
asymmetry more visible.

## v1 (candidate, never installed)

Base manifest `eb62bff7…`; candidate manifest `af2f6fe3…`. The pilot compares two offsets; −5 and +1 texels in X and Y of the
4× texture were chosen. Shifting RGB and alpha together failed the native smoke (10092): the `legacySmooth` mode needs the
alpha to match away from the Legacy border. The accepted variant moves the colour and extends the inner rim colour in a narrow
band while keeping the original alpha. Only the RGB of 8,147 pixels in the Fate Points mask changed; `IntTop` stays 3200×164
with full alpha, the same identity and Legacy metadata; the Sleep socket, art 137 and the other 1,141 entries are
byte-identical. Native bar smoke on the base (10091) and the candidate (10096) passed (13 HD frames, 12 states, Fate click); 455
pixels changed per pair; the visible brass rim on the left grew from 1 to 7 pixels and shrank on the right from 17 to 12. An
independent review found no blocking issue. v1 was superseded by v2 before any installation.

## v2 (installed)

The owner asked for one more pixel to the left. The brass ring is shifted 1 px further left than v1, and the extra shift on the
right fades into v1 at the join with the edge of art 137 (pilots at 1, 1.5 and 2 px, a join variant). Against the actual base
`fe3a8dc7…` (with the slot repair) only 8,151 RGB pixels in the Fate Points mask differ; the 39,639 pixels of the slot repair,
five `sp_plugged` entries, alpha, size, metadata, art 137, Sleep, the other 1,141 entries and the recipe are byte-identical.
An independent review reproduced the PNG byte for byte. Native bar smoke on the base (10107) and on v2 (10108) passed
(13 HD frames, 12 states, Fate click); all 12 pairs kept the geometry and changed 464 pixels each, all inside the socket; the mean
X of the visible left edge moved from 156.786 to 156.000 and the right band stayed at 12 pixels.

## Installation (2026-11-09 11:37)

By the owner's request after the game was closed. Because Combat and the slots had been installed meanwhile, the pack was
rebuilt over `6f2c2ffb…`, which kept their later repairs; the Fate area is byte-identical to the approved v2. Result manifest
`f3fa4748…`. Native bar smoke 10114 passed. Manifest, recipe and all entries of the installed pack and the backup were re-verified
(hashes, paths and entry counts in `install-rebase-6f2c2ffb/installed.json`). No tracked diff, so no root checks.
