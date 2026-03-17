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
- Interim (partial) results shown in a separate read-only element below the textarea, styled in gray
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

Floating button, positioned `absolute bottom-4 right-4` inside the `<main>` container in `AgentsView.vue`. `<main>` must have `relative` positioning added for this to work correctly.

- Renders only when `store.activeAgentId` is set
- Emits `open` to parent to show the dialog
- Visual: microphone icon, subtle styling consistent with the existing dark theme

### `VoiceDictateDialog.vue` (new)

Modal dialog (same pattern as `NewTaskModal.vue` — Teleport to body, backdrop click to cancel).

**Props:** `show: Boolean`, `agentId: String`
**Emits:** `close`

**State:**
- `transcript: string` — accumulated final results, editable by user
- `interimText: string` — current partial result from SpeechRecognition; displayed as a separate read-only element below the textarea with grayed-out styling (not inline in the textarea, as `<textarea>` only supports plain text)
- `imageFile: File | null` — optional image attachment
- `isRecording: boolean`
- `error: string | null`

**State reset on re-open:**
A `watch` on the `show` prop (same pattern as `NewTaskModal.vue`) resets all state when `show` transitions to `true`: `transcript`, `interimText`, `imageFile`, `isRecording`, `error` are all cleared.

**SpeechRecognition configuration:**

A new `SpeechRecognition` instance is created each time the dialog opens (inside the `show` watcher). Required options:

```js
recognition.continuous = true      // keep listening until explicitly stopped
recognition.interimResults = true  // fire onresult with partial results
```

**Behavior:**
- On open: create new instance with the above config, call `recognition.start()`
  - The browser microphone permission prompt may appear over the open dialog on first use — this is acceptable behavior
- `onresult`: iterate results; append final results to `transcript` after running through `normalizeTranscript()`; set `interimText` to the latest interim result
- `onerror`: set `error`, set `isRecording = false`
- `onend`: set `isRecording = false`, clear `interimText`
- **Stop button**: calls `recognition.stop()`, sets `isRecording = false`
- **Restart button** (shown when not recording): creates a new instance (cannot reuse stopped instance — calling `start()` again throws `InvalidStateError`), calls `start()`. The existing `transcript` is preserved; recording appends to it.
- **Cancel**: calls `recognition.abort()` (not `stop()`, to prevent `onend` firing after the dialog has closed), then emits `close`
- **Image file picker**: `accept="image/*"`, sets `imageFile`
- **Confirm button**: disabled when both `transcript.trim()` is empty and `imageFile` is null

**Transcript normalization:**

The speech recognition engine applies inverse text normalization (ITN) — it automatically converts spoken numbers to formatted time (`10:30`), currency, etc. This is unwanted for prompt dictation. Every final result is passed through a `normalizeTranscript(text)` function before being appended to `transcript`:

```js
function normalizeTranscript(text) {
  // Undo time normalization: "10:30" → "10 30", "1:00" → "1 00"
  return text.replace(/\b(\d{1,2}):(\d{2})\b/g, '$1 $2')
}
```

This function is defined as a module-level utility inside `VoiceDictateDialog.vue`.

**Confirm logic:**
1. If `transcript.trim()` is non-empty: `store.sendKeystroke(agentId, transcript.trim() + '\r')`
2. If `imageFile` is non-null: `POST /api/agents/{agentId}/upload` with `FormData`. The FormData field name must be `"file"` (matches the backend `IFormFile file` binding parameter). Check `response.ok` — if false, parse `{ error }` from the JSON body and display the error. The upload endpoint also validates `Content-Type` and returns 400 for non-images (the `accept` attribute is advisory only).
3. Emit `close`

**Image-only confirm is valid and intentional.** When an image is confirmed without text, only the upload request is sent. The backend's upload endpoint automatically injects the saved file path + `\r` into the agent's terminal via `WriteInputAsync`, so no explicit keystroke is needed for the image path.

### `AgentsView.vue` (modified)

- Add `relative` to the `<main>` element
- Add `VoiceDictateButton` inside `<main>` (sibling of `<TerminalPanel>`)
- Add `VoiceDictateDialog` (Teleport to body handles positioning)
- Pass `store.activeAgentId` as `agentId` to the dialog

---

## Data Flow

```
[Click mic button]
    ↓
[VoiceDictateDialog opens]
[new SpeechRecognition({ continuous: true, interimResults: true }).start()]
    ↓
[Transcript fills in real-time (final) + interimText shown below (partial)]
[User edits transcript / attaches image]
    ↓
[Confirm clicked]
    ├─ text non-empty → store.sendKeystroke(agentId, text.trim() + '\r')
    └─ image attached → POST /api/agents/{agentId}/upload (FormData)
                         check response.ok; show error on 4xx/5xx
                         on success: backend injects file path + '\r' into terminal
```

Text and image are sent as two separate inputs (text first, then image). This is intentional — the existing upload endpoint handles image path injection automatically, requiring no backend changes.

---

## Error Handling

| Scenario | Behavior |
|---|---|
| Browser lacks Web Speech API | Error shown in dialog: "Váš prohlížeč nepodporuje hlasové zadávání. Použijte Chrome nebo Edge." |
| Speech recognition error | `error` shown in dialog; `isRecording` set to false |
| No active agent | Mic button not rendered |
| Confirm with empty text and no image | Confirm button disabled |
| Image upload HTTP error (400/404/500) | Parse `{ error }` from response JSON, display in dialog |
| Image upload network failure | Catch exception, display generic error in dialog |

---

## Existing Code Reused

- `store.sendKeystroke(agentId, data)` — used for paste in `TerminalPanel.vue`
- `POST /api/agents/{id}/upload` — used for image paste in `TerminalPanel.vue`
- Modal pattern (Teleport + backdrop + `watch(show)` reset) — same as `NewTaskModal.vue`
- Dark theme Tailwind classes — consistent with existing components

---

## Out of Scope

- Language selection for speech recognition (uses browser default)
- Whisper / backend transcription
- Combining text + image into a single terminal input
- Continuous/always-on dictation mode
