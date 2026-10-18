# Viewer sound output and volumes (ARC-UI-040)

Status: **Implemented bounded component**, task 3 of the
[audio plan](../research/audio-evidence.md#task-plan). The AREA viewer (exterior, cave and map
mode) can play the original's sounds: the module's [sound catalogue](sound-catalog.md) resolves
an id or a file, the payload is admitted, and the engine decodes and plays it on one of the
original's three channels at the original's volumes. Music and ambient play through it since ARC-UI-041 ([scheme player](scheme-player.md)); positional effects
(ARC-UI-042), voice (ARC-UI-043) and interface sounds (ARC-UI-044) will use it too.

## Volume rules (Godot-free)

`Arcanum.Presentation.Audio` holds the original's arithmetic
([audio study](../research/audio-evidence.md) SN6, SN8, SN9, SN16, checked in the executable):

| Item | Rule |
| --- | --- |
| `AudioVolumes` | Effects, Voice, Music settings 0–10, default 5: the original's three option bars. A user preference, never in a save. |
| `AudioVolumeRules.ChannelVolume` | `setting × 127 / 10`, then for effects and music `× 80 / 100` (integer steps): effects and music 50 at 5 and 101 at 10, voice 63 at 5 and 127 at 10. |
| `StartVolume` / `AdjustedVolume` | A new effect starts at `volume × channel / 100`; a recalculation (listener or source move, later tasks) uses `volume × channel / 127`. Both scales are the original's. |
| `Amplitude` | A Steamwoven choice, NOT_VERIFIED against the original's mixer: a 0–127 volume maps linearly to amplitude, clamped (a fresh full effect at setting 10 computes 128). |
| `AudioVolumesCodec` | The preference text `{"version":1,"effects":E,"voice":V,"music":M}`; anything else reads as null (defaults). |

Ambient scheme sounds use the effects channel and scheme songs the music channel (SN14, SN16).
The original has no master or separate ambient bar; none is added here.

## Backend (Godot)

`ViewerAudioBackend` (`tools/terrain-viewer/Godot`) adds the buses `Effects`, `Voice` and
`Music` under Master (only those it created are removed again) and owns up to 32 stream players:

- `Play(payload, channel, volume, loop)`: an admitted WAV (PCM, through `AudioStreamWav`) or MP3
  (`AudioStreamMP3`, decoded progressively from memory) at a 0–127 volume. A looped WAV repeats
  its whole sample range, a looped MP3 the whole file. Statuses: `Playing`, `Silent` (decoded
  and started, but the engine has no output device: the Dummy driver of a headless run),
  `NotAdmitted`, `DecodeFailed`, `VoiceLimit` (nothing is stolen), `Disposed`.
- `Play(..., pan)`: a pan −1…1 routes the voice through one of nine panning buses of its channel (`Effects pan −4` …
  `+4`, an `AudioEffectPanner` at each quarter, sending to the channel bus; a Steamwoven choice, the original pans
  continuously). Pan buses are created on first use and removed with the others.
- `FadeOut(handle, ms)`: fades a voice to silence and releases it (25 ms for scheme songs); a stale handle is a no-op.
- Handles carry a slot and a generation: a stale handle never stops a later sound in its slot.
  `Stop`, `StopAll`, `IsPlaying`, `Volume`.
- `FlattenChannel(channel, channelVolume)`: when a setting changes, the original sets every live
  sound of that channel to the bare channel volume (SN8); the backend does the same.
- `Tick()` once per frame frees finished one-shot players. Engine thread only; a failure is a
  status and never touches game state.

`AudioVolumesStore`: `user://audio_volumes_v1.json` holding the codec text; a missing, unreadable
or invalid file loads 5/5/5. `AUDIO_VOLUMES_TEST_PATH` isolates tests; an automated run
(self-test, native smoke) without it has no file at all and never reads or changes the owner's
setting.

## Viewer integration

- At startup the viewer creates the backend and loads the volumes (log line
  `ARC-UI-040 audio driver=… audible=… volumes=E/V/M from=…`).
- The catalogue is opened on first use for the module archive the area was read from, through
  the same content root as the script content (any map of any module), and kept until exit.
- `PlaySfx(id)` plays the original's non-positional effect: volume 127 and the centre pan (the
  plain play call), scaled by the effects channel at start (SN6).
- Palette (Ctrl+Shift+F9 / Start): **Sound test: next interface sound** plays the next of the
  original's fixed interface sounds (3008, 3009, 3010, 3011, 3002–3007, 3027, 3028; SN31) and
  reports the outcome; the palette's sound line shows the device, the three settings, the
  catalogue and the last result, and the AREA status line shows the last result.
- The volume settings have no controls yet (ARC-UI-044 adds them); the file can be edited.

## Checks

- `tests/Arcanum.Presentation.Tests/AudioVolumesTests.cs`: channel volumes, the two scales,
  amplitude clamp, settings validation, codec.
- Self-test (headless and windowed, synthetic PCM, no original data):
  `ARC-UI-040 self-test audio PASS`: decode and start on a channel bus (`Silent` without a
  device), loop kept by `Tick`, flatten, refusals, voice limit, stale handles after slot reuse,
  buses removed, the volume file at the test path.
- Native smoke on the original installation (exterior AREA and map mode):
  `ARC-UI-040 native-smoke audio PASS`: the module's catalogue opens; every fixed interface sound
  resolves, is admitted, decodes and starts on the effects channel; the world-map travel music
  (an MP3 in the module folder) loops on the music channel; with an output device the music and at
  least one interface sound are still playing a few frames later; stop all leaves no voice. Hearing itself is not checked.
