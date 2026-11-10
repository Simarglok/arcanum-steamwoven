# ARC-UI-125 — Journal window in HD: no brightened pages

Work-Item: ARC-UI-125
Result: The Journal (logbook) window draws its HD art on its premultiplied canvas without adding the RGB of transparent texels to the page.
Components: viewer logbook window, HD interface texture, bar smoke
Human-Gates: none
Requirements: FR-PRODUCT-004

Finding: the Journal is two layers, `Book_Main` (art 260, the opaque book) and `Book_Quests` (art 261, the leather rim over it; about 90 % of its
texels are transparent). In the HD pack the two layers are registered to each other (best offset 0, 0 over the rim, alpha equal to the Legacy mask), so
the pack frames and the anchors are not the fault. The window's canvas is prepared for art text (`AreaArtTextDraw.Prepare`, PremultAlpha), and the
logbook loaded its frames as straight-alpha textures. A PremultAlpha canvas adds the RGB of a transparent HD texel to what is under it, and the
transparent texels of the HD rim carry a dark brown, so the pages came out lighter and pinker (page mean R/G/B +50/+32/+29 against the pack frame in an
in-app readback; the wood, drawn by an opaque layer, was equal). ARC-UI-114 fixed the same thing for the character sheet.

Change: `LogbookArtLook` builds its frames with `InterfaceHd.Texture(frame, premultipliedAlpha: true)`; the bar smoke requires every HD frame of the
logbook to be premultiplied, and `ARC_AREA_LOGBOOK_HD_CAPTURE=PATH` saves an in-app readback of the open window (local, ignored path) for layer comparison.
After the change the page mean matches the pack frame within 0.3 per channel. (The measurements in this note are from local readbacks of the owner's pack and
installation, not reproducible from git; the art numbers 260/261 and the names `Book_Main`/`Book_Quests` are the pack plan's.)

Not covered: the world map, the premade-hero browser and the main menu art views also call `AreaArtTextDraw.Prepare` and load their frames through
`HotkeyTexture` (straight alpha); whether they show the same fault is not checked here and belongs to their own tasks. No original or derived art is added. Coverage status of FR-PRODUCT-004 is unchanged (partial).
