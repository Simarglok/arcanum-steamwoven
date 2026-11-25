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

## Script float lines (ARC-SCRIPT-3124)

Checked in the executable (float line action 23, handler 0x44B1F0 → the dialog interface's float callback, the pointer set by 0x567330 to
0x568430 → speech display 0x568540 → voice start 0x5688D0; the AI barks' global 0x5F8488 is a different pointer):

- **Identity.** The line is loaded as the dialog's own NPC line is (0x413A30 mode 1, 0x416840); its speech id is the same composer on the same field: the
  running script's number and the line's condition field (0x4189C0), so the id of a script float line is `AreaScriptFloat.VoiceOf`. A line the DLG does not
  hold, or one without a condition field, is −1 and a playing voice is left alone (0x5688D0); the viewer shows a missing line as one space, which makes no bubble, so no voice either.
- **Listener.** The voice's gender is that of the display's listener, the nearby PC the float step found (the hero here): gender stat 26 = 0 picks the
  female file with the male file as fallback, any other value the male file only. The same listener picks the line's text (alternate for a female PC; the tile and use-script host reads the male text only, as before). The viewer takes the hero's gender from the conversation host's profile (male without one).
- **Per speaker.** The voice start sits inside the per-speaker loop, so a line floated over several objects restarts the same file for each (the last start
  survives: one voice). Speakers that are dead or busy are not silenced (they only do not turn); a PC speaker is voiced too. A multiplayer client without the
  broadcast bit says nothing at all (not applicable).
- **Viewer.** `AreaScriptFloat.Voice` carries the identity from the two hosts that read the DLG (dialog scripts, `AreaScriptHost`; tile and use scripts,
  `AreaUseScriptHost`); `TickScriptFloats` starts it with the bubble through the dialog voice channel. The float newspaper headline (86) and the AI barks keep their
  own paths (the follower comments' voice: ARC-AI-1640).
- **Checks.** `AreaDialogVoiceTests`, `AreaUseScriptHostTests` (the tile script's float carries its voice); the voice smoke steps a float line with and without a voice.

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

- Combat taunts' voice by the AI barks other than the follower comments, story slides,
  generated greetings (the generic greeting is a silent floating line, ARC-DEV-324).
  Script float lines are voiced since ARC-SCRIPT-3124 (above).
- Nothing was compared by ear with the running original.
