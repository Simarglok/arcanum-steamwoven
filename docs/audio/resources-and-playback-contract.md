# Audio resources and playback contract — ARC-RES-022

Status: research implementation, not a production audio runtime. This document
separates the implemented research seam from the proposed future Data/Godot
boundary. Independent exact-candidate review and delivery are separate gates.
The accepted task uses local-only / `CI_NOT_REQUIRED`; mandatory local checks
still apply. No hardware, audible playback, graphical or export gate is required
for this research. None is claimed to have passed.

**Superseded points (ARC-RES-052, 2026-10-17).** The [audio study](../research/audio-evidence.md)
checked these claims in the retail executable and counted the whole installed data. Where they
differ, the study wins: audio resolves through the content stack, not one selected source;
soundparams key 12 is the small size's volume and the per-size volume is applied; a balance-left
above 127 clamps to 127 and `/over` wins over `/loop`; one ambient fires per slot per tick;
combat stinger and loop start together; voice gender is the PC's; overnight hour windows exist in
the data (20 entries) and are a rule-setting choice, not an unsupported case. Its task plan
replaces the proposed profile below.

## Evidence and scope

Labels used throughout:

- **Source (S)**: a reading of the original's functions at the cited addresses,
  evidence level F0 (not checked in the executable) unless the verification
  ledger lists them; the [audio study](../research/audio-evidence.md) rows
  SN1–SN34 checked many of them and win where they differ.
- **Data (D)**: bounded read-only original observations retained in the local
  ignored ledger, not reproduced here. Headers are not decoding/playback proof.
- **Policy (P)**: accepted task and [architecture direction](../architecture/decisions.md),
  independent C# Data/core and Godot presentation, simulation independent of
  playback, preferences separate from save state.
- **Proposal (R)**: future implementation profile; not an implemented component
  or an inference about retail parity.
- **Unknown (U)**: deliberately unresolved; not silently replaced by a guess.

No runtime library, middleware or codec of another implementation is
incorporated. The standard-library Python
research helpers and literal synthetic fixtures are independently authored under
the repository's [MIT license](../../LICENSE). The local toolkit manifest was
consulted for provenance only; no toolkit binary was executed or copied.

