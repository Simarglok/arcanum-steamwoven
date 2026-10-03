# Crash Site NPC → script → dialog evidence

## Scope and claim categories

This is one bounded, identifier-only research chain, not a gameplay implementation.
It resolves the base-world Virgil instance to a conditional **SAP_DIALOG** entry.
It does not establish the first automatically initiated conversation in the original
executable. Heartbeat and dialog-override attachments are separate, excluded callers.
The installation and all selected archives/files were opened read-only. No original
text, expressions, asset bytes, gameplay operand values or machine paths are published.

Categories used below: **ObservedField** (selected bytes), **SourceClaim** (pinned
CE semantics), **ProjectPolicy** (instrumentation or proposed contract), **Unknown**.
Original execution, original display, saved-world overrides and untraced branches
are **NOT_VERIFIED**. An explicit selected archive is not the runtime precedence winner.

CE specification lineage is `alexbatalov/arcanum-ce`, revision
`a7ff41b300ef712f0e7d088183a3d08957110cdd`, Sustainable Use License 1.0
([license](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/LICENSE.md)).
Source bodies informed field and caller interpretation; no CE code was copied or
mechanically translated. This is not clean-room research. CE-derived references
are not independent corroboration. Toolkit editor documentation is supporting
provenance only; no legacy executable was run. The original fields and separately
authored selected-byte worksheet check results, not independent source lineage.

## Immutable selected input and association

| Claim | Category | Locator / observation | Disposition |
|---|---|---|---|
| NDE-01 | ObservedField + SourceClaim | Selected module MapList payload SHA-256 `3df16fd2bd186415da0738c25a6b7ea8a32134f5ea95817e3bcfafdbcf55843f`; exactly one START_MAP, map ID 5000. Its map basename transforms to the selected loose game aggregate basename by the pinned rotation/reversal rule. | Base-world map context; not an editor `.mob` or a guessed archive member. |
| NDE-02 | ObservedField | Aggregate SHA-256 `a548f611ff5de9842bba1f700f6614c0b9651cf3ab640a23d19efc7626dde6a5`, 1,823,280 bytes, 16-byte prefix then 7,399 records consumed exactly to EOF. Selected instance begins at 743644, ends at 744462, object type 16, prototype ID 17102, identity SHA-256 `bd0cff305f47b07e845a120966b402c5c44073aba7ffd1407b626f93733bf601`. | Unique matching instance. Complete census uses external explicit layout descriptors; selected fields separately reconciled without those descriptors. |
| NDE-03 | ObservedField + SourceClaim | Instance location is present; its little-endian 8-byte value hashes to `5474307a69c758197cbcae7ee15276f82c4dae9e423a7496f1805b48405c5d10`. Private coordinate comparison places it adjacent to the selected START_MAP location. Description ID 31073 resolves to Virgil in module gamedesc; name hash `e3b7a5d30dcfe893ffafd9ef2407468b7e8253cf461bf728db69195ee282e3ba`. | Identity comes from object and description, not SCR filename. Module description payload hash `8254286b832d30cb1ce13a9ccffd4fc887ae7b4d0783f2251a2b8f4a85f68e60`. |
| NDE-04 | ObservedField | Selected prototype identity/type matches instance prototype/type; prototype SHA-256 `084a85124a37c41097806dface638254194efd46a5b318009e1c841c42797ea4`. Script array is explicitly present on the instance, with six occupied event keys. Key 9 (SAP_DIALOG) binds script ID 1324. | Whole instance array overrides prototype array; absent overridden slots are NOT individually inherited. |
| NDE-05 | ObservedField | Attachment flags/counters and selected SCR initial flags/counters have identical 8-byte header hash `af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc`; privately checked clear local flags. | These are distinct state origins, not interchangeable headers. Later state writes belong to the effective attachment. |
| NDE-06 | ObservedField + SourceClaim | Explicit selected script archive has one numeric catalogue candidate for 1324; SCR payload hash `f726b47ca8a960b9d876583320fb9592bd79009ff1880c71650bceecfa43e4fe`, 6,268 bytes, 47 entries, capacity 50. Derived DLG is uniquely present in explicit selected DLG archive; payload hash `34a49a2c160cf8a97856cc0a9e48baa04fe0611c5d6bf05b5f52a7310f45e53a`, 409 unique records. | Exact raw selectors, no normalization or First/last-match precedence. Collisions outside selected sources remain Unknown. |

