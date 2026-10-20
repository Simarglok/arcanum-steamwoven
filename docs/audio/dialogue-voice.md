# Dialogue voice (ARC-UI-043)

Status: **Implemented bounded component**, task 8 of the
[audio plan](../research/audio-evidence.md#task-plan). An NPC line spoken in the AREA viewer's
conversation (exterior, cave and map mode, any module) plays its recorded voice as the original does.

## Which file

Checked in the 1.0.7.4 executable ([audio study](../research/audio-evidence.md) SN21, SN22, SN36):

- **Identity.** `(script number & 0x7FFF) << 16 | (atoi(condition field) & 0xFFFF)`, the DLG's script
  number and the NPC line's condition field. A condition field that is empty or only C white space is
  stored as no field, so the line has no voice (−1). `SoundCatalog.SpeechConditionField` and
  `SpeechIdentity` (Data), `AreaConversationStart.VoiceIdentity` (viewer core).
- **File.** `sound/speech/NNNNN/vL_g.mp3` with the PC's gender (`m`, `f`); a female PC falls back to
  the male file, never the reverse (`SoundCatalog.ResolveSpeech`, ARC-DATA-040). A missing file plays
  nothing and changes nothing in the dialog.
- **Volume.** The voice channel's volume, not positional (SN16: the voice play call sets the voice
  channel volume directly).

## When it plays

`Arcanum.Presentation.Audio.DialogVoicePlayer` (Godot-free):

- Each NPC line shown plays its voice once; a voiced line first stops the voice that plays.
- A line without a voice leaves the previous voice playing.
- Every reply the PC picks stops the voice before it is processed, whatever it does (another line, the
  end of the dialog, a return to the script); the viewer's panel reports each processed pick.
- Closing the dialog does not stop it (an original quirk kept in both settings); a map change stops
  every sound.

The viewer runs it every frame while a conversation is open; the status line names the last voice and
counts the voices played.

## Checks

Unit tests: `DialogVoiceTests` (Presentation), `AreaDialogVoiceTests` (viewer core). Opt-in real-data
census `AreaDialogVoiceOriginalTests` (`DAT_ROOT`): of the 124 NPC lines the interiors' conversations
show today (first choosable reply each turn, never an insult; 57 before ARC-WORLD-043 admitted the `ME` condition), 27 have a voice identity and every one names an installed
file, for a male and a female PC. Native smoke `ARC-UI-043 native-smoke voice PASS` (any map, after the
audio smoke): an installed line voice plays on the voice channel at the voice volume, a missing one
stops it and plays nothing; the talk smoke records each tried speaker's voice (no map's talk smoke
reaches a voiced speaker yet: the Caladon underground stops at the combat-mode smoke on clean main,
and the Derian Ka temple's voiced speaker is not the one drawn).

## NOT READY

- Floating lines and combat taunts (their voices come with the text-float layer), story slides,
  generated greetings (the generic greeting is not modelled).
- Nothing was compared by ear with the running original.
