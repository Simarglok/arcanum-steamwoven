# ARC-UI-130 — Combat button centred in its socket (local art)

```text
Work-Item: ARC-UI-130
Result: Record the local centring of the three Combat frames (art 470) and its two installations.
Components: local HD pack (not in git), native bar smoke
Human-Gates: owner remark on the idle/hover bevel (done)
Requirements: FR-PRODUCT-004
```

Local label before renumbering: ARC-UI-119. Local sources: `local/res095/combat119`. No original or derived art is in git.

## Finding and repair

`AreaGameBarLayout` anchors button 470 at (86, 457) and the backing 184 at (0, 441). The mask centres of the button and the
socket coincide at about (105.827, 476.785); the visible offset lives in the RGB of the green disc and the shield. The drawing is
moved one logical pixel left and down, the transition to the shadow is centred, and the upper-left bevel is restored in idle and
hover after the owner noticed its loss. Alpha is byte-identical. The button is 39×39 in the game and 156×156 in the pack.

## Verification (macOS, 2026-11-09)

- Native bar smoke on the previous HD pack (10095), on Legacy (10103) and on the result (10109) passed. The last run confirmed
  four HD frames, all three states, the unchanged geometry and entering and leaving combat. Each run logged the texture leak
  message at exit.
- `native-comparison.png` (local) shows original, previous HD and result in idle, down and hover at one scale.
- Against the slot repair's base, exactly three PNGs and the manifest differ; 1,139 other entries are kept. The second
  installation changes two frames only (idle and hover); down equals the first one byte for byte.
- The native captures used an isolated checkout at `f968fba06` with a temporary smoke adaptation (`native-capture-harness.patch`);
  it was removed and left no tracked diff. No root checks or PR were needed for local art.

## Installation (2026-11-09)

Two stages under the shared lock, each checking that the game was closed, the live base, every file and then an atomic swap:
11:30 centring of all three frames (`fe3a8dc7…` → `a2a99ce4…`), then 11:32 the bevel fix (→ `ae812c77…`). Backup links in the
receipts `installed.json` and `installed-bevel.json`. The owner then reported that a quick click showed no Down frame, which is
code, not art: [ARC-UI-120](arc-ui-120-combat-press-feedback.md). See [the chain](hd-interface-local-repairs.md).
