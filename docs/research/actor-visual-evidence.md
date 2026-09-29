# One actor visual evidence record

## Result and readiness

**Readiness: NOT READY — actor-world anchor missing.** The probe did establish
one bounded original monster visual chain, but it does not claim that ART frame
hotspots/offsets define an actor's world anchor. That last relationship needs
actor-specific runtime observation or another independently described evidence
source. The result is therefore not an implementation-ready renderer contract.

This is research tooling, not a production MOB/PRO parser, composite-equipment
system, renderer, movement system, collision rule, or archive-precedence
implementation. It opens exactly one caller-provided module DAT, resource DAT,
and prototype directory; it never searches loose, PATCH, or alternative archive
sources. Original paths, entry names, directory strings, textual payloads,
payload hashes, bytes, pixels, images, and screenshots are neither printed nor
committed.

## Reproducible selected chain

The opt-in run labelled `actor-research-source-01` authenticated its three
caller-selected inputs before and after its read-only traversal:

| Input | Length | SHA-256 |
| --- | ---: | --- |
| module DAT | 355,938,305 | `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f` |
| resource DAT | 447,470,816 | `6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576` |
| explicit prototype-root aggregate | 949,341 | `1741dec3e542e2717a130c8162a4d739c82edf0a14866905d2b85a070d5ec33c` |

The selected source is module-DAT ordinal **643**. It is a fully consumed v119
object envelope of type 16 (NPC), whose serialized common field 0 supplied the
effective current ART directly: `0xc9001810`. It is an ART type-12 monster
rather than a player/type-15 or human critter; the checked armor, shield, and
weapon selector components are all zero, which is the bounded single-layer
criterion for this candidate. The tool does not infer an NPC/world association,
name, gameplay identity, or a composite equipment policy.

The direct current ART is transformed only by the pinned CE source's Stand
(action `0`) and Walk (action `1`) selector rule. The resulting exact packed
selectors are `0xc9001810` (Idle/Stand) and `0xc9001850` (Walk). Each resolves
exactly once, through the explicit resource DAT's source-defined monster
mapping, to resource-DAT ordinals 4753 and 4754 respectively. Mapping values
are treated as private source text: the report retains the resource ordinal,
not the logical path/value. Both ARTs passed `ArtDecoder.Inspect` under its
1 MiB input limit.

| Action | Packed selector | resource DAT ordinal | raw ART FPS | physical groups | ordered frames per group | palette |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Idle / Stand | `0xc9001810` | 4753 | 8 | 8 | 15 | 1 |
| Walk | `0xc9001850` | 4754 | 15 | 8 | 8 | 1 |

All eight admitted source-facing selectors were inspected for both actions.
The source-facing value is deliberately an opaque integer, not a compass enum:

| requested facing | physical ART group | `flipX` |
| ---: | ---: | --- |
| 0 | 0 | false |
| 1 | 7 | true |
| 2 | 6 | true |
| 3 | 5 | true |
| 4 | 4 | false |
| 5 | 5 | false |
| 6 | 6 | false |
| 7 | 7 | false |