Scope: selected SFX ID → named table → resource; selected scheme ID → list
entries → resources; supplied speech identity → voice candidates; selected
sector's 12-byte sound record; WAV/MP3 header classes and loop metadata. This
is not the full inventory of gameplay callers, every audio asset, or any
retail executable A/B study. [Research method](../research/method.md),
[FR-MEDIA-003/004](../requirements/ui-media-lifecycle.md#fr-media-003) and
[toolchain](../workflow/toolchain.md) remain applicable.

### Source / claim ledger

Confidence for S rows is a reading of the functions at the cited addresses (F0
unless the ledger lists them), not experimental retail confirmation. Each claim's boundary is part
of the claim, not an optional footnote.

| Claim | Functions read (address) | Established / limit |
| --- | --- | --- |
| S01 | resolve path (0x41A940), init (0x41AA30), module load/unload (0x41B0A0, 0x41B0D0) | `snd_00index.mes` names the SFX tables. Search follows their load order, then module `snd_user.mes`; resource prefix is `sound`. This is table lookup, NOT global DAT-versus-loose precedence. |
| S02 | init (0x41AA30), volume get/changed (0x41C8A0, 0x41C8B0, 0x41C920, 0x41C930, 0x41C970, 0x41C980), volume adjust (0x41C9D0) | Listener initialization, defaults, soundparams overrides and effects/voice/music settings. No separate original Ambient or Master setting is established. |
| S03 | ping (0x41B2A0), update (0x41BAF0), scheme helper (0x41BCD0) | Strictly greater than 250ms ping interval, current game hour, ambient frequency/random volume/balance, loop-hour eligibility, transition restore, `#id` resource references. |
| S04 | scheme active test (0x41BD10), play scheme (0x41BD50), scheme load (0x41BE20), entry parse (0x41BF70), scheme helpers (0x41C260, 0x41C290) | Two active scheme slots; equal identities suppressed; repeated active IDs retained; load scans 100 IDs and stores at most25 sounds. Parse options and one-shot startup; not a safe public parser contract. |
| S05 | sector sound record layout, sector sound load/save (0x4F8150, 0x4F8180), DIF load/save (0x4F81C0, 0x4F81D0) | Sound record is flags/music/ambient,12 bytes; modified flag cleared on load/save. DIF helpers no-op in this source. |
| S06 | sector load (0x4D1530), sector-change notify (0x4F2330) | Framing order; sound included for version≥0xAA0003. Sector caller invokes schemes only if at least one ID nonzero. A zero/zero sector does NOT establish stop-all. |
| S07 | speech id (0x4189C0, 0x418A00), speech start/stop (0x5688D0, 0x5689B0) | Packed directory15bits / line16bits; `-1` no speech; gender-dependent path and female→male existence fallback; destroy/clear previous handle. |
| S08 | dialog reply/option handlers (0x5684C0, 0x568540), option processing (0x567E30), dialog close (0x5673A0) | Reply caller starts voice; option selection/exit stops it. Full dialog-close path inventory Unknown. |
| S09 | positional parameters (0x41B420), play on location/object (0x41B870, 0x41B930), listener and move (0x41C690, 0x41C6D0, 0x41C780) | Origin-relative projected coordinates; doubled Y for distance, horizontal X for pan; source and listener updates recompute positional volumes. |
| S10 | scroll (0x40E630), origin changed (0x40E910), S02 | Camera scroll uses viewport-center location; origin-change caller too. Effects-volume callback instead repositions listener to local PC. Not a universal actor-only or camera-only rule. |
| S11 | footstep table, critter sound (0x4F0ED0), path-move update (0x4305D0) | Plate/surface/chain variants plus random0..3; selected walk/run caller on frames3/8. Does not establish complete animation or movement timing. |
| S12 | combat music start/stop (0x41C4D0, 0x41C5A0), lock/unlock (0x41C610, 0x41C660), load/save (0x41B0F0, 0x41B1D0) | Combat defers world-scheme replacement and later restores; nested scheme lock and serialized restore identities. Not a promise of sequential stinger/loop playback or complete persistence compatibility. |
| S13 | public sound interface, play SFX (0x41B2E0), stop all and voice (0x41C340, 0x41C370) | Distinct public SFX/voice/music/scheme/listener operations; lock/failure invalid handles. Not the future Steamwoven API. |
| S14 | TIG sound layer (not read) | Not checked in the executable (F0 gap): startup success does not prove initialized playback; codec/device/audibility are not proven. |

## Resource identity and selected-source profile

### SFX

**S01:** lookup is ID-driven, not filename heuristics. `snd_00index` entries
name tables, whose records map cue IDs to relative names. The source takes the
first matching loaded table, then tries `snd_user` only on a miss. A duplicate
ID across tables therefore differs from an archive-path collision. It must be
reported with every candidate and the selected table; duplicates within a table
are rejected by this research subset. Unknown IDs and missing resource payloads
are different errors. Neither silently becomes another cue.

**R:** a future resolver returns a resource reference plus provenance:
profile identity, selected DAT/loose source identity, table/index ID, raw cue
ID, normalized key, archive ordinal/offset/sizes, role and resolution status.
Use stable raw IDs; do not deduplicate the domain by filenames. `#id` scheme
references go through the same SFX resolver; other entries prepend `sound`.
No renderer asset name, guessed material or actor art basename selects audio.

**D:** the opt-in local ledger binds the selected source-indexed footstep cues
and each selected sector-scheme entry to its resource and header. It also
retains the module fallback table, positional parameter keys, missing-ID outcome
and cross-table duplicate candidates. No original table excerpt, observation
hash, catalog count, path-specific aggregate or sampled audio bytes are public
fixtures. A dummy user-table entry is not proof of a useful playable user cue;
that fallback is independently tested synthetically.

**R selected-source policy:** request one explicit source, never search all
mounted content. For this observation the base SFX/scheme/params tables and
selected WAVs use the explicitly selected base DAT; sector/speech/user table
use the explicitly selected module DAT; the selected music uses an explicit
module loose-file root. TIGCache is derived, not a canonical fallback. No
fallback to another archive after malformed/unsupported content. Case-insensitive
ASCII keys and slash/backslash equivalence are this **selected research profile**,
not a claim about all platforms/mods. Reject selected ambiguous normalized paths,
absolute/drive paths, `..`, `.`, empty components, control bytes and unsupported
encoding. A loose path must stay under its supplied root with no symlink in the
selected chain. Archives are never extracted.

**U:** universal archive/patch/loose precedence, localized/non-ASCII resource
identity, mod overlays, duplicate MES behavior inside the original's general MES parser,
full caller catalog, and retail-version compatibility require separate evidence.
The CLI only examines the chosen archive entry, not a global path-safety census;
unselected unsafe/non-ASCII names are not candidate matches.

### Schemes, time and loop semantics

**S03/S04:** SchemeIndex's `#number` identifies the start of a SchemeList
range. The source examines100 consecutive numeric IDs, tolerating holes; the
sound array holds25 entries. Zero is no load. Equal requested music/ambient
IDs are reduced to one; already active identities are not restarted. Replacement
fades tracked loops25ms; untracked one-shot effects need not be stopped by that
operation. Slot number is not a permanent bus or role identity.

Options in inspected source: `/loop` is a song repeated while the hour is in
an inclusive range; `/anchor` is a one-shot song without restore; `/over` is a
one-shot transition which arms restore. Non-loop songs begin immediately on
load, not after a time-window check. Ambient entries use `/freq`, `/time`,
`/vol`, `/bal`, `/scatter`. Defaults are hours0..23, frequency5, volume100%,
balance50%; scatter overwrites volume/balance ranges. Frequency compares an
integer draw0..999 strictly below its value. Source random volume/balance upper
endpoints are exclusive when their range has width; equal endpoints are fixed.
A reversed hour range is not a midnight-wrapping window in the inspected source.
Percent values convert to TIG's raw0..127 representation, not linear dB.

**R implemented research parser:** strict token grammar, positive `#id`,
nonnegative percent ranges, inclusive increasing hours0..23, frequency0..1000,
one mode, no repeated/unknown options. Blank list records are omitted. It
preserves percent metadata and does not mechanically emulate `atoi`, substring
matching, raw scaling or the original's invalid-value clamps. Overnight windows, combined
`/over /loop`, song frequency/balance/scatter, and timed anchor/over are explicit
unsupported cases, not retail impossibilities. Numeric holes are scanned;
excess sounds fail rather than silently truncating.

**U anomalies not ported:** in the original, soundparams key12 is the small
size's volume and key22 the medium's, with no overwrite (ledger SN3); a
balance-left above127 clamps to127 and `/over` wins over `/loop` (ledger SN13).
Combined transition/loop
flags, failed transition backend handles, pending restore reentrancy, end-of-track
versus failed-start completion, and source integer overflow/division edge cases
are not a robust future contract. Preserve them as source anomalies, not proof
of retail bugs.

**P/R time boundary:** presentation supplies monotonic elapsed audio time for
fades and scheduling, separately supplies a validated game hour for eligibility,
and supplies a dedicated audio RNG or deterministic recorded draw stream.
No hidden consumption of authoritative gameplay RNG. The original draws from the
shared game random generator (audio study rows SN14, SN33, [audio evidence](../research/audio-evidence.md)); that is Source, not accepted project
RNG policy. No WORLD-004 dependency
is introduced here. A future clock adapter is a later integration task.
Proposed tick cadence is250ms without replaying an unbounded catch-up storm;
pause freezes game-hour input according to its owner, not the presentation
fade clock. Mute/device failure cannot advance or invalidate game state.

**S06 vs R zero:** sector caller skips zero/zero, so crossing such a sector may
leave a previous scheme. A direct scheme play with zero/zero has different behavior.
Future `SetSchemes` explicitly treats zero/zero as clear; a future sector adapter
must choose Skip versus Clear in its declared compatibility profile. It cannot
claim these are interchangeable or infer that viewport redraw should restart
music. Repeated identical requests are idempotent within the current session.

### Speech identity and lifecycle

**S07/S08:** identity is assembled from masked script/directory15bits and
line16bits. It is not the DLG node ID nor a global SFX index. Split into
`directory=(identity>>16)&0x7fff`, `line=identity&0xffff` and form a five-digit
minimum directory plus `v<line>_<gender>.mp3`. The inspected branch maps a
nonzero PC gender stat to `m`, zero to `f`; this report does not rename those
stat enum values. Female candidate missing → try male. Male missing has no
reverse fallback. Both missing → no playback, while text presentation remains.
The UI stops the previous handle before resolving a new non-sentinel identity.
Importantly, identity `-1` returns **before stopping** prior speech. The research
candidate-list helper emits no candidates for it; it does not model that UI
side effect. Selected data establishes a supplied identity/resource binding,
not that a particular retail conversation emitted it.

**R:** keep optional speech identity separate from text. A new reply command
owns its voice token and replaces/cancels prior voice even when new voice is
missing or silent. Close/skip/speaker destruction explicitly cancel the owner.
This is a proposed improvement, not a claim that the original's `-1` branch stops voice
or that every close caller was audited. Missing voice never delays a valid
reply/option, rolls back dialog state, or changes a script transition.

## Sector and positional boundary

**S05/S06:** within the selected framing, serialized little-endian sound record
is flags:u32, music:i32, ambient:i32, 12 bytes;
its modified bit is `0x08000000`. Keep raw flags in observations. Do not
interpret unknown bits as domain permissions. Before the sound record, walk
validated light count and48byte selected light records,4096tile words,
roof discriminator plus optional256roof words, version0xAA0000..0xAA0004,
tile scripts (count×24), optional12byte sector script, then12bytes of
metadata. Version≥0xAA0003 carries the sound record. The existing
[sector framing research](../research/crash-site-framing.md) is the
cross-check, not a new DAT decoder. The new helper stops after the sound
record; it does not validate the remaining objects, block masks or DIF file.
Earlier versions report sound absent, not a fabricated clear command.

**S09/S10:** source distance is floor of `sqrt(dx²+(2dy)²)` in the projected,
origin-relative location coordinate system, not Euclidean world-grid tiles or
unscaled pixels after arbitrary Godot zoom. The horizontal X difference sets
pan; per-size min/max radii govern attenuation. Viewport scroll supplies
listener location at its center; listener may also move to PC on volume change.
Actor-follow camera coincidence does not prove actor ownership. Listener source
must therefore be an explicit profile input; renderer zoom must not secretly
rescale the metric or simulation. The tool tests the metric, not a reproduction
of the whole attenuation curve or every location conversion caller.

**S11:** footstep plate armor uses one base independent of surface; otherwise
source tile sound category sets the base and chain armor adds a second set for
wood/stone/dirt. Four variants are selected via source RNG. The local-player
object must exist, but the body does not compare the sounding object with that
PC; a misleading comment is not a guard. The observed frames3/8 caller is a
selected branch, not a new rule for simulation steps. Future simulation may
emit an immutable cue/material/equipment event; Data resolves it; playback
cannot move the actor or invent action timing from a rendering frame.

## Bounded future implementation profile (Proposal)

No production assemblies/components or Godot backend are introduced by this
card. The following is the small boundary a later accepted task can implement.
Logical names below are proposed API vocabulary, not symbols claimed to exist.

| Boundary | Request / output | Owner and failure contract |
| --- | --- | --- |
| Data `ResolveCue` | profile, role, raw SFX/scheme/speech identity → `Resolved` resource/provenance or `MissingId`, `MissingResource`, `Ambiguous`, `Unsupported`, `Malformed`, `LimitExceeded` | C# independent of Godot; validate before reads; no guessed fallback. |
| Data `OpenAudio` | selected immutable resource reference, cancellation → bounded bytes for small PCM or bounded read-only MP3 stream lease plus codec metadata | Lease owns stream/archive; lifetime explicit. Never hand an archive-wide buffer or pathname outside allowed root to backend. |
| Presentation `Play` | resolved lease, bus, owner identity, generation, gain, loop policy, optional projected source → opaque session-local token/status | Godot adapter owns players/decoder buffers; success means accepted/start status, not audible proof. Release lease on failure/cancel. |
| `Cancel` / `CancelOwner` | token or owner scope → no-op if stale/already released | Idempotent; unrelated owner unaffected. Completion callback carries token AND generation; stale completion cannot stop a reused slot. |
| `SetSchemes` | desired music/ambient IDs, supplied hour and audio draw stream | A map/session owner, distinct from dialog. Stable desired state prevents duplicate loops. Replacing state retires the old generation and its restore commands. |
| `SetListener` / `MoveSource` | explicit projected origin-relative coordinates and size/profile | Presentation-only positional recalculation, finite/ranged numbers; actor transform unchanged. |
| `SetVolumes` | Master/Music/Voice/SFX/Ambient linear0..1 preferences | User configuration outside saves; reject NaN/inf/out-of-range;0 is silence, not `log(0)`; linear gain maps to backend dB only after zero branch. |
| `CloseSession` / backend lost | revoke generations, stop/release players/leases, bounded terminal statuses | No authoritative-state write. Device retry requires explicit recovery eligibility; do not replay expired voice/effects. |

Mixer proposal: Master parents four sibling buses Music, Voice, SFX and Ambient.
Ambient is a **proposed extension**, not an original option exposed by the
inspected source. Per-cue gain × bus × Master is independent; an Ambient mute
must not silence SFX. Store volumes outside save slots. Save-load restores only
explicit desired presentation identities, not device handles, active decoder
objects, monotonic timestamps or user preferences. No compatibility with the original's
serialized restore state is claimed.

Proposed conservative limits for a first backend:32 total active voices,
at most2 streamed music voices (including replacement fade),1 dialog voice,
4 concurrent loads,16MiB decoded small-SFX cache,32MiB aggregate backend decode
budget, maximum5000ms requested fade. Reject overload before allocation;
no stealing/replay policy is implicit. Backend must also enforce decoded frame
and duration budgets: a compressed-byte limit does not limit decoded memory.
Streamed music must not be inflated wholesale merely to fit a small-entry API.
These backend limits are NOT implemented/proven by the research helper.

Implemented research read limits:512MiB archive,32MiB index,200000entries,
DAT stored AND decoded payload≤1MiB using unchanged `dat_probe.py`;
MES≤64KiB,4096records,1024value bytes,240byte ASCII resource keys;
loose file≤8MiB, header prefix≤64KiB. Full-file SHA-256 uses streaming reads
for invariance; it is not audio decoding. WAV chunk count≤128, smpl loops≤16,
sector lights/scripts≤4096, projected deltas≤1000000. These limits fail closed;
there is no CLI flag to silently raise the inherited DAT1MiB limit for music.
Use explicit bounded loose prefix observation for a larger selected MP3.

### Codec/header classes and unsupported cases

- **Research-supported WAV header subset / R initial decode candidate:** RIFF
  WAVE PCM16, mono/stereo, rates11025/22050/44100/48000, coherent block alignment
  and byte rate, exactly one fmt/data, bounded padded chunks. Unknown chunks
  are skipped only after bounds validation. `smpl` loop type/start/end-inclusive/
  fraction/repeat are recorded; loop endpoints must fit frame count. Header
  admission does not promise backend support for every loop type.
- **Research-supported MP3 header subset / R streamed candidate:** two consecutive
  complete MPEG1/2/2.5 LayerIII frames within the bounded prefix, valid indexed
  bitrate/sample-rate fields; optional bounded ID3v2.3/v2.4 prefix. VBR can change
  bitrate; duration, gapless metadata, seek behavior and decoded samples Unknown.
  Whole-file indefinite loop is scheme metadata, not an MP3 embedded-loop claim.
- **Unsupported until a separate profile:** ADPCM, float PCM, extensible WAV,
  RF64, extra fmt/data chunks, free-format MP3, other MPEG layers, out-of-prefix
  ID3 or frame headers, unknown modes/options, non-ASCII record identity,
  overnight scheme windows, full mod catalog and retail parity. Unsupported
  does not mean absent in the original catalog.

Proposed ownership trace: request→validate→load lease→start→terminal release.
Cancellation before load, during load, during fade and after finish has the same
owner/generation checks. Scene reload/map change invalidates map/scheme tokens;
dialog close only cancels dialog voice. Combat override has a distinct owner and
restore target; replacement during combat updates the desired world pair, not a
stack of unbounded restore commands. Audio-disabled startup resolves to a silent
backend with explicit status, not a false `Playing` state. Single-session token
idempotence is not distributed exactly-once delivery, durable deduplication or
a guarantee across process restarts.

## Independent synthetic scenario oracles

The executable helpers are [audio_resource_probe.py](../../tools/research/audio_resource_probe.py)
and [audio_resource_probe_tests.py](../../tools/research/audio_resource_probe_tests.py).
Literal expected values are separately written in the tests, not generated from
source constants or fed back from the implementation. They exercise the actual
selected DAT/loose helpers and CLI as well as identity/header seams. Lifecycle
is explicitly a proposed **synthetic scenario model**, not a simplified stand-in
claimed to prove production Godot behavior.

| Oracle | Input and independently specified result | Seam exercised |
| --- | --- | --- |
| O01 | cue9 in two ordered tables plus user → first table; cue10 only user → user; cue11 absent → missing | `resolve_sfx`, duplicate/missing policy |
| O02 | identity0x002a0011 → folder00042/line17; absent female→male; absent male→missing; -1→empty candidates | `speech_candidates`/`resolve_speech` |
| O03 | index7→base100, entries100/102 with hole101 → both entries; zero→none;26 entries→limit error | `scheme_entries` |
| O04 | `/freq:10 /time:5-19`: hour4 false,5/draw9 true,5/draw10 false,19 true,20 false; freq0 false,1000/draw999 true | real scheme metadata/eligibility, not original execution |
| O05 | PCM16 mono11025 synthetic4frames →8data bytes; optional loop1..3 accepted; endpoint4 rejected | `wav_header` geometry/loop bounds |
| O06 | literal MP3 headerfffb90c0 with two417byte frames → MPEG1/LayerIII/44100/128kbps/mono; one frame rejected | `mp3_header`, ID3 and malformed matrix |
| O07 | zero lights, empty roof, version0xAA0004 and explicit sound bytes → offset16424, flags0x08000000, music1/ambient101; nonempty roof/lights/scripts →17616 | `sector_sound`; absent older-version sound explicit |
| O08 | projected delta(30,20) →50, (0,50) →100; camera listener reaches source→0, actor need not move | exact bounded metric, not full attenuation |
| O09 | cancel dialog owner retains map; cancel twice no-op; reset then cancel old(1,1) retains new(2,3); voice33 rejected | synthetic ownership model |
| O10 | repeated(7,8) unchanged, equal(7,7)→(7,0), repeated zero unchanged | proposed scheme intent, not every original transition |
| O11 | compressed synthetic DAT entry CLI→header; duplicate normalized path/missing reject;1MiB excess/decompression overrun reject | actual `selected_dat` and inherited DAT seam |
| O12 | synthetic3MB loose MP3 reads only64KiB header; symlink and8MiB excess reject | actual loose helper and CLI |

Negative matrix also covers traversal/drive/NUL/non-ASCII keys, malformed and
duplicate MES IDs, record size/count bounds, invalid/duplicate scheme options,
hour/RNG limits, unknown WAV codecs/chunks/loops, ID3 synchsafe/size/version,
missing frame pairs, sector truncation/version/roof/script limits, stale tokens
and invalid scheme IDs. Parsing non-ASCII prose **outside** MES records does
not invent a codepage for values; actual non-ASCII record values remain Unknown
and rejected. No public fixture contains original content.

Reproduce synthetic verification from the repository root using Python3.10+:

```sh
python3 -B tools/research/audio_resource_probe.py --self-test
python3 -B tools/research/dat_probe.py --self-test
```

Opt-in selected observations (output must stay local, outside Git/attachments):

```text
python3 -B tools/research/audio_resource_probe.py --dat <explicit-archive> --entry <selected-resource-key> --kind table
python3 -B tools/research/audio_resource_probe.py --dat <explicit-archive> --entry <selected-resource-key> --kind wav
python3 -B tools/research/audio_resource_probe.py --dat <explicit-archive> --entry <selected-resource-key> --kind sector
python3 -B tools/research/audio_resource_probe.py --dat <explicit-archive> --entry <selected-resource-key> --kind mp3
python3 -B tools/research/audio_resource_probe.py --loose-root <explicit-music-directory> --entry <exact-relative-file> --kind mp3
```

The CLI samples exactly one explicitly requested source. The task-private
observation driver composes these same helpers into selected table/sector/
speech bindings without exporting payloads. Invalid inputs exit2; failed
self-tests exit1. Missing required originals do not become a pass or synthetic
replacement. Run the pinned locked restore/build/root synthetic tests from the
toolchain as well. Unrelated labs/export endpoints and unrelated original-data
test families are not this research task's acceptance gates.

## Acceptance and findings ledger

Stage disposition is implementation/check readiness only. Independent native
review is NOT_VERIFIED until a distinct Reviewer records FULL PASS for the
stable candidate. The private run ledger binds actual commands, timestamp,
workspace, base/head, session, tool/source pins and original invariance; it is
retained outside commits and public attachments. Rerun affected checks after
source/base/tool/input changes. Cited addresses are audited against the function
list; local links, privacy and the full scoped diff are audited before handoff.

| Requirement / finding | Production seam / independent oracle | Evidence and disposition |
| --- | --- | --- |
| AC1 | S01/S04/S07; O01–O03/O11–O12; original explicit selected bindings | Implemented identity/path helpers and local source/index/resource/header ledger. Duplicate/missing/unsupported/traversal policies explicit. No universal precedence claim. |
| AC2 | real selected DAT/loose reads and sector framing; O05–O07; independent prefix/cursor checks | Bounded original WAV/music/speech/table/sector observations retained locally with before/after fingerprints; header proof only. No full audio catalog decode/export. |
| AC3 | S01–S14; O01–O12 literal fixtures; license/source audit | Source/Data/Policy/Proposal/Unknown distinguished; unknown retail/caller/timing behavior retained. |
| AC4 implementation | Data/Godot API table and explicit limits/unsupported profile; actual CLI/self-tests, DAT regression and required root checks | Research tool and independent synthetic cases implemented. No production runtime/codec/hardware claim. Required local checks bound in handoff. |
| AC4 independent review | exact implementation-ready SHA, original implementer/session, whole ledger | Future required native dev-reviewer FULL PASS; NOT_VERIFIED at Developer completion. Existing child-aware routing retains same Developer/candidate before native review. |
| AC5 | independent PASS→private PR exact-head→separate Reviewer policy merge or observed owner merge→fresh-main ancestry receipt | NOT_IMPLEMENTED here; routing owner preserves publication obligation and existing CLOSE card. Stage completion is not delivery. |
| F01 | duplicate-fmt negative test | Initial oracle expected codec rejection but actual seam correctly rejected duplicate fmt first; expected category corrected to `WavFormatChunk`; matrix rerun. Closed, no production behavior weakened. |
| F02 | selected MES observation vs strict table lexer | Initial whole-file ASCII requirement rejected non-ASCII prose outside records. Byte-level lexer now ignores non-record prose and still rejects non-ASCII actual values; independent positive/negative regression and original run passed. Closed; encoding of prose remains Unknown. |
| F03 | original module fallback | User table represented, no useful original user audio inferred from dummy mapping; synthetic fallback oracle O01 provides independent behavior evidence. Closed as explicit scope/Unknown, not fabricated original playback. |
| F04 | environment inline-script denial | Runtime rejected shell `python -c`; used permitted task-owned script files without credentials/profile changes. Closed infrastructure alternative; not a substantive review correction. |
| F05 | bounded MES identity preservation / O01 negative boundary | Self-audit found blanket comment removal could rewrite comment-like bytes inside values. Byte scanner now strips comments only outside fields; literal value preservation and unterminated/nested-brace rejection are regression-tested. Closed without normalizing original identifiers. |
| F06 | WAV aggregate loop budget / O05 boundary | Final self-review found repeated `smpl` chunks could each spend the16-loop limit. Enforce16 across the complete WAV; literal16-loop acceptance and17-loop split-chunk rejection regression added. Closed after full checks rerun. |
| U01 | Combined flags, bad values, pending restore, zero-sector, listener callers | Explicit Source anomalies/Unknown above; proposed strict subset differs intentionally, not retail parity. |
| U02 | all decoders, platforms, devices, catalog and retail behavior | NOT_VERIFIED and outside accepted research gates; no inferred pass from source, header or exit0. |

Implemented: research contract, selected source traces, bounded resource
and header helpers, literal synthetic tests and opt-in original observation
workflow. Not implemented: production C# audio loader/backend/mixer/dialog/map
integration, decoding or audible validation. Remaining delivery: exact-head
independent native FULL PASS, authorized private PR and head readback, separate
policy Reviewer merge/verified receipt. Review/publication continuations must
retain the same candidate and private evidence; they must not copy raw data into
a PR, public artifacts or replacement worktree.
