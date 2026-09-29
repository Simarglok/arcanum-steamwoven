# One actor visual evidence record

## Result and boundary

**Readiness: NOT READY.** This candidate establishes a bounded, reproducible
input/authentication and exclusion boundary; it does not select a renderable
actor. The probe intentionally stops before an actor resource name is derived:
a filename convention is not an accepted replacement for a pinned mapping rule.
No actor, image, original entry name, logical path, text, payload bytes, or
payload hash is published by the probe.

This is research tooling, not a production MOB/PRO parser, composite-equipment
system, renderer, movement system, collision rule, or archive-precedence
implementation. It opens exactly one caller-provided module DAT, resource DAT,
and prototype directory. It neither searches loose/PATCH sources nor selects a
winner between archives.

## Opt-in original-input result

The one explicit caller-selected source labelled `actor-research-source-01`
was authenticated before and after the probe. The module identity was
355,938,305 bytes / SHA-256
`8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f`;
the resource identity was 147,991,122 bytes / SHA-256
`4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d`.
The explicit prototype-root aggregate was 949,341 bytes / SHA-256
`e956718e2a978a4a84f34b9aee52c77e2166bac86970bb2641b38e3a20114956`.
No path, entry name, prototype name, payload hash, or payload was retained.

The bounded run examined 256 eligible module-DAT ordinal entries, admitted 54
type-16 envelopes, excluded zero type-15 envelopes, and classified zero
envelopes as invalid. It returned exit 3 / `NOT_READY` because no pinned,
reviewed actor selector-to-resource mapping was admitted. A bounded scan of
the eight readable 1TAD inputs found three inputs with one or more type-16
envelopes; this source is the first investigated alternative with the largest
observed bounded type-16 count. This is input-selection evidence only: it is
not archive-precedence, an actor selection, or a runtime observation.

## Inputs, caps, and output

The caller supplies `--module`, `--resource`, `--prototype-root`, and a
printable safe `--source-id`. The paths are never echoed. Before and after the
run the tool fingerprints module/resource contents and an aggregate of the
bounded prototype directory; any change fails with exit 1. It opens both DATs
through `DatArchive` with 1 MiB stored/decoded entry caps. In DAT ordinal order
it considers at most 256 eligible `.mob` entries; malformed entries are
aggregated as `invalid`, type 15 is counted as `player_excluded`, and type 16 as
`npc`. The current implementation recognizes only the version/type admission
prefix as a fail-closed research boundary. It does **not** represent that as a
full object-field parser or as evidence for direct/default resolution.

The deterministic report contains only safe source label and aggregate counts.
It exits 0 only for self-test, 2 for usage, 1 for I/O/format/limit/immutability
failure, and 3 for the expected bounded negative. A type-16 count is not a
candidate selection. A report ending `actor-resource-mapping-unresolved` means
that exact CE-derived selector/path policy, single-layer proof, and anchor and
cadence evidence still must be admitted before any ART is resolved.

## Evidence ledger

| ID | Claim | Classification | Source / limit |
| --- | --- | --- | --- |
| AV-001 | Selected source is explicitly caller supplied; no precedence is inferred. | synthetic experiment | Probe argument boundary; not original-runtime behavior. |
| AV-002 | Type-15 and type-16 values are distinct object types; current ART is common field 0 in the previously bounded object research. | CE-source claim | `src/game/obj.h:10-14`; `docs/research/crash-site-objects.md#record-envelope-admitted-fields-and-deliberate-omissions`. Static type-3 evidence does not prove critter parsing. |
| AV-003 | CE modifies a current actor art ID for stand/conceal and animation goals. | CE-source claim | `src/game/critter.c:1713-1764`; `src/game/anim.c:13193-13228`. Source policy, not runtime observation. |
| AV-004 | CE tests Walk and Stand resource availability for critter/monster families. | CE-source claim | `src/game/name.c:49-91,410-505,605-609,922-930`. This does not authorize copying its naming logic. |
| AV-005 | Actor-like types can mirror physical rotations and transform hotspot/offset X. | CE-source claim | `first_party/tig/src/art.c:1200-1256`. No actor world-anchor delta is measured here. |
| AV-006 | ART metadata provides raw FPS and signed geometry; it does not prove walking speed. | observed bytes / CE-source claim | `ArtDecoder.Inspect`; `docs/research/art-ce-spec.md#contract-adopted-for-this-bounded-decoder`. |
| AV-007 | Animation goal scheduling and move-to-tile are separate source paths. | CE-source claim | `src/game/anim.c:7348-7352,8075-8079,13302-13328`. No cadence or locomotion parity is claimed. |
| AV-008 | Current probe result is deliberately negative pending a mapping policy. | synthetic experiment | `Arcanum.ActorVisualProbe`; output is deterministic metadata only. |

The CE revision is `a7ff41b300ef712f0e7d088183a3d08957110cdd` from
`alexbatalov/arcanum-ce`, used as Sustainable Use License specification
material only. No CE code, comments, data structures, executable, or runtime
is copied or run. This record makes no clean-room, independent corroboration,
or pixel/runtime-parity claim. ArcNET is CE-derived; archived tools and WorldEd
are community reports, not independent evidence.

## Required next evidence

1. Independently document a bounded type-16 v119 envelope including masks,
   optional/array/string traversal, exact current-ART field presence, and one
   canonical type/ID-matching prototype default. Missing, duplicate,
   mismatched, malformed, and composite values must stay distinct.
2. Admit and cite a non-mechanical CE-source selector mapping for Idle/Walk,
   appearance, facing, palette, resource path, and one normalized resource DAT
   entry. Verify every selected physical group/flip/palette/frame through
   `ArtDecoder.Inspect`.
3. Establish an actor-specific world-anchor-to-top-left delta and a scheduler
   authority. Do not transfer static type-3 placement or derive collision,
   navigation, movement speed, or position updates from ART geometry/FPS.
4. Only then select the lowest eligible non-humanoid, single-layer type-16
   record within the 256-entry cap. If none completes, retain NOT READY with
   typed aggregate reasons rather than broadening to humanoids.

## Reproduction

```text
<canonical-dotnet> restore tools/research/Arcanum.ActorVisualProbe/Arcanum.ActorVisualProbe.csproj --locked-mode
<canonical-dotnet> build tools/research/Arcanum.ActorVisualProbe/Arcanum.ActorVisualProbe.csproj --no-restore
<canonical-dotnet> run --project tools/research/Arcanum.ActorVisualProbe/Arcanum.ActorVisualProbe.csproj --no-build -- --self-test
<canonical-dotnet> run --project tools/research/Arcanum.ActorVisualProbe/Arcanum.ActorVisualProbe.csproj --no-build -- --module <module> --resource <resource> --prototype-root <prototype-root> --source-id <safe-label>
```

## Implemented / not implemented / remaining work

Implemented: a standalone net8.0, `Arcanum.Data`-only, metadata-only probe;
input identity checks; bounded DAT opening/ordinal census; v119 type offset
admission after the two object-identifier envelopes; type-15 exclusion;
type-16 accounting; and direct self-tests for version/type/truncation/privacy
and exit contract.

Not implemented: a selected actor, full MOB/PRO parser, prototype inheritance,
resource mapping, action/facing resolution, composition handling, frame binding,
anchor delta, source cadence, renderer, movement, collision, navigation, or
archive precedence.

Remaining work: the three evidence gates above, followed by a separate reviewed
revision of this bounded research candidate.
