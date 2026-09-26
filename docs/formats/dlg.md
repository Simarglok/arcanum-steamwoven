# DLG: bounded seven-field research subset

## Scope

This is a bounded reader-research record for one caller-selected DAT entry. It
is not a runtime dialogue system, resource-precedence policy, SCR interpreter,
text extractor, UI, or gameplay implementation. The accompanying
`Arcanum.DialogProbe` accepts one explicit archive path and one exact raw DLG
identifier; it neither scans an installation nor writes beside an input.

The initial inspection candidate is `dlg\01324Virgil.dlg` in `arcanum3.dat`
(ordinal 160; declared/stored bytes 56,782/12,335; archive SHA-256
`cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`). Its
filename is not evidence of character identity, initial encounter, live
archive winner, or runtime entry node. Those claims remain **unknown**.

## Observed grammar and bounded policy

Pinned `arcanum-ce` source revision
`a7ff41b300ef712f0e7d088183a3d08957110cdd` describes a brace-delimited entry
with seven fields: numeric record ID, text, alternate/gender field, IQ
field, condition field, response value, and action field
(`src/game/dialog.c:150-161,2641-2802`). The probe independently implements only the observed physical brace/comment
boundary. It treats fields 1 and 2 as opaque bounded bytes and applies ASCII
checks only to numeric fields (0, 3, and 5) and operational fields (4 and 6).
It emits no original text or field content.

| Field | Bounded role | Evidence category | Boundary |
| --- | --- | --- | --- |
| 0 | unique signed decimal record ID | source fact | probe accepts full `Int32` range; duplicate, malformed, outside-range, or empty IDs reject |
| 1 | primary text bytes, reported only as aggregate length | source fact | opaque bounded bytes; encoding is not established |
| 2 | alternate/gender bytes, reported only as aggregate length | source fact | opaque bounded bytes; encoding is not established |
| 3 | empty denotes NPC; nonzero denotes player response | source fact | a nonempty zero rejects; role is structural, not runtime selection |
| 4 | condition opcode classes | source fact / local structural experiment | printable ASCII, emitted as two-letter class counts only |
| 5 | signed decimal response value; empty parses as zero in the cited source | source fact | probe accepts full `Int32` range; positive direct candidate, zero/empty exit candidate, negative special candidate |
| 6 | action opcode classes | source fact / local structural experiment | printable ASCII, emitted as two-letter class counts only |

`//` comments outside fields, CRLF/LF accounting, empty fields, and fields
between braces are handled by the probe. Nested braces, unterminated braces,
NUL bytes, malformed numeric/non-ASCII operational fields, unsupported opcode
shape, and all policy overflows fail closed. Text bytes are not decoded or
normalized. The raw resource selector is rejected before ASCII encoding unless
it is printable ASCII, exactly `dlg\\basename.dlg`, and free of drive, slash,
separator, or traversal forms; replacement-byte matching is impossible. The probe caps stored/decoded DAT entries
at 1 MiB, DLG payload at 512 KiB, records at 10,000, fields at exactly seven,
and each field at 4 KiB. These are safety limits, not universal DLG facts.

Synthetic non-game examples use the form
`{100}{synthetic-npc}{}{ }{}{200}{}` and
`{200}{synthetic-player}{}{5}{}{0}{}`. They contain no original dialogue.

## References and traversal

Source `dialog_check` treats a positive player response as a target record and
reports a missing target or a target with the wrong structural role
(`src/game/dialog.c:2968-2991`). Source `sub_417590` distinguishes positive,
zero, and negative response values (`src/game/dialog.c:2537-2549`). The probe
therefore classifies references as `direct`, `exit`, `special`, `dangling`,
`ambiguous`, or `unresolved`; it never silently converts a missing or
unsupported edge into a direct branch.

A bounded structural slice starts at a **research-selected start**: the lowest
numeric structurally NPC record. This deterministic convenience is explicitly
not a claim that the record is the runtime dialogue entry. Traversal increments
its node and reference counters before appending a row; it never materializes a
frontier beyond 256 included nodes or 1,024 examined references. It stops at a
second decision frontier or at the **first** exit, special/generated transition,
callback, uninterpreted condition/action, dangling/ambiguous/unresolved target,
cycle, or either cap. The stopped row is retained and classified, but no later
response is inspected or selected after that fixed stop. Because this probe has
no operation evaluator, every nonempty condition/action is an unsupported
boundary (with `SC` additionally classified callback-shaped). Only if no fixed
stop occurs does it choose a direct candidate deterministically by smallest
positive target ID, then smallest response ID, among the only currently
admissible service set: the source-backed serialized transition itself.
First-record order is never entry evidence.

## Encoding and unknowns

The physical parser is byte-oriented. It accepts printable ASCII only for IDs,
numeric operational values, and condition/action fields, and reports opaque
text fields solely through length aggregates. It does not establish the original text encoding,
localization rules, text substitution semantics, action vocabulary, generated
menus, SCR callbacks, or runtime archive precedence. A successful structural
run is reader-subset evidence only, not game-dialog-runtime readiness.
