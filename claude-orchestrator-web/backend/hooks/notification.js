#!/usr/bin/env node
'use strict'
// Claude Code Notification hook — fires when Claude sends a notification.
// Forwards the notification message to the orchestrator.
const url = process.env.CLAUDE_ORCHESTRATOR_URL
const agentId = process.env.CLAUDE_AGENT_ID
if (!url || !agentId) process.exit(0)

async function main() {
  let raw = ''
  for await (const chunk of process.stdin) raw += chunk
  let data = {}
  try { data = JSON.parse(raw) } catch {}
  await fetch(`${url}/api/agents/${agentId}/hook/notification`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  }).catch(() => {})
}
main()
