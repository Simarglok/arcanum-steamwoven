# ARC-UI-115 — save/load HD alpha composition

Work-Item: ARC-UI-115  
Result: Save/load HD layers composite without added RGB from transparent texels.  
Components: Godot save/load rendering, native/synthetic verification  
Human-Gates: none  
Requirements: FR-PRODUCT-004

## Acceptance and implementation

- AC1: Non-font HD save/load art uses the existing premultiplied path, retaining mip filtering.
- AC2: Layout, hit rectangles, original alpha, font blend, preview/portrait data and save logic remain unchanged.
- AC3: Synthetic framebuffer evidence covers transparent RGB and fractional alpha under the production material; reuse existing regression where sufficient.
- AC4: Bounded native save/load smoke uses isolated saves, verifies the affected HD frames and captures preview/lens/arrows in relevant states; no user progress writes.
- AC5: Relevant and mandatory root checks pass through launch queue; a Codex sub-agent reviews the exact candidate diff.
- AC6: Publish a PR containing code/synthetic evidence only; original-derived images/manifests stay local. Do not merge.

`AreaSaveListArtView._Ready` prepares its canvas with `AreaArtTextDraw.Prepare` (PremultAlpha).
`AttachSaveListArt` now requests `InterfaceHd.Texture(frame, premultipliedAlpha: true)` for every non-font frame:
745, 336, 335, 321, 323, 32, 746, 747 and 748. The existing helper premultiplies HD RGB before generating mipmaps.
The Legacy texture, alpha channel, frame size and HD filtering path are unchanged. Font 27, preview/portrait textures,
coordinates, hit tests and save operations are untouched. This is a presentation correction in both rule modes.
No original-function behaviour or requirement coverage level changes.

## Verification (native macOS, 2026-11-08)

- Root locked restore/build and synthetic tests: PASS, 4,513 passed, 48 skipped, zero failures.
  Skips include opt-in original oracle/real-data and platform-specific cases. Queue receipt: run-000000010058-dotnet.log.
- Terrain viewer locked restore/build: PASS. Separate viewer synthetic tests: PASS, 1,443 passed, zero skipped/failures
  (run-000000010063-dotnet.log).
- DAT probe self-test and original-oracle self-test: PASS; synthetic only, no original executable was read.
- `./arcanum smoke self-test --no-build`: native window PASS (run-000000010061-window.log).
  Reused `TerrainViewerInterfaceHdTest.AssertInterfaceHdAsync`: nonzero RGB in a zero-alpha HD texel reads back as
  zero RGBA through PremultAlpha; the fractional-alpha edge matches ordinary alpha blending within one byte/channel.
  `ARC-UI-114 self-test HD premultiplied canvas PASS` and `ARC-UI-112 self-test interface HD filter PASS` were observed
  with pixel checks enabled, including filtering under a Nearest parent. No duplicate alpha/filter helper was added.
- Native save/load command: `./arcanum smoke pr --stages menu --main-menu` with explicit local `ARC_HD_PACK`,
  `ARC_SAVE_HD_VERIFY=1` and `ARC_AREA_SAVE_CAPTURE` paths. The opt-in check forces HD only in memory and restores
  pack/enabled state in `finally`; every frame of all nine non-font arts must resolve HD.
  The menu smoke owns a temporary save library: it writes its synthetic new-game auto-save there, opens Save without
  committing it, then checks Load and the selected save info/portrait. User saves and settings are not used.
  Final candidate PASS (run-000000010070-window.log): both Save and Load HD markers, 15 local captures.
  Captures cover preview/cover, lens, both arrows idle/hover/down and load info; release away avoids executing arrows.
  The successful menu smoke also reports three shutdown renderer resource messages (texture/RID teardown); no stage
  assertion fails. Local readbacks show the preview cover, lens and arrows without added rectangular RGB glare.
- The separate `scene` stage fails before reaching Save with `InvalidOperationException` / `Upload RenderFailure`
  on both candidate and unchanged base main `317c3768e` (baseline run-000000010068-window.log).
  The unrelated scene transition was not changed or investigated; Save coverage uses the bounded menu probe instead.

Original-derived captures and the installed pack remain local and are excluded from this PR. Installed HD artwork
was read only; no pack import, overwrite or regeneration was performed.

Independent Codex sub-agent review of the final three C# files and this evidence document: no actionable findings.
`git diff --check` passed.