Full before/after SHA-256 fingerprints were equal for the five selected files:
module archive `8bddf7ae4b8f0606efd64f9137f904cfa4787d80ef85fa04d06a3def9dadc76f`,
aggregate and prototype above, script archive
`4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d`,
DLG archive `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`.
Payload fingerprints alone would not prove whole-file integrity.

### Source seams for NDE-01..06

All links below are the pinned revision, not mutable main:

- [Game aggregate versus editor mobile load](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1360-L1501), [name transform](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1668-L1688).
- [Object framing](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.c#L4770-L4880), [whole-array inheritance](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj.c#L2540-L2593), [description source selection](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/description.c#L123-L145).
- [SAP and operand declarations](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.h#L7-L81), [catalogue and DLG derivation](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script_name.c#L38-L207), [SCR framing](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L3430-L3555).

## Conditional caller and entry rule

The inspected object-dialog caller invokes SAP_DIALOG with PC as triggerer, NPC
as attachee, no extra object and SCR line 0
([caller](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/tb_ui.c#L248-L258),
[dispatch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L3866-L3908)).
On the explicitly assumed context of NPC not following this PC, both encountered
local-flag queries false, and NPC not jilted, entries 0..3 fall through their
DO_NOTHING else actions. Entry 4 is TRUE → DIALOG; its immediate value selects
DLG NPC node **1**. This is a conditional rule, not selection of the minimum ID.
True alternatives jump to SCR lines 6, 8, 10, 12; they are not followed here.
A missing following/jilted query result is a frontier, never false by default.

The callback receives PC, NPC, script ID, current SCR line and distinct DLG node
([callback](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1513-L1534)).
UI/source admission additionally depends on life/consciousness/speaking/combat,
local-dialog exclusivity and hostility checks
([UI admission](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/dialog_ui.c#L118-L211),
[hostility/generated branch](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L471-L506)).
The static chain does not prove these predicates actually held in the original
executable. SCR execution guards, host/state-copy behavior and post-callback
return handling remain separate dependencies; no full VM is admitted.

## Complete selected operation/frontier ledger

SCR operations use signed little-endian i32 type, eight one-byte operand tags,
eight i32 value slots: 44 bytes per condition/action, three per entry (132 bytes).
The SCR code container begins at byte 64. Only active operands have semantics;
unused slots contain opaque values and MUST NOT be treated as extra arguments.
DLG operations below are opaque ASCII byte fields privately classified by mnemonic,
with decimal operand tokens. No raw expressions or operand values are published.

| Instance(s) | Required behavior / arity / participants | Order and provenance | Support |
|---|---|---|---|
| SCR line 0 FOLLOWING (type 18) | Two focus operands: attachee NPC, triggerer PC; query following relationship. | First branch query; `script.c:869-890`. | NeedsImplementation; probe consumes explicit nullable context, does not query world. |
| SCR lines 1 and 3 LOCAL_FLAG (type 3) | One immediate value tag 3; read distinct bit indices from effective attachment header. | Before dialog callback; `script.c:621-629`. | NeedsImplementation as SCR dispatch; existing DLG LF port alone is not SCR execution. |
| SCR line 2 JILTED (type 39) | One focus operand, attachee NPC; query jilted state. | Between local-flag checks; `script.c:1269-1300`. | NeedsImplementation. |
| SCR line 4 TRUE (type 0) | Zero active arguments. | Select true action unconditionally; `script.c:590-592`. | NeedsImplementation in future dispatcher. |
| SCR lines 0..3 DO_NOTHING else (type 0) | Zero active arguments; continue next SCR line. | Taken only under stated predicates. | NeedsImplementation as control flow, not permission to skip unknown effects. |
| SCR lines 0..3 GOTO true (type 3) | One immediate SCR-line operand. | Alternatives leave chosen entry path; `script.c:1511-1512`. | NeedsImplementation; alternative conversations NOT_VERIFIED. |
| SCR line 4 DIALOG (type 4) | One immediate DLG-node operand; callback receives five contextual arguments. | Entry-resolution boundary; `script.c:1513-1534`. | NeedsImplementation. |
| DLG node 1 action instances 1,2,3,5: GF | Two operands: global-flag identity and boolean value; shared world flag state. | Ordered instances, with IN at instance 4; `dialog.c:1973-1975`. | NeedsImplementation; first effect already outside current runtime. |
| DLG node 1 action instance 4: IN | One signed item-name identity; sign selects direction. Selected case NPC/substitute inventory → PC, with transfer failure/drop behavior. | Inventory effect, not display or IQ gate; `dialog.c:2004-2056`. | NeedsImplementation (inventory query/transfer/location/failure policy). |
| DLG node 1 action instance 6: QU | Two operands: quest identity/state; participants PC plus NPC context. | After preceding effects; `dialog.c:1958-1960`. | SupportedByExistingRuntime as a standalone command in its accepted domains, NOT admission of the entire entry. |
| DLG node 1 action instance 7: RU | One rumor identity; PC rumor log. | Last entry effect; `dialog.c:2075-2077`. | SupportedByExistingRuntime individually, not a skipped-prefix execution path. |
| DLG NPC nodes 1 and 9 Condition fields | Speech metadata, not Player condition evaluation; source derives speech ID using script identity. | Before NPC text/action handling; `dialog.c:2264-2277,2968-2991`. | NeedsImplementation (speech/media boundary); no false LF/unknown-condition rejection as NPC eligibility. |
| Player IDs 2..7 gender/IQ | Alternate field is gender selector, not female text. Signed IQ gate: positive minimum, negative maximum; dumb adjustment precedes IQ. | Gender → IQ → condition; `dialog.c:1309-1355`. | NeedsImplementation in caller; current runtime accepts caller booleans only. |
| Player IDs 2..7 RE condition | One signed threshold: positive reaction minimum, negative maximum; NPC reaction toward PC. | After entry effects and gender/IQ; `dialog.c:1489-1499`. | NeedsImplementation; do not substitute LF or guessed reaction. |
| Player IDs 4,5,7 RE action | One operand; selected unsigned form sets reaction to absolute value, not relative adjustment. | Selected reply effect before transition; `dialog.c:1933-1956`. | NeedsImplementation. |
| Player IDs 2,3,6 actions | Empty field. | No response command. | SupportedByExistingRuntime individually. |
| Player IDs 2..7 Direct edge | All point to NPC node 9; choose response ID 2 conditionally eligible. | One selected edge; selected action empty, destination action empty. | Direct shape SupportedByExistingRuntime, but whole chain not admitted. |
| Node 9 next decision | Stop before evaluating its replies. | First subsequent frontier. | NeedsImplementation for continued traversal; no Exit or special form encountered in selected step. |

The source-catalogued first-entry sequence is **GF, GF, GF, IN, GF, QU, RU**.
Research records later fields without executing or skipping the unsupported first GF.
Source-first-entry effects occur before reply enumeration; no rollback is inferred.
The six structural immediate replies are IDs **2,3,4,5,6,7**. Selected serialized
and numerical-source grouping agree on this input (F-ORDER closed for this input
only). CE enumerates up to five *eligible* replies; current project runtime
rejects six structural replies rather than truncating. It also lacks GF/IN/RE.
Thus **the current one-edge runtime cannot run this conversation**. Do not choose
a different reply group, truncate, or discard effects to manufacture compatibility.

## Text, variant and decoding findings

| ID | Main text bytes | Alternate bytes | Role | Non-ASCII display bytes |
|---|---:|---:|---|---:|
| 1 | 158 | 158 | NPC | 0 |
| 2 | 28 | 0 | Player | 0 |
| 3 | 13 | 0 | Player | 0 |
| 4 | 28 | 0 | Player | 0 |
| 5 | 13 | 0 | Player | 0 |
| 6 | 63 | 0 | Player | 0 |
| 7 | 63 | 0 | Player | 0 |
| 9 | 219 | 219 | NPC | 0 |

No name-substitution delimiters were present in these selected display fields.
No generated-text directive was encountered by private selected-field inspection.
Ordinary NPC variant selection uses **PC** critter/gender, not NPC gender
([selection](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2332-L2339)).
Empty alternate is not automatically replaced by main text. Player neutral/matching
selector and IQ tests are analysis policies, not a new production formatter.
Source name substitutions recognize PCNAME/NPCNAME case-insensitively; unknown
paired tokens add no name; unmatched delimiter behavior is unsafe/Unknown, so
instrumentation fails closed
([substitution](https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/dialog.c#L2362-L2389)).

The selected sample supports lossless **ASCII subset** decoding only, not universal
UTF-8, CP1251, installation-wide encoding or Russian localization. Production
DlgReader preserves high display bytes; the probe refuses to classify non-ASCII
text without a specified decoder. Synthetic valid UTF-8-looking and invalid byte
sequences both hit this explicit unknown-codepage boundary. Actual display,
glyph coverage, voice resolution and generated/localized text remain NOT_VERIFIED.

## Reproduction and instrumentation policy

The independently authored [NpcDialogProbe](../../tools/research/Arcanum.NpcDialogProbe/Program.cs)
uses production `DatArchive.Open/Entries/ReadEntry` and `DlgReader.Read`.
It is outside the solution and must be built/tested explicitly:

```sh
dotnet build tools/research/Arcanum.NpcDialogProbe/Arcanum.NpcDialogProbe.csproj
dotnet run --no-build --project tools/research/Arcanum.NpcDialogProbe -- --self-test
dotnet run --no-build --project tools/research/Arcanum.NpcDialogProbe -- --original --manifest "$LOCAL_MANIFEST"
```

The private JSON manifest has PascalCase properties `Module`, `Aggregate`,
`Prototype`, `Scripts`, `Dialogs` (explicit existing local files), `ObjectHash`
(lowercase SHA-256 of the serialized instance ObjectID), `Following`, `Jilted`
(nullable booleans; selected reproduction uses false explicitly) and `Layouts`.
Layouts map object type numbers to `Fields` and `GroupLengths`. Fields are explicit
serialized descriptors: `i`, `p64`, `poid`, `s`, `ai32`, `au32`, `ai64`, `au64`,
`ascript`, `aquest`, `aoid`; groups reproduce the source mask sections, not a
continuous bit numbering assumption. Source field types may be inspected against
[existing descriptors](../../src/Arcanum.Data/StaticObjects/StaticObjectLoader.cs)
but that static reader is not an NPC loader. In particular container inventory
must be an object-ID **array**, not a solitary handle; this research supplies the
corrected descriptor externally and does not change the production static reader.
Review the external layout against the pinned source; it is an evidence input,
not proof of its own correctness. Never place the private manifest in a PR.

Caps: aggregate 32 MiB / 16,384 records, prototype 2 MiB, SCR 1 MiB / 4,096
entries / 128 visited steps, DLG 512 KiB; array population 4,096, bitmap 128 words,
string 4,096 bytes. SCR capacity may exceed count; exact payload size derives
from count, not capacity or serialized pointer-placeholder. Malformed framing,
reserved masks, duplicates/missing resources, wrong role, invalid IDs, cycles,
unsupported active operations and missing queries fail with redacted categories.
The probe never extracts resources or prints original exceptions/paths/text.
It reads all record boundaries but semantically traces only the necessary SCR
path and first DLG frontier. A success establishes structural evidence, not VM
or runtime admission. Logs and independent selected-field/count worksheets are
retained privately with task/run/workspace/head/command/UTC/digest bindings.

The separate oracle uses fixed discovered locators but recomputes selected masks,
scalar/pointer widths, location, description, event bitmap rank, catalogue uniqueness,
SCR counts/active operands, DLG roles/edge/effect order and field lengths directly.
It does not import probe layout constants or result-derived expected values.
The full aggregate census uses common descriptors and is not an independent
source lineage or a second semantic proof. Synthetic fixtures reorder IDs,
exercise actual production DAT/DLG seams and use literal expected outcomes.

See the [invocation contract](../scripting/npc-dialog-entry-contract.md) for state
ownership and future gaps; [prior DLG research](dialog-evidence.md) remains valid
within its original structural scope, not as a claim of initial gameplay entry.
