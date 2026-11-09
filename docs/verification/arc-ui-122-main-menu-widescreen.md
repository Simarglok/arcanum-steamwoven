# ARC-UI-122 — Main menu widescreen

Work item scope: root main-menu art, layout and pointer input. Requirements:
FR-PRODUCT-004, FR-UI-001, FR-UI-007. Human gates: none.

## Art and layout

The original 800 × 600 background concentrates the amber and cobalt light around
the lamp and globe, with dark reddish wood. The previous HD redraw lights the
table more evenly and uses a greyer wood finish. The chosen local plate keeps the
full original frame, cropped props and colours in its centre. The side strips
continue the tabletop and the clipped outlines. The centre compares with zero
differing pixels after uniform scaling to the centre rectangle of each 16:9
preview. Fine texture differences remain visible across the joins at full size.

`AttachMenuArt` loads the original art and the font. The optional widescreen
plate uses the same centred transform as the original 800 × 600 button layout.
Pointer hit tests still work in that control's local coordinates. At 1920 × 1080
the centre measures 1440 × 1080 with 240 pixels on each side. At 2560 × 1440 it
measures 1920 × 1440 with 320 pixels on each side. A narrower viewport crops the
decorative sides first. Legacy, unusable or missing wide art, and submenu windows
keep the whole-factor layout. HD interface is independent of the rules mode and
the world visual profile.

The new plate is a local optional file as described in [HD packs](../formats/hd-pack.md#local-widescreen-main-menu-art).
The PR ships code and synthetic fixtures. The owner-reviewed preview and the
candidate pack stay on the development machine.

## Native and synthetic evidence

- Root locked restore, build and filtered tests passed in queue ticket 10143
  (4,513 passed and 48 skipped by the suites). DAT probe and original oracle
  self-tests passed using synthetic data.
- Native `menu` (10151) and `map` (10153) smokes passed. The menu smoke exercises
  the original art, hover, Single Player, the new-game path and saving/loading.
  Both emitted GL texture shutdown diagnostics, also seen in the baseline package's
  menu smoke.
- Native self-test (10166), synthetic wide plate in the main viewport:
  1920 × 1080, 2560 × 1440 and 1600 × 1000 through one resize sequence.
  Opposite corners contain the background. At every size the label hovers and a
  pointer click opens the synthetic submenu at its existing scale. Missing art,
  invalid aspect, transparency, HD interface off and a missing pack exercise
  the fallback. The viewport uses exact logical render sizes for this check.
- Local candidate at 1920 × 1080 (10158): the HD interface used the candidate
  pack under Original rules and the Legacy world profile. The menu filled the
  viewport. A pointer click opened Single Player and Escape returned to the
  widescreen root menu.
