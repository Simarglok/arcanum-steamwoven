# Sound catalogue (ARC-DATA-040)

Status: **Implemented**, `Arcanum.Data.Audio` (Godot-free). The first task of the
[audio plan](../research/audio-evidence.md#task-plan): the original's sound tables, schemes,
positional parameters, voice paths and fixed music names over the
[content stack](../research/content-precedence.md), for any map of any module. It reads and
resolves, and since ARC-DATA-041 admits payloads; it does not decode, play or schedule sound
(ARC-UI-040 and later play them).

## Sources and load phases

The sound subsystem's init function runs while the game initialises, before the module is
mounted (its entry in the executable's subsystem table holds init 0x41AA30 and module load
0x41B0A0; the init loop and the mount order are the [load phases](../research/original-verification-ledger.md#load-phases-arc-data-027)
rows). So `ContentPhase` decides the copy read:

| File | Read by | Phase | Copy read |
| --- | --- | --- | --- |
| `sound/snd_00index.mes` and every table it names | sound init | `GameStart` | first base source |
| `sound/SchemeIndex.mes`, `sound/SchemeList.mes` | sound init | `GameStart` | first base source |
| `sound/soundparams.mes` | sound init | `GameStart` | first base source |
| `sound/snd_user.mes` | module load | `ModuleLoaded` | stack winner |
| every WAV/MP3 (effects, songs, voice, fixed music) | play request | `ModuleLoaded` | stack winner |

MES files go through `MesFile` with the original grammar; a refusal there (for example a
duplicate number) refuses the catalogue.

## API

`SoundCatalog.Open(ContentStack, SoundCatalogOptions?)` returns `SoundCatalogResult`:

- `Ready` with the catalogue, or a typed refusal naming the file: `MissingFile` (an init file in
  no base source), `Unreadable`, `Malformed` (with the `MesParseStatus`), `InvalidIndex` (an empty
  index, which the original refuses at start, an empty or non-ASCII table name, more tables than
  `MaxTables`).
- `Issues` that do not refuse: `UserTableMissing` (no user table is searched) and
  `PositionalParametersMissing` (the original's fallback values below), each with the reason
  (missing, unreadable, malformed and the MES refusal); `PositionalParameterMalformed` (a value that
  is not a plain whole number within int32; it is still read like `atoi`); `IndexEntryZeroSkipped`.

| Member | Behaviour (row of the [audio study](../research/audio-evidence.md)) |
| --- | --- |
| `Tables`, `UserTable` | Index tables in ascending index number, then the module's user table (SN1–SN2). |
| `ResolveSfx(id)` | (Its path is checked as a virtual path, which the original does not do.) The first index table holding the id wins, even with an empty record (`EmptyRecord`, plays nothing); only on a miss the user table; else `UnknownId`. A named record gives a `SoundFile` (`Found`, `Missing`, `InvalidPath`); `Playable` is true only for a found file (SN1). |
| `ResolveSoundFile(name)`, `ResolveFile(path)` | `sound/` + name, or a virtual path, as the stack winner. Paths longer than 259 characters (the original's 260-byte buffer with its terminator), non-ASCII, rooted or with `..` are `InvalidPath`. |
| `Scheme(id)`, `SchemeIds` | One scheme as the original loads it (SN12–SN13): `None` (0), `NotInIndex`, `NoListBase` (no `#`, such as "No Sound": a silent slot), `Loaded` with its entries, `TooManyEntries` (a 26th record, which crashes the original; both rule settings refuse), `Malformed` (a project refusal for a list base that is not a positive whole number below 2³¹ − 100, and a resource longer than the path buffer, which overflows it in the original). |
| `Positional` | `SoundPositionalProfile` from `soundparams.mes` (SN3): keys 1–4 large radii and pan distances, 10–12 small, 20–22 medium, 30–32 extra-large (min radius, max radius, volume %), defaults when a key is absent; `WithoutFile` (small, medium, extra-large at 0) when the file does not load. `RangeTiles` is the max radius / 40. |
| `ResolveSpeech(identity, gender)`, `SpeechIdentity(script, field)` | `sound/speech/NNNNN/vL_g.mp3` with the PC's gender; a female PC falls back to the male file, never the reverse; −1 is `NoVoice` (SN21–SN22). The identity is `(script & 0x7FFF) << 16 | (atoi(field) & 0xFFFF)`. |
| `SectorSound(WorldSector)` | Flags, music and ambient of a sector's sound record (words 3–5 of the metadata-and-sound section; none before version 0xAA0003). `AppliesOnEntry` is the original's "at least one id non-zero" test (SN20). |
| `CombatStingerName(1..6)`, `CombatLoopName`, `WorldMapTravelMusicName`, `SlideMusicName` | The fixed music files under `sound/` (SN17, SN30). |

### Scheme entries

`SoundSchemeParser` reads a SchemeList record the way the original does: the record lower-cased
(ASCII letters only), the resource up to the first space, options found anywhere in the rest by
substring, values like the executable's `atoi` (ASCII white space, a sign, digits, built in 32
bits so a long number wraps modulo 2³²), a second value after the first `,` or else the first `-`
(so `/time:-1` reads as −1 and 1, as in the original, and `/time:-5,3` as −5 and 3). Kinds: `TransitionSong` (`/over`, which wins
over `/loop` and reads no `/time`), `LoopSong` (`/loop` with `/time`), `AnchorSong`, `Ambient`
(`/freq`, `/time`, `/bal`, `/scatter`, the last overriding volume and balance). Defaults: frequency
5, hours 0–23, volume 100/100, balance 50/50. A max below its min is raised to it; volume and
balance become raw `127 × percent / 100` (the product in 32 bits, as in the original), truncated and
clamped to 0–127 (balance-left too).
Hour windows are kept as written: `HourWindowReversed` marks the 20 installed windows the original
never plays; the rule setting is applied by the player (ARC-UI-041). A `#N` resource resolves
through `ResolveSfx` (an unknown id or empty record leaves the original nothing to open: the file is
`InvalidPath`); any other through `sound/` + the name. An empty record still takes a slot, as in
the original (its file is `InvalidPath`).

## Payloads (ARC-DATA-041)

`SoundCatalog.OpenPayload(SoundFile, SoundPayloadOptions?)` reads a found file through the stack
(archive member or loose file, whole, within `MaxBytes`; nothing is extracted to disk) and
`SoundPayloadReader.Inspect` admits it by content, not by extension:

| Class | Admitted when | Result |
| --- | --- | --- |
| WAV | `RIFF`/`WAVE`, exactly one `fmt ` and one `data` chunk (an empty one gives 0 frames), PCM (tag 1), 1 or 2 channels, 8 or 16 bits, 8, 11.025, 16, 22.05, 32, 44.1 or 48 kHz, block align and byte rate consistent; `smpl` loops inside the samples (at most 16) | `WavFormat`: channels, rate, bits, data offset and length, whole frames, loops, ignored tail bytes |
| MP3 | an optional ID3v2 tag (versions 2–4, synchsafe size inside the file, a footer only in v2.4), up to 64 KiB of zero padding, then at once two consecutive valid frame headers of the same version, layer and rate, Layer III. Every installed MP3 starts its frames there; frames after any other bytes are not admitted, so arbitrary data cannot pass as audio | `Mp3Format`: MPEG version, rate, stereo, first frame offset and bit rate |

Statuses: `Admitted`, `Missing`, `InvalidPath`, `TooLarge`, `Unreadable`, `UnknownContainer`
(neither class), `Unsupported` (a well-formed file of another class: a WAV format tag other than
PCM, which includes compressed, float and extensible WAV, other rates or sample sizes, more
channels, MPEG layer I or II; its bytes are handed back for reporting), `Malformed` (truncated or
overlapping chunks, a missing or repeated fmt/data chunk, inconsistent alignment, a loop outside
the samples, a damaged ID3 tag, no two consecutive valid frames at the start; a free-format bit
rate is not a valid frame).
One installed effect carries unrelated bytes after its samples: once both fmt and data were found,
bytes that do not form a chunk (a short tail, or a header that runs past the end) end the walk and
are reported as `IgnoredTailBytes`, never read as audio; the same damage before them is
`Malformed`. An all-zero remainder is padding, of any length after fmt and data, and of fewer than
8 bytes before them.

Limits (`SoundPayloadOptions`): `MaxBytes` 32 MiB (the largest installed file, a music track, is
under 3 MiB), `MaxWavChunks` 128 (at most 65,536), `MaxWavLoops` 16 (at most 4,096),
`MaxMp3SyncSearchBytes` 64 KiB of leading zero padding. Files are read whole: a player decodes
MP3 progressively from memory, so nothing streams from disk.

Checks: `SoundPayloadTests` (literal synthetic WAV and MP3 bytes for every admitted class and
refusal) and, with `DAT_ROOT`, `SoundPayloadRealDataTests`: all 3,410 installed sound files are
admitted (706 WAV, one with an ignored tail and one `smpl` loop; 2,704 MP3, all MPEG-1 Layer III
44.1 kHz).

## Limits

`SoundCatalogOptions`: `Mes` (the MES reader limits), `MaxTables` (256). The scheme span (100 list
numbers) and the 25-record limit are the original's and fixed. Nothing is cached: `Scheme` and the
resolvers read the stack's index on each call; the stack must outlive the catalogue.

## Checks

- Synthetic: `tests/Arcanum.Data.Tests/Audio/SoundCatalogTests.cs` (table order, empty records,
  user table, phases, refusals, positional keys, every scheme option and clamp, 25/26 records,
  speech fallback, path limits).
- Real data (`DAT_ROOT`, category `RealData`): `SoundCatalogRealDataTests` reproduces the census
  of the installed module: 6 base tables and the module's user table, 834 named ids (27 without
  an installed file), 805 empty records, 60 SchemeIndex records (58 loaded, "None", "No Sound"),
  155 list records (153 with text, 2 empty), none over 25, 20 reversed hour windows, 74 map
  defaults, 1,654 sector records with 410 applied pairs, every id in the index, 2,630 voice
  files reached by identity and gender, and the nine fixed music files in the module folder.
