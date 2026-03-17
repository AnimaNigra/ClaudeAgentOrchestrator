# Voice Dictation Feature — Design Spec

**Date:** 2026-03-15
**Branch:** feat/priority-list
**Status:** Approved

---

## Overview

Add voice dictation support to the Claude Orchestrator web app. The user can click a floating microphone button, dictate a prompt, optionally attach an image, and confirm — the text and/or image are then sent directly to the active agent.

---

## Requirements

- Floating microphone button in the terminal area (bottom-right), visible only when an agent is active
- Clicking the button opens a modal dialog
- Dialog starts speech recognition immediately on open
- Transcript fills into an editable textarea in real-time as the user speaks
- User can manually edit the transcript
- User can optionally attach an image file
- Confirm sends the input to the active agent; Cancel discards
- No backend changes required

---

## Approach

**Web Speech API (browser-native)**

Uses `window.SpeechRecognition` / `window.webkitSpeechRecognition`. No external API, no cost, no backend changes. Supported in Chrome and Edge. Firefox is not supported — a graceful error message is shown if the API is unavailable.

---

## Components

### `VoiceDictateButton.vue` (new)

Floating button, positioned `absolute bottom-4 right-4` inside the terminal container in `AgentsView.vue`.

- Renders only when `store.activeAgentId` is set
- Emits `open` to parent to show the dialog
- Visual: microphone icon, subtle styling consistent with the existing dark theme

### `VoiceDictateDialog.vue` (new)

Modal dialog (similar pattern to `NewTaskModal.vue` — Teleport to body, backdrop click to cancel).

**State:**
- `transcript: string` — accumulated final results, editable by user
- `interimText: string` — current partial result shown in grayed-out style appended after transcript
- `imageFile: File | null` — optional image attachment
- `isRecording: boolean`
- `error: string | null`

**Behavior:**
- On open: call `recognition.start()`
- `onresult`: append final results to `transcript`; update `interimText` with interim
- `onerror`: set `error`, stop recording
- `onend`: set `isRecording = false`
- Stop/restart button toggles recording
- Image file picker (accepts `image/*`)
- Cancel: close dialog, stop recognition, discard
- Confirm: see Data Flow below

**Props:** `show: boolean`, `agentId: string`
**Emits:** `close`

### `AgentsView.vue` (modified)

Add `VoiceDictateButton` and `VoiceDictateDialog` inside the existing template. The dialog receives `activeAgentId` as `agentId` prop.

---

## Data Flow

```
[Click mic button]
    ↓
[VoiceDictateDialog opens + SpeechRecognition.start()]
    ↓
[Transcript fills in real-time, user can edit]
    ↓
[User optionally attaches image via file picker]
    ↓
[Confirm clicked]
    ├─ text present → store.sendKeystroke(agentId, text + '\r')
    └─ image present → POST /api/agents/{agentId}/upload (FormData)
                        backend saves file to agent's tmp/ dir
                        backend automatically types file path + '\r' into terminal
```

Text and image are sent as two separate inputs (text first, then image). This is intentional — the existing upload endpoint handles the image path injection automatically, removing the need for backend changes.

---

## Error Handling

| Scenario | Behavior |
|---|---|
| Browser lacks Web Speech API | Error shown in dialog: "Váš prohlížeč nepodporuje hlasové zadávání. Použijte Chrome nebo Edge." |
| Speech recognition error | Error shown in dialog with `error.message`; recording stops |
| No active agent | Mic button hidden (not rendered) |
| Confirm with empty text and no image | Confirm button disabled |
| Image upload fails | Error shown in dialog |

---

## Existing Code Reused

- `store.sendKeystroke(agentId, data)` — already used for paste in `TerminalPanel.vue`
- `POST /api/agents/{id}/upload` — already used for image paste in `TerminalPanel.vue`
- Modal pattern (Teleport + backdrop) — same as `NewTaskModal.vue`
- Dark theme colors — consistent with existing Tailwind classes

---

## Out of Scope

- Language selection for speech recognition (uses browser default)
- Whisper / backend transcription
- Combining text + image into a single terminal input (would require backend change)
- Continuous/always-on dictation mode
