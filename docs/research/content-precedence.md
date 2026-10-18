# Resource source precedence (DAT, PATCH, module, loose files)

Status: **Research + bounded API**, work item ARC-DATA-011 (2026-10-16).
Requirements: [FR-CONTENT-001](../requirements/content-product.md#fr-content-001) and
[FR-CONTENT-002](../requirements/content-product.md#fr-content-002).
This document answers which physical copy of a resource the original runtime opens
when several archives and folders hold the same name, records the observed
overrides of the owner's installation, and states which Crash Site inputs they
touch. The API is [`src/Arcanum.Data/Content`](../../src/Arcanum.Data/Content). The
Area loader, NPC conversation and viewer are **not** rewired by this work.

## Vocabulary and boundary

- **Source fact**: an earlier source reading (provenance in git history), cited by
  the executable address of its function (the file and archive functions of the tig
  library are named in words, without an address); level F0 (not checked in the
  executable) unless a ledger row or check is cited. It is not an observation of
  the running original.
- **Local observation**: measured by `tools/research/Arcanum.ContentProbe` on the
  owner's unchanged installation (GOG files). The probe prints paths, member
  names, sizes and SHA-256 digests only, never payload bytes.
- **Project policy**: a choice made here where the source leaves a decision open or
  where fidelity cannot be modelled; each is listed under [Limits](#limits-and-not-ready).

The API and probe were written from the rules stated here.

## Mount sequence (source facts)

Every mount pushes a source on **top** of a list; lookups walk the list from the top.
Re-adding a mounted path moves it to the top
(the file layer's mount function).

1. `tig.dat`, then the game folder `.` (the file layer's init).
2. Every `arcanum*.dat` in the game folder, in case-insensitive name order, then the
   `data` folder (`gamelib_load_data` 0x404A20,
   called from `gamelib_init` 0x4020F0). The name list is
   gathered through the already mounted sources and kept sorted and de-duplicated
   case-insensitively (the file layer's listing functions).
3. Module `Arcanum` (the default of the game library; `-mod:NAME`
   overrides it in `main`, 0x401000):
   `Modules/NAME.dat`; only if it exists, `Modules/NAME.patch0`..`patch9` in digit
   order; then the folder `Modules/NAME`
   (`gamelib_load_module_data` 0x404C10,
   from `gamelib_mod_load` 0x402A50).
   Missing `data` and module folders are created; this project never writes, omits a
   missing `data` folder and refuses a module with neither `.dat` nor folder.

Search order is therefore, highest priority first:
module folder › `patch9` … `patch0` › module `.dat` › `data/` › last-named
`arcanum*.dat` … first › game folder › `tig.dat`. Multiplayer mounts
(0x49CC70, 0x49D590) are out of scope.

## Lookup rule (source facts and derivation)

- **Archive member names** are ASCII-lowercased when an archive is opened (the
  Windows build keeps backslashes; the earlier reading's non-Windows port turned them
  into slashes),
  and every member gets the "check loose first" flag `0x100`
  (the archive open function). Lookup is a
  binary search over the lowercased names; a member flagged `0x800` is treated as
  absent (the archive lookup). A
  sidecar `ARCHIVE.ignore` file sets `0x800` by pattern
  (the archive's ignore-file reader).
- **Open for reading** (the file layer's open function):
  find the topmost archive holding the name. If its member still carries `0x100`/`0x200`,
  try every folder **above that archive**, top-down; the first loose file found wins
  (and the member is marked `0x200`). Otherwise the archive wins and both flags are
  cleared, so later opens never look for a loose file again. With no archive holding
  the name, the topmost folder holding it wins. Folder names match without regard to
  letter case: the Windows original relies on its file system; the earlier reading's
  non-Windows port resolved each segment itself.
- **Exists** (the file layer's exists test) and
  **extract**, used for sounds and movies (the file layer's extract function,
  called by the sound library and by `gmovie_play_path` 0x40DEE0),
  take the first source in plain search order, archive or folder.
- Mounting a folder deletes its top-level `TIGCache` folder
  (the mount function), so cached extracts
  are never seen as candidates after a mount, also not through an enclosing folder
  such as the game folder.
- A script's file name comes from listing `scr\*.scr` across all sources; the sorted,
  de-duplicated list maps each number to a name, and a later name with the same number
  replaces an earlier one (`script_name_init` 0x4CEC10, `script_name_mod_load` 0x4CECE0).
  Numbers from 1000 up are listed at game start, before the module is mounted, so only
  names held by `tig.dat`, the game folder, `arcanum*.dat` and `data/` count for them;
  1..999 are listed at module load, over every source. Checked in the 1.0.7.4 executable
  (ARC-SCRIPT-011): `script_name_init` 0x4CEC10 keeps numbers ≥ 1000, `script_name_mod_load`
  0x4CECE0 keeps 1..999, both replace an earlier name and report the duplicate, and
  `script_name_build_scr_name` 0x4CEDC0 looks up ≥ 1000 in the first table and 1..999 in
  the second. The mount timing (checked in the executable by ARC-DATA-026): `gamelib_init`
  (0x4020F0) loads the base data before it runs the init functions, and
  `gamelib_mod_load` (0x402A50) mounts the module before it runs the module-load
  functions.
  The dialog name is the script name with `scr` swapped for `dlg`
  (`script_name_build_dlg_name` 0x4CEE60); the `dlg\*.dlg`
  listing in `dialog_check` (0x418030) is a debug check only. Both
  files are then opened through the rule above (the script cache, 0x44C4E0).
- Prototypes (`proto\*.pro`) are all listed and read once at game start, before the
  module is mounted, and never at module load: only `tig.dat`, the game folder,
  `arcanum*.dat` and `data/` count for them, and a module's copy is never read.
  Checked in the 1.0.7.4 executable (ARC-DATA-026, ledger row "Prototype phase");
  implemented by `ContentPrototypes` ([map catalogue](../world/map-catalog.md#prototypes)).
- The same holds for every file an init function reads and keeps: the original reads it from
  the first base source, and a module copy is never read; a file read at module load or later
  is the stack's winner. `ContentPhase`/`ContentLoadPhase` (ARC-DATA-027) applies this in the
  prototype source, the description catalogue, the lighting readers, the starting hero's tables,
  the whole-map step rules and the whole-map plans' name tables; the
  files checked so far are in the ledger's
  [load phases](original-verification-ledger.md#load-phases-arc-data-027) (effect, level and
  quest experience, inventory sources, main-menu and light/tile name tables at game start;
  game-init, map list, lighting schemes and the module quest table at module load; scheme
  files and `mapinfo.txt` while the game runs).

**Derivation.** On a static installation, a folder above the topmost archive that holds
a name is, by construction, earlier in search order than that archive; if no folder
above holds it, that archive is the first holder. So the first-open read rule and the
exists/extract rule select the same copy: **the first source in search order that holds
the path wins**. The API implements exactly this single rule.

## API (`Arcanum.Data.Content`)

- `ContentMountPlan.Discover(gameRoot, module = "Arcanum")` returns a typed
  `ContentMountDiscovery` (`Ready`, `MissingGameRoot`, `MissingTig`, `MissingModule`,
  `InvalidModuleName`, `AmbiguousName`, `UnsupportedLayout`, `Unreadable`) with the
  search order.
- `ContentStack.Open(plan, options)` opens every archive with `DatReaderOptions`, indexes
  members and loose files once, and returns `ContentStackOpenResult` (`Ready`,
  `ArchiveUnreadable`, `UnsortedArchiveIndex`, `UnsupportedEntryName`,
  `IgnoreListUnsupported`, `DirectoryRecordCollision`, `LooseLimitExceeded`,
  `AmbiguousLooseName`, `LooseUnreadable`).
- `Resolve(path)` → `ContentResolution`: `Resolved`/`NotFound`/`InvalidPath`, the
  normalized key, every candidate in search order, `Winner` and `Shadowed`.
  `Overrides()` lists every key held by two or more sources. `Read(candidate)` returns
  `ContentReadResult` (`Read`, `TooLarge`, `Unreadable`) and accepts only candidates
  this stack issued.

Tests: [synthetic](../../tests/Arcanum.Data.Tests/Content/ContentStackTests.cs) (order,
patch-only-with-module, case and separator handling, invalid paths, directory/ignored
records, TIGCache, sort refusal, ignore list, limits, reads, disposal) and
[real data](../../tests/Arcanum.Data.Tests/Content/ContentStackRealDataTests.cs)
(category `RealData`, `DAT_ROOT` = the game folder).

## Limits and NOT READY

- **Snapshot.** Archives and loose files are indexed once at `Open`. The runtime's
  sticky first-open flag, files written while the game runs (saves, extracts) and the
  writable-folder branch for mode `w` are not modelled.
- **Refused instead of guessed** (project policy): an archive whose lowercased names are
  not strictly ordered in both the backslash and slash form (the original's binary
  search could miss members), a non-printable-ASCII member name, any `.ignore`
  sidecar (pattern semantics not implemented), two loose names or two mount names that
  differ only in case (the original takes the first directory entry, which is
  unordered), an archive directory record (`0x400`) whose name is also a file somewhere
  in the stack (the original's member lookup would return the record), and a folder
  named `tig.dat`, `arcanum*.dat`, `NAME.dat` or `NAME.patchN`. The original skips
  unreadable `arcanum*.dat` files and creates a missing module folder; this API refuses
  both (`ArchiveUnreadable`, `MissingModule`).
- **Script names.** Two different `.scr` names with the same number would be decided by
  name order, not by search order. The owner's installation has none; the API does not
  map script numbers to names.
- **Not indexed**: archive directory records (`0x400`, checked only for the collision
  above), symbolic links, loose names
  outside printable ASCII (counted in `SkippedLooseFiles`), files under a folder's
  top-level `TIGCache` (counted in `ExcludedCacheFiles`). Paths that start with
  `.`, `/`, `\` or a drive letter are opened directly by the original and are
  `InvalidPath` here.
- The mount order of `arcanum*.dat` names uses lowercase ordinal comparison; a name
  containing one of the ASCII characters between `Z` and `a` (brackets, backslash,
  caret, underscore, backtick) could sort differently in the original. None was
  observed.
- Save-game `.dif` files, `map.patch` application, `-mod:` selection UI and other
  modules than `Arcanum` are not covered. Original executable parity: **NOT_VERIFIED**.

## Observed installation (local observation)

| search | role | source | bytes | sha256 |
|---:|---|---|---:|---|
| 1 | module folder | `modules/arcanum/` | folder | |
| 2 | patch0 | `modules/arcanum.patch0` | 18,804 | `d9a3313f71d78115dcac489296bf3d8a5801dd55b20267bd6017b7a898364bce` |
| 3 | module | `modules/arcanum.dat` | 355,938,305 | `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` |
| 4 | data folder | `data/` | folder | |
| 5 | base[3] | `arcanum4.dat` | 447,614 | `975d43854fca8b7dd74e2b12ef2ff3fe6d3c0f99dc541151d33001469f89710c` |
| 6 | base[2] | `arcanum3.dat` | 9,605,011 | `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499` |
| 7 | base[1] | `arcanum2.dat` | 147,991,122 | `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` |
| 8 | base[0] | `arcanum1.dat` | 447,470,816 | `6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576` |
| 9 | game folder | `./` | folder | |
| 10 | tig | `tig.dat` | 57,149 | `c9b48eb6396c514eba3805fb4847df67db3c67ef9f13a79ea21bbf7dd329a09b` |

keys: 97,630; excluded TIGCache files: 45; skipped non-ASCII loose names: 0
overridden paths: 109

All **109** overridden paths are archive members; no loose file overrides or is
overridden by anything. **None** of the 109 copies is byte-identical to what it shadows.

| chain (winner first) | paths | identical payloads |
|---|---:|---:|
| `arcanum4.dat > arcanum3.dat` | 63 | 0 |
| `arcanum4.dat > arcanum2.dat` | 43 | 0 |
| `modules/arcanum.patch0 > modules/arcanum.dat` | 2 | 0 |
| `modules/arcanum.patch0 > arcanum4.dat > arcanum3.dat` | 1 | 0 |
`arcanum4.dat` is a base patch archive: it replaces 47 DLG, 39 SCR, 4 sound and 16
MES/TXT members of `arcanum3.dat`/`arcanum2.dat`. `Arcanum.PATCH0` replaces
`mes/gamequestlog.mes` and `rules/wmap_rnd.mes` of the module, and `rules/unique.txt`
over both base copies; its `rules/map.patch` exists nowhere else.

## Crash Site impact

| Consumer | Inputs it reads | Original winner | Result |
| --- | --- | --- | --- |
| Area loader (exterior and cave, ARC-DATA-009 / ARC-WORLD-009) | sectors and maps from `modules/Arcanum.dat`; ART and the 18 pinned name/rule tables from `arcanum1.dat`/`arcanum2.dat` by ordinal | the same members: nothing in `arcanum1.dat` is shadowed, `arcanum2.dat` loses only `scr/` and `sound/` members, the module loses only the two PATCH0 files | **Not affected.** Checked per binding by the real-data test. |
| Area prototypes and game-generated mobiles | `data/proto/*.pro`, `modules/Arcanum/maps/*` loose files | the same loose files, one copy each | **Not affected.** |
| Virgil conversation (ARC-SCRIPT-005) | until ARC-SCRIPT-011: `scr\01324Virgil.scr` from `arcanum2.dat` #8973 (6,268 B) | `Arcanum4.dat` #86 (6,532 B, different payload) | **Fixed by ARC-SCRIPT-011:** the viewer now reads scripts and dialogs through the stack ([script content](../scripting/script-content.md)) and gets the `Arcanum4.dat` copy (49 lines: the shadowed copy's 47 lines unchanged and two added, so Virgil's talk entries did not change). `dlg\01324Virgil.dlg` (`arcanum3.dat` #160) has one copy. |
| Passability closure (`map.patch`) | `Arcanum.PATCH0` `rules/map.patch` | the same member, one copy | Not affected. |

Three more Virgil scripts (`01708`, `02023`, `02033`) are also replaced by
`Arcanum4.dat`; they are not used at the Crash Site start. `oemes/oname.mes`,
`mes/spell.mes`, `rules/backgrnd.mes`, `rules/gamelevel.mes`, `rules/xp_level.mes`,
`rules/monster.txt`, `rules/npc.txt` and `sound/snd_critter.mes` are replaced too and
matter to future item, character and audio work. This settles the open question of
[character creation](character-creation-evidence.md) (ARC-RES-025): the `Arcanum4.dat`
copies of `rules/backgrnd.mes` and `mes/mainmenu.mes` are the ones the game reads.

The follow-up named here is done: ARC-SCRIPT-011 moved the conversation's script and
dialog lookup onto `ContentStack` and re-checked Virgil's talk with the patched script.
Future loaders should resolve names through the stack instead of fixed archive roles.

## Reproduce

```bash
dotnet run --project tools/research/Arcanum.ContentProbe/Arcanum.ContentProbe.csproj -- --self-test
dotnet run --project tools/research/Arcanum.ContentProbe/Arcanum.ContentProbe.csproj -- --game-root "$DAT_ROOT"
DAT_ROOT="$DAT_ROOT" dotnet test tests/Arcanum.Data.Tests --filter "FullyQualifiedName~ContentStackRealData"
```

The report run took about 1.5 s; all archive fingerprints matched before and after.

## Appendix: every overridden path

| path | winner | shadowed | payload |
|---|---|---|---|
| `dlg/01012blackroot_apothecary.dlg` | `arcanum4.dat`#4 (7,828 B) | `arcanum3.dat`#11 (7,828 B) | differs |
| `dlg/01040ristezze.dlg` | `arcanum4.dat`#5 (46,935 B) | `arcanum3.dat`#27 (46,933 B) | differs |
| `dlg/01041lukan.dlg` | `arcanum4.dat`#6 (34,290 B) | `arcanum3.dat`#28 (34,290 B) | differs |
| `dlg/01046magnus_shale_fist.dlg` | `arcanum4.dat`#7 (64,002 B) | `arcanum3.dat`#32 (63,990 B) | differs |
| `dlg/01054pelonious_schuyler.dlg` | `arcanum4.dat`#8 (3,926 B) | `arcanum3.dat`#36 (3,916 B) | differs |
| `dlg/01067randver_thunder_stone.dlg` | `arcanum4.dat`#9 (58,830 B) | `arcanum3.dat`#45 (58,830 B) | differs |
| `dlg/01074raven.dlg` | `arcanum4.dat`#10 (118,545 B) | `arcanum3.dat`#51 (118,560 B) | differs |
| `dlg/01078hallofrecordsclerk.dlg` | `arcanum4.dat`#11 (16,033 B) | `arcanum3.dat`#54 (16,010 B) | differs |
| `dlg/01087mingorad.dlg` | `arcanum4.dat`#12 (51,593 B) | `arcanum3.dat`#63 (51,669 B) | differs |
| `dlg/01110stringy_pete.dlg` | `arcanum4.dat`#13 (32,632 B) | `arcanum3.dat`#76 (32,425 B) | differs |
| `dlg/01268molly_williamson.dlg` | `arcanum4.dat`#14 (8,394 B) | `arcanum3.dat`#137 (8,374 B) | differs |
| `dlg/01269edwin_wallows.dlg` | `arcanum4.dat`#15 (17,581 B) | `arcanum3.dat`#138 (17,581 B) | differs |
| `dlg/01287cassandrapettibone.dlg` | `arcanum4.dat`#16 (25,798 B) | `arcanum3.dat`#147 (25,798 B) | differs |
| `dlg/01291evelyn_garringsburg.dlg` | `arcanum4.dat`#17 (16,733 B) | `arcanum3.dat`#149 (16,723 B) | differs |
| `dlg/01358geoffrey_tarellond-ashe.dlg` | `arcanum4.dat`#18 (35,169 B) | `arcanum3.dat`#171 (35,170 B) | differs |
| `dlg/01363isleofdespairguard.dlg` | `arcanum4.dat`#19 (9,946 B) | `arcanum3.dat`#172 (9,927 B) | differs |
| `dlg/01374ellumyn.dlg` | `arcanum4.dat`#20 (9,335 B) | `arcanum3.dat`#177 (9,334 B) | differs |
| `dlg/01394winde.dlg` | `arcanum4.dat`#21 (10,843 B) | `arcanum3.dat`#186 (10,841 B) | differs |
| `dlg/01397whysper.dlg` | `arcanum4.dat`#22 (11,203 B) | `arcanum3.dat`#189 (11,221 B) | differs |
| `dlg/01461damianmaug.dlg` | `arcanum4.dat`#23 (16,371 B) | `arcanum3.dat`#210 (16,375 B) | differs |
| `dlg/01471vegard_moltenflow.dlg` | `arcanum4.dat`#24 (19,233 B) | `arcanum3.dat`#216 (19,251 B) | differs |
| `dlg/01497clarissa_shalmo.dlg` | `arcanum4.dat`#25 (22,523 B) | `arcanum3.dat`#227 (22,520 B) | differs |
| `dlg/01633gildor_nightwalk.dlg` | `arcanum4.dat`#26 (22,294 B) | `arcanum3.dat`#294 (22,264 B) | differs |
| `dlg/01644sogg_mead_mug.dlg` | `arcanum4.dat`#27 (20,748 B) | `arcanum3.dat`#298 (20,750 B) | differs |
| `dlg/01646perriman2.dlg` | `arcanum4.dat`#28 (45,414 B) | `arcanum3.dat`#300 (45,416 B) | differs |
| `dlg/01649zan_alurin.dlg` | `arcanum4.dat`#29 (38,597 B) | `arcanum3.dat`#302 (38,598 B) | differs |
| `dlg/01650vollinger.dlg` | `arcanum4.dat`#30 (27,298 B) | `arcanum3.dat`#303 (27,300 B) | differs |
| `dlg/01677franklin_payne.dlg` | `arcanum4.dat`#31 (47,492 B) | `arcanum3.dat`#315 (47,494 B) | differs |
| `dlg/01708virgil_in_caladon_cemetery.dlg` | `arcanum4.dat`#32 (2,267 B) | `arcanum3.dat`#323 (2,265 B) | differs |
| `dlg/01718theo_brightstart.dlg` | `arcanum4.dat`#33 (16,107 B) | `arcanum3.dat`#328 (16,067 B) | differs |
| `dlg/01730mrs_cameron.dlg` | `arcanum4.dat`#34 (17,447 B) | `arcanum3.dat`#332 (17,440 B) | differs |
| `dlg/01758king_loghaire.dlg` | `arcanum4.dat`#35 (20,793 B) | `arcanum3.dat`#345 (20,794 B) | differs |
| `dlg/01769mr_langley.dlg` | `arcanum4.dat`#36 (11,097 B) | `arcanum3.dat`#356 (11,065 B) | differs |
| `dlg/01773mrplough.dlg` | `arcanum4.dat`#37 (6,624 B) | `arcanum3.dat`#360 (6,686 B) | differs |
| `dlg/01875raven_lover_stuff.dlg` | `arcanum4.dat`#38 (9,089 B) | `arcanum3.dat`#424 (9,088 B) | differs |
| `dlg/01918magnus_in_the_iron_clan.dlg` | `arcanum4.dat`#39 (5,057 B) | `arcanum3.dat`#457 (5,084 B) | differs |
| `dlg/01955generic_beggar.dlg` | `arcanum4.dat`#40 (1,824 B) | `arcanum3.dat`#477 (1,834 B) | differs |
| `dlg/01957game_of_witt.dlg` | `arcanum4.dat`#41 (35,798 B) | `arcanum3.dat`#479 (35,695 B) | differs |
| `dlg/02026donnthrogg.dlg` | `arcanum4.dat`#42 (20,488 B) | `arcanum3.dat`#517 (20,468 B) | differs |
| `dlg/02510petes_ship_wheel_teleports.dlg` | `arcanum4.dat`#43 (584 B) | `arcanum3.dat`#592 (581 B) | differs |
| `dlg/02676university_book_store.dlg` | `arcanum4.dat`#44 (3,118 B) | `arcanum3.dat`#614 (3,118 B) | differs |
| `dlg/02834sebastian.dlg` | `arcanum4.dat`#45 (28,989 B) | `arcanum3.dat`#641 (28,990 B) | differs |
| `dlg/02869shipbuilder.dlg` | `arcanum4.dat`#46 (7,145 B) | `arcanum3.dat`#646 (7,201 B) | differs |
| `dlg/02886jayna_stiles.dlg` | `arcanum4.dat`#47 (22,036 B) | `arcanum3.dat`#656 (22,038 B) | differs |
| `dlg/02900prison_warden.dlg` | `arcanum4.dat`#48 (4,092 B) | `arcanum3.dat`#661 (4,072 B) | differs |
| `dlg/02901jerrold_aymes.dlg` | `arcanum4.dat`#49 (3,935 B) | `arcanum3.dat`#662 (3,927 B) | differs |
| `dlg/02902guido_lightouch.dlg` | `arcanum4.dat`#50 (3,434 B) | `arcanum3.dat`#663 (3,426 B) | differs |
| `mes/gamequestlog.mes` | `modules/arcanum.patch0`#1 (16,270 B) | `modules/arcanum.dat`#14912 (16,246 B) | differs |
| `mes/item_effect.mes` | `arcanum4.dat`#54 (13,560 B) | `arcanum3.dat`#751 (13,561 B) | differs |
| `mes/mainmenu.mes` | `arcanum4.dat`#55 (26,704 B) | `arcanum3.dat`#755 (26,676 B) | differs |
| `mes/spell.mes` | `arcanum4.dat`#57 (23,754 B) | `arcanum3.dat`#768 (23,607 B) | differs |
| `module template/mes/gamearea.mes` | `arcanum4.dat`#60 (605 B) | `arcanum3.dat`#835 (395 B) | differs |
| `module template/mes/gamenewspaper.mes` | `arcanum4.dat`#61 (3,286 B) | `arcanum3.dat`#842 (6,593 B) | differs |
| `oemes/oname.mes` | `arcanum4.dat`#66 (23,594 B) | `arcanum3.dat`#899 (23,552 B) | differs |
| `rules/backgrnd.mes` | `arcanum4.dat`#68 (7,241 B) | `arcanum3.dat`#1068 (7,241 B) | differs |
| `rules/gamelevel.mes` | `arcanum4.dat`#69 (18,492 B) | `arcanum3.dat`#1073 (18,506 B) | differs |
| `rules/invensource.mes` | `arcanum4.dat`#70 (35,149 B) | `arcanum3.dat`#1074 (35,008 B) | differs |
| `rules/invensourcebuy.mes` | `arcanum4.dat`#71 (23,869 B) | `arcanum3.dat`#1075 (22,811 B) | differs |
| `rules/monster.txt` | `arcanum4.dat`#72 (107,615 B) | `arcanum3.dat`#1079 (107,504 B) | differs |
| `rules/npc.txt` | `arcanum4.dat`#73 (74,003 B) | `arcanum3.dat`#1080 (74,006 B) | differs |
| `rules/schematic.mes` | `arcanum4.dat`#74 (27,492 B) | `arcanum3.dat`#1082 (27,492 B) | differs |
| `rules/spelllist.mes` | `arcanum4.dat`#75 (160,558 B) | `arcanum3.dat`#1085 (162,769 B) | differs |
| `rules/unique.txt` | `modules/arcanum.patch0`#4 (55,503 B) | `arcanum4.dat`#76 (55,464 B), `arcanum3.dat`#1087 (55,464 B) | differs |
| `rules/wmap_rnd.mes` | `modules/arcanum.patch0`#5 (22,373 B) | `modules/arcanum.dat`#14938 (19,957 B) | differs |
| `rules/xp_level.mes` | `arcanum4.dat`#77 (1,032 B) | `arcanum3.dat`#1091 (1,017 B) | differs |
| `scr/01087mingorad.scr` | `arcanum4.dat`#79 (5,608 B) | `arcanum2.dat`#8745 (5,344 B) | differs |
| `scr/01089alexander_the_acolyte.scr` | `arcanum4.dat`#80 (2,440 B) | `arcanum2.dat`#8747 (2,440 B) | differs |
| `scr/01094vendigrothpickup.scr` | `arcanum4.dat`#81 (724 B) | `arcanum2.dat`#8752 (460 B) | differs |
| `scr/01105kerghan.scr` | `arcanum4.dat`#82 (14,188 B) | `arcanum2.dat`#8763 (13,924 B) | differs |
| `scr/01198gianttrapuse.scr` | `arcanum4.dat`#83 (856 B) | `arcanum2.dat`#8852 (592 B) | differs |
| `scr/01241ristezze_receipt_ins.scr` | `arcanum4.dat`#84 (988 B) | `arcanum2.dat`#8895 (988 B) | differs |
| `scr/01275bangellian_scourge_des.scr` | `arcanum4.dat`#85 (724 B) | `arcanum2.dat`#8929 (724 B) | differs |
| `scr/01324virgil.scr` | `arcanum4.dat`#86 (6,532 B) | `arcanum2.dat`#8973 (6,268 B) | differs |
| `scr/01361geoffrey_tile_level1.scr` | `arcanum4.dat`#87 (1,912 B) | `arcanum2.dat`#9005 (1,780 B) | differs |
| `scr/01362geoffrey_tile_level2.scr` | `arcanum4.dat`#88 (1,648 B) | `arcanum2.dat`#9006 (1,516 B) | differs |
| `scr/01461damianmaug.scr` | `arcanum4.dat`#89 (2,440 B) | `arcanum2.dat`#9104 (2,044 B) | differs |
| `scr/01523sewer guarddoor.scr` | `arcanum4.dat`#90 (2,176 B) | `arcanum2.dat`#9165 (1,912 B) | differs |
| `scr/01527murderguarddoor.scr` | `arcanum4.dat`#91 (1,648 B) | `arcanum2.dat`#9169 (1,384 B) | differs |
| `scr/01528vincentdeath.scr` | `arcanum4.dat`#92 (1,780 B) | `arcanum2.dat`#9170 (1,648 B) | differs |
| `scr/01708virgil_in_caladon_cemetery.scr` | `arcanum4.dat`#93 (1,780 B) | `arcanum2.dat`#9347 (1,384 B) | differs |
| `scr/01764cassie.scr` | `arcanum4.dat`#94 (5,344 B) | `arcanum2.dat`#9402 (5,344 B) | differs |
| `scr/01830dog_death.scr` | `arcanum4.dat`#95 (460 B) | `arcanum2.dat`#9466 (328 B) | differs |
| `scr/01918magnus_in_the_iron_clan.scr` | `arcanum4.dat`#96 (3,892 B) | `arcanum2.dat`#9553 (3,232 B) | differs |
| `scr/01953arthurtyron.scr` | `arcanum4.dat`#97 (1,120 B) | `arcanum2.dat`#9588 (856 B) | differs |
| `scr/01987beggar_hb.scr` | `arcanum4.dat`#98 (2,176 B) | `arcanum2.dat`#9622 (2,176 B) | differs |
| `scr/01992tailor_city.scr` | `arcanum4.dat`#99 (3,760 B) | `arcanum2.dat`#9627 (3,760 B) | differs |
| `scr/02023virgil_leaves_the_pc_tile2.scr` | `arcanum4.dat`#100 (2,176 B) | `arcanum2.dat`#9657 (1,516 B) | differs |
| `scr/02026donnthrogg.scr` | `arcanum4.dat`#101 (3,100 B) | `arcanum2.dat`#9660 (2,968 B) | differs |
| `scr/02033zen_virgil_hb.scr` | `arcanum4.dat`#102 (1,120 B) | `arcanum2.dat`#9667 (856 B) | differs |
| `scr/02078magnus_in_the_iron_clan_tile.scr` | `arcanum4.dat`#103 (724 B) | `arcanum2.dat`#9712 (724 B) | differs |
| `scr/02103gem_of_malachi_rench_ins.scr` | `arcanum4.dat`#104 (2,440 B) | `arcanum2.dat`#9737 (2,044 B) | differs |
| `scr/02128jormunddeath.scr` | `arcanum4.dat`#105 (592 B) | `arcanum2.dat`#9760 (328 B) | differs |
| `scr/02151velorien_altar_tile.scr` | `arcanum4.dat`#106 (1,648 B) | `arcanum2.dat`#9783 (1,384 B) | differs |
| `scr/02155stringy_pete_death.scr` | `arcanum4.dat`#107 (1,120 B) | `arcanum2.dat`#9787 (1,120 B) | differs |
| `scr/02179murgo_sector.scr` | `arcanum4.dat`#108 (2,044 B) | `arcanum2.dat`#9810 (2,044 B) | differs |
| `scr/02191velorien_resurrect_script.scr` | `arcanum4.dat`#109 (460 B) | `arcanum2.dat`#9821 (460 B) | differs |
| `scr/02510petes_ship_wheel_teleports.scr` | `arcanum4.dat`#110 (2,044 B) | `arcanum2.dat`#10135 (1,252 B) | differs |
| `scr/02756renzohb.scr` | `arcanum4.dat`#111 (724 B) | `arcanum2.dat`#10377 (592 B) | differs |
| `scr/02764renzokos.scr` | `arcanum4.dat`#112 (1,252 B) | `arcanum2.dat`#10385 (988 B) | differs |
| `scr/02798void-dragon-hb.scr` | `arcanum4.dat`#113 (3,364 B) | `arcanum2.dat`#10418 (1,252 B) | differs |
| `scr/02806halcyon_altar_use.scr` | `arcanum4.dat`#114 (724 B) | `arcanum2.dat`#10426 (724 B) | differs |
| `scr/02836sebastian_death.scr` | `arcanum4.dat`#115 (460 B) | `arcanum2.dat`#10456 (460 B) | differs |
| `scr/02900prison_warden.scr` | `arcanum4.dat`#116 (2,044 B) | `arcanum2.dat`#10520 (328 B) | differs |
| `scr/02944halcyon_offering_plate_use.scr` | `arcanum4.dat`#117 (592 B) | `arcanum2.dat`#10564 (592 B) | differs |
| `semes/action.mes` | `arcanum4.dat`#127 (6,824 B) | `arcanum3.dat`#1093 (6,596 B) | differs |
| `sound/krag_fidgeting.wav` | `arcanum4.dat`#130 (20,416 B) | `arcanum2.dat`#11393 (62,372 B) | differs |
| `sound/melee-flesh-metal1.wav` | `arcanum4.dat`#131 (29,198 B) | `arcanum2.dat`#11440 (24,060 B) | differs |
| `sound/melee-flesh-metal2.wav` | `arcanum4.dat`#132 (15,558 B) | `arcanum2.dat`#11441 (31,114 B) | differs |
| `sound/snd_critter.mes` | `arcanum4.dat`#133 (20,121 B) | `arcanum2.dat`#11579 (19,840 B) | differs |