For every table cell above, the probe checks that physical group and requested
palette exist and retains every frame in group-major, frame-index order. Its
metadata-only output includes each frame's `index:width x height @ hotX,hotY +
offsetX,offsetY`; this avoids publishing an original entry name or payload while
making the exact geometry chain reviewable. The measured frame sets are not
uniform: Idle has 15 frames per group, Walk has 8, and the dimensions/hotspots
vary by group and frame. No eight-direction compass order, equal frame family,
universal palette, or absence of mirroring is assumed.

For the selected chain, ART offset X/Y is retained per frame. Mirrored facings
follow the source rule: group `8-facing`, `flipX=true`, and actor-frame geometry
uses the corresponding source mirroring transform (hot X becomes `width-hotX-1`
and offset X changes sign). This is a CE-source rule, not a measured world
placement. There is **no stored actor-anchor-to-top-left delta** in this record:
`worldAnchor + anchorToTopLeft` remains unsupported until it is observed for an
actor rather than transferred from static scenery.

## Bounded census and negative facts

The scan visits `.mob` entries in module-DAT index-ordinal order, admits a
candidate to its cap only after its complete v119 type-16/type-15 envelope has
been consumed, and stops before touching any subsequent module entry once 256
such eligible records have been admitted. It rejects an entry with a stored or
declared decoded size above 1 MiB before payload read. The run
reported: 256 eligible records; 256 type-16 records; 198 direct ART resolutions;
zero completed prototype-default resolutions; zero missing prototypes; 58
invalid prototype resolutions; 52 fully inspected single-layer monster
Idle/Walk chains; and selected lowest qualifying ordinal 643. Other fully
framed non-NPC entries, malformed/unsupported records, composite/non-monster
ARTs, missing mappings/resources, or ART failures are retained only as typed
aggregate rejection counts.

The exact selected result remains NOT READY only for the unresolved actor-world
anchor. It is not a claim that no actor ART exists, that all NPCs use the same
chain, or that ART metadata supplies collision, grid position, navigation,
locomotion, walking speed, or runtime parity.

## Envelope and inheritance policy

The standalone reader consumes a complete bounded v119 record before admitting
it: version; two 24-byte object IDs; concrete type; signed override count;
all type-appropriate mask words; and every selected scalar, optional, array,
string, and object-ID wire value through exact end-of-record. It rejects
unsupported version/type, reserved mask bits, mask/popcount mismatch, invalid
optional presence, invalid array header/bitset population, missing string NUL,
truncation, arithmetic/cap failures, and trailing bytes.

Current ART has only these distinct outcomes:

| Outcome | Rule | Result |
| --- | --- | --- |
| `Direct` | instance common field 0 is present | use its signed packed value |
| `PrototypeDefault` | field 0 absent and exactly one canonical six-digit, type/ID-matching v119 prototype resolves | use prototype common field 0 |
| `Missing` | inherited record has no or multiple canonical prototype candidate | reject candidate |
| `Invalid` | unlocatable/mismatched/malformed/over-cap prototype | reject candidate |

The prototype directory is explicit and fingerprinted before/after even when a
direct candidate needs no prototype payload. Its private aggregate binds each
top-level leaf filename and parsed canonical selector, bounded file length, and
raw content digest without emitting a name or path. No generic inheritance,
adjacent field default, fallback archive, or filename-based ART guess is
allowed.

## Evidence ledger and provenance

| ID | Claim | Classification | Source / limit |
| --- | --- | --- | --- |
| AV-001 | v119 field-0 direct/one-exact-prototype ART resolution is the only admitted inheritance edge. | observed bytes + CE-source claim | Probe envelope; CE `src/game/obj.c:178-200,3549`; no general MOB/PRO parser claim. |
| AV-002 | Type 15 and type 16 are separate player/NPC envelopes; type 15 is excluded. | CE-source claim + synthetic experiment | `src/game/object.h:25-41`; production-path self-test and bounded census. |
| AV-003 | Monster Stand/Walk actions are source action values 0/1; normalization/path selection is source-defined. | CE-source claim | `first_party/tig/include/tig/art.h:234-260`; `src/game/name.c:647-820,978-1005`. No CE code is copied. |
| AV-004 | Monster ART has packed action, rotation, palette and appearance selectors; type 12 is monster. | CE-source claim | `first_party/tig/include/tig/art.h:25-41`; `first_party/tig/src/art.c:16-40,1738-1829`. |
| AV-005 | Requested rotations 1..3 resolve to physical 7..5 with horizontal flip; the rest retain physical group. | CE-source claim + observed metadata | `first_party/tig/src/art.c:553-568,1211-1256`; probe inspected every retained group. |
| AV-006 | ART frames provide raw FPS and signed frame geometry. | observed bytes + CE-source claim | `ArtDecoder.Inspect`; `first_party/tig/src/art.c:119-142,1160-1193`. |
| AV-007 | A move animation starts with Walk, while scheduler pause can depend on speed. | CE-source claim | `src/game/anim.c:10483-10500,10503-10557`; raw FPS is metadata, movement speed is not inferred. |
| AV-008 | Actor anchor-to-top-left delta remains unmeasured. | unknown | No original-runtime observation; static-scenery evidence is deliberately excluded. |

The CE revision is `a7ff41b300ef712f0e7d088183a3d08957110cdd` from
`alexbatalov/arcanum-ce`, used only as Sustainable Use License specification
material. No CE code, comments, data structures, executable, or runtime is
copied or run. This record makes no clean-room, independent corroboration, or
pixel/runtime-parity claim. ArcNET is CE-derived; archived tools and WorldEd are
community reports, not independent evidence.

## Synthetic production-path coverage

`--self-test` constructs only synthetic inputs. The matrix below records its
actual entry point and stable outcome; it is a traversal/serialization check,
not an original-runtime oracle.

| Case | Entry point | Asserted result |
| --- | --- | --- |
| Direct + exact inherited candidate, type-15 exclusion, two qualifying index ordinals | `Main → Input.Parse → Probe` twice | exit 3, deterministic byte-identical report, lower index ordinal, `direct`, `prototype_default`, and `player_excluded` counters |
| Hard eligible-envelope cap | `Main → Input.Parse → Probe` | 256 admitted full envelopes plus a truncated trailing `.mob` report `eligible=256` and do not report the trailing record rejection |
| Full v119 envelope version/type/count/reserved-mask/truncation/trailing bytes | `Main → Probe` | exit 3 with the corresponding `record-*` typed rejection |
| Optional presence; array header/population; valid, unterminated, over-cap, and exact-entry-cap strings | `Main → Probe` | player exclusion for valid small and exactly 1 MiB entries; typed `optional-presence`, `array-*`, `string-*` rejections otherwise |
| Missing, duplicate, type/ID-mismatched, truncated, trailing, and over-cap canonical prototypes | `Main → Probe` | `prototype-missing-or-ambiguous`, `prototype-invalid`, or sanitized fail-closed `stage=prototypes reason=entry-cap` |
| Missing/malformed/duplicate/unsafe mapping; missing/duplicate-normalized/wrong-family resource; palette, malformed ART, resource cap, and ART frame-count limit | `Main → Probe` | stable sanitized fatal `stage=mapping reason=*` for mapping failures; typed `mapping-*`, `resource-*`, and `art-*` rejection otherwise |
| Wrong ART family and independent non-single-layer armor, shield, and weapon selectors | `Main → Probe` | typed `composite-or-nonmonster` rejection for each selector component |
| Distinct Idle/Walk FPS and two group-major frames with nonzero geometry | `Main → Probe` | report preserves action FPS, frame order/geometry, `SourceScheduler` cadence authority, and no movement-speed inference |
| Private input sentinel and fatal missing path sentinel | captured `Main` stdout + stderr | exit 3/1 respectively; both streams omit the sentinel and fatal output is sanitized |
| Prototype suffix/selector rename, per-file cap, and aggregate cap | `FingerprintDirectory` helper-only identity policy | unchanged total with changed aggregate SHA-256 for each rename, restored original aggregate, and fail-closed per-file and aggregate caps |
| All eight physical group/flip bindings and unreachable out-of-domain action | `MapPhysicalFacing` / `MonsterPath` helper-only source policy | exact opaque bindings; unsupported action rejected |

## Reproduction

```text
<canonical-dotnet> restore tools/research/Arcanum.ActorVisualProbe/Arcanum.ActorVisualProbe.csproj --locked-mode
<canonical-dotnet> build tools/research/Arcanum.ActorVisualProbe/Arcanum.ActorVisualProbe.csproj --no-restore
<canonical-dotnet> run --project tools/research/Arcanum.ActorVisualProbe/Arcanum.ActorVisualProbe.csproj --no-build -- --self-test
<canonical-dotnet> run --project tools/research/Arcanum.ActorVisualProbe/Arcanum.ActorVisualProbe.csproj --no-build -- --module <fixed-module> --resource <fixed-resource> --prototype-root <explicit-prototype-root> --source-id <safe-label>
```

## Implemented / not implemented / remaining work

Implemented: standalone net8.0, `Arcanum.Data`-only research probe; immutable
input checks; bounded complete v119/type-16 envelope traversal; direct/exact
prototype ART resolution; single-layer monster filtering; Stand/Walk resource
resolution; all admitted group/flip/palette/frame geometry inspection; and the
metadata-only evidence above.

Not implemented: a production MOB/PRO parser; actor renderer; actor world
anchor; composite equipment; runtime scheduler; movement; collision;
pathfinding; archive precedence; a Godot scene; or a claim of original-runtime
parity.

Remaining work: acquire actor-specific anchor-to-top-left observation (and only
then decide a separate reviewed presentation consumer). Cadence remains
`SourceScheduler` for runtime timing authority, with raw ART FPS preserved and
no movement-speed conclusion.