# Independent synthetic audio lab (ARC-UI-002)

This is a standalone macOS Godot.NET diagnostic, not game audio integration or
original Arcanum control parity. Core has no Godot or Simulation dependency.
Only own synthetic PCM is generated in memory; no game installation, extracted
payload, toolkit, codec, middleware, input preferences or other lab is needed.

## Provenance and boundaries

Implementation and square-wave recipe are independently authored, under the
repository MIT license. CE/TIG are one research lineage, not two independent
confirmations: [CE pin a7ff41b](https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd)
is attributed research context governed by its Sustainable Use License. No CE
code, mechanical translation or runtime dependency is used. The proposal/source
boundary in [FR-MEDIA-003/004](../requirements/ui-media-lifecycle.md#fr-media-003)
remains: schemes, positional attenuation, footsteps, combat transitions, dialogue
callers and resource resolution need separate integration files/tasks. This lab
is neither RES022 nor UI003 acceptance; original WAV/MP3 playback and broad
format compatibility are unverified. Physical hearing/latency/device quality
is human follow-up, not an additional approval gate and not implied by mixer RMS.

## Stable reusable surface

`Core/AudioPolicy.cs` defines immutable `PlayCommand`, `AudioHandle`,
`AudioResult`, `AudioLimits`, `BusLevel` and `PlaybackState`. `EncodedAudio.Create`
copies caller bytes only after kind/length admission; the decoder gets its own
copy. Callers supply an explicit `Wav` or `Mp3` kind, never guess by extension.
All mutations of policy and backend, including `Dispose`, are serialized on the
owner/engine thread. Wrong-thread commands return `WrongThread`; wrong-thread
Dispose throws without touching state. No finalizer, cancellation callback or
background operation creates/frees engine objects. Queries are owner-thread
snapshots, not a cross-thread API; there is no distributed exactly-once promise.

`Godot/AudioBackend.cs` exposes:

- `Create(parent, limits, out backend)` after parent enters the tree. It rejects
  Dummy output, an existing lease or nonempty/non-Master audio topology.
- `Play(command, CancellationToken)`, synchronous bounded decode/admission.
  Cancellation is observed before reservation and after decoder completion;
  it cannot interrupt the engine decoder mid-call. A cancelled/failed operation
  returns no usable handle and releases its reservation/temporary stream.
- `Stop(handle)`, `Pause(handle, bool)`, `Fade(handle, gain, realSeconds)`,
  `State(handle, out state)`, `Tick(realSeconds)`, `SetLevel`, `ApplyPreferences`.
- Read-only `Preferences`, `Limits`, `ActivePlayers`, `NativePlayers`,
  `RetainedBytes`, `CacheBytes`, `Driver`, `Device` and engine-thread `Dispose`.

The backend policy is internal: consumers cannot reserve slots independently
of engine nodes. Handles bind a random instance owner, slot and monotonically
increasing generation. Double stop returns `AlreadyStopped` until slot reuse;
reuse makes the old handle `StaleHandle`. Foreign/invalid handles never stop a
new player. Finished one-shots release on `Tick`; loops stop explicitly. Zero
gain does not automatically release a loop. Fade continues while a player is
paused; `Tick` must use presentation real time, never authoritative game time.

This adapter exclusively owns a standalone process's `Master -> Music/Voice/
SFX/Ambient` topology. It creates only its four children, restores the original
Master volume/mute and removes its children at disposal. It never selects an
output device, changes OS volume, system/default routing or gamepad settings.
A game host with existing buses requires a separately accepted ownership adapter,
not removal or renaming of its buses. Substantive integration belongs in new
adapter/caller files; do not make this lab a generic engine framework.

## Limits and failure profile

Defaults are 8 simultaneous players, 1,048,576 bytes per encoded AND retained
stream buffer, 8,388,608 total charged bytes, zero cache. Constructor limits are
explicit; players must be 1..64, byte limits positive, total >= per-buffer.
Reservation charges encoded length; stream admission additionally charges decoded
PCM length (WAV) or retained compressed length (MP3). Charging each invocation's
input conservatively covers shared command inputs even when they share storage.
Charges are removed on failure, stop, natural completion and disposal. No cache
holds a stopped stream. These are payload retention/admission limits, not a
claim that process RSS equals payload bytes: a bounded decoder copy, WAV Data
inspection copy, engine mixing state and runtime overhead are transient/extra.
The recipe reports sampled actual process-tree peak RSS separately (150ms
sampling can miss a shorter allocation peak). Lab runtime budget is 1GiB RSS;
owned export/build/evidence budget is 2GiB; check free space before export. Do
not remove shared caches, another task's artifacts or retained evidence.

WAV admission is deliberately narrow metadata validation, not a custom codec:
canonical RIFF/WAVE 44-byte header, PCM16, mono/stereo, 8..48kHz, exact sizes,
block alignment and byte rate, at least one frame. Ancillary chunks, compressed
WAV, extensions and mismatched declared lengths return `InvalidMedia` before
Godot decoding and cannot expand an untrusted compressed WAV. Actual decode is
`AudioStreamWav.LoadFromBuffer`; decoded length/format are checked again.
`AudioStreamMP3.LoadFromBuffer` is a bounded-buffer engine wrapper with per-player
compressed-stream ownership. Valid MP3 is optional and NOT VERIFIED here: no
licensed/author-created MP3 fixture or encoder installation is assumed.
Invalid commands/limits and decoder failures do not mutate simulation (the lab
has no simulation reference). Results distinguish cancellation, disposed/thread/
handle errors, player/per-buffer/total limits, malformed media, decoder/backend
failure and preference failures. No startup success is claimed for Dummy.

## Mixer preferences and UI

Volumes are finite normalized 0..1 values; mute is a separate boolean. Invalid
values are rejected, not clamped. Master defaults to .5 and every child to 1,
all unmuted. Zero volume produces effective bus silence without changing the
stored mute. A fade's zero gain uses -80dB (approximately 1e-4 linear), not a
claim of exact digital zero. The capture oracle tolerates RMS below .0001.

The dedicated user preference file is `user://audio-preferences-v1.json` in
`SteamwovenSyntheticAudioLab`, or an explicit diagnostic `--prefs=PATH`.
`Core/Preferences.cs` stores v1 with all five volume/mute pairs. Missing file
returns `PreferencesMissing` plus defaults; corrupt/missing/extra/duplicate
fields, incompatible version, nonfinite/out-of-range volume or >4096 bytes
return `PreferencesInvalid` plus the same defaults; IO/access errors return
`PreferencesIo`. Save uses a same-directory unique temporary file and rename;
no game-save/input/OS file is read or changed. Save is explicit (button/API),
not implicit on exit, so a corrupt file is not silently rewritten.

Each child row has Loop/Once/Stop/Pause-resume/Fade-1s; sliders and mute buttons
apply immediately. Replacing a row's playback stops the old handle. Defaults,
Save preferences and Quit are separate actions. A normal WM-close request
runs the same disposal path. The lab is not a keyboard/gamepad parity claim.

## Checks and independent oracles

Use the pinned tools and process-local environment in
[toolchain.md](../workflow/toolchain.md). From repository root:

```sh
DOTNET="$HOME/.local/share/dotnet/dotnet"
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
"$DOTNET" restore experiments/audio-lab/Tests/AudioLab.Tests.csproj --locked-mode
"$DOTNET" run --project experiments/audio-lab/Tests/AudioLab.Tests.csproj --no-restore
"$DOTNET" restore experiments/audio-lab/Godot/AudioLab.csproj --locked-mode
```

Pure executable tests have literal oracles: 8044 total WAV bytes, 8000 PCM bytes,
4000 mono frames at 16kHz (.25s), +4096/-4096 square wave every 20 samples. Limits
use literal 2 players/4 bytes/8 total and beyond values; tests cover cancellation,
foreign/stale/double stop, disposal, invalid time/gain/volume/preferences, immutable
buffers, independent controls and 1000 play-stop cycles. None derives its expected
limit from `AudioLimits.Lab`.

Own `tools/check.py --evidence OWNED_DIR --run RUN --label UNIQUE -- COMMAND...`
executes a bounded real command, retaining timestamp/run/task/workspace/head/source
digest/exit/PID/log hash and sampled process-tree peak RSS. Create an ignored owned
`artifacts/audio-lab/<run>/ownership.json` first. Labels are immutable; do not
reuse a stale log. Read the logs and reports: exit zero alone is not acceptance.
Evidence created before the final commit is exploratory, not final-head proof.
Set `AUDIO_LAB_SCRATCH` to an existing owned scratch directory; each command gets
a fresh retained `TMPDIR` there, recorded in its receipt. Do not delete it before
review/delivery or claim all engine caches are isolated by that variable.

Run fresh canonical editor build/import (not `dotnet run` for the Godot app):

```sh
env __GODOT_SHELL_ENV_SET=1 DOTNET_ROOT="$HOME/.local/share/dotnet" \
  PATH="$HOME/.local/share/dotnet:/usr/bin:/bin:/usr/sbin:/sbin" \
  "$GODOT" --path experiments/audio-lab/Godot --headless --editor \
  --build-solutions --verbose --quit
```

Read the verbose editor log: SDK8.0.425 and canonical dotnet must be selected,
never Homebrew. This headless editor check proves tool selection, NOT audio.
Then launch graphically (no `--headless`): `GODOT --path ... -- --verify
--prefs=OWNED_PATH --report=OWNED_PATH`. This exercises real CoreAudio, all four
simultaneous WAV buses, literal actual decoded waveform/channels/length, captured
nonzero Master signal, one-child isolated mute/half gain, remaining-bus activity,
pause/fade/stop, nodes/byte release, one-shot completion, cancellation/bad media,
limits/foreign/stale/thread/disposal, repeated cycles and backend restart. The
capture sits BEFORE Master volume/mute: child mute/gain forwarding is measured;
Master's normalized volume/mute independence is separately read from AudioServer.
It does NOT claim loopback capture of hardware or physical hearing.
The actual rendered 1000x650 viewport is saved beside the report, not inside the app.

Run two separate processes with `--write-prefs` then `--read-prefs` using the SAME
owned file: literal Master .5/false, Music .25/true, Voice .75/false, SFX .5/true,
Ambient1/false must survive restart and populate the UI. `--defaults-prefs` with
a deliberately corrupt owned file verifies typed failure/defaults at startup.
Repeated `--verify` launches establish cold startup/shutdown, not just same-process
restart. `--ordinary-seconds=2` is a bounded ordinary graphical four-loop launch,
without the verification capture; without these diagnostic options it remains
interactive until Quit/window close.

## Own macOS export recipe

No terrain/gamepad/display endpoint, root registry or seal is used. Export only
a clean committed candidate after required source/root checks. Use a NEW owned
`artifacts/audio-lab/<run>/export-<head>/` directory; retain all logs outside it.
Fresh editor bridge/import is required before export; then canonical Godot
`--headless --path experiments/audio-lab/Godot --export-debug macOS
ABSOLUTE_EXPORT_DIR/AudioLab.zip --verbose`. Inspect exporter log/bridge; use
matching 4.7.2.mono templates. `tools/artifact.py extract --directory DIR
--head SHA` safely extracts only AudioLab.app members without external symlinks;
`seal` applies an ad-hoc local signature and writes an immutable complete file/
mode/hash/directory manifest plus archive hash/head/provenance. This is not
Developer-ID signing, notarization or permission to change macOS security.

Launch the exported `AudioLab.app/Contents/MacOS/AudioLab` WITHOUT editor/path
arguments through the same native verification/restart suite. For ordinary launch
also use macOS LaunchServices `open -n -W AudioLab.app --args
--ordinary-seconds=2 ...`, verify report and normal shutdown (the opener exit alone
is not app exit proof). Finally run `artifact.py verify` to check every payload
entry/archive hash plus deep/strict codesign AFTER runtime, keeping runtime logs
outside the sealed payload. Manifest digest belongs in the exact-head handoff.
No game files are eligible payload; only own compiled lab, synthetic recipe,
Godot/.NET engine dependencies and Main scene/project configuration are exported.

Review is independent native exact-head FULL PASS for AC1–AC6's implementation
stage; local-only / CI_NOT_REQUIRED does not waive GitHub-required gates. One
private PR and separate policy-Reviewer/observed-owner merge plus receipt remain
future delivery stages. Never call publication/receipt complete from a local PASS.
