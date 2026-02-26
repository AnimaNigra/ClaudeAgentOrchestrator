#!/usr/bin/env node
'use strict'
// Claude Code PreToolUse hook — fires before every tool call.
// Sends the tool info to the orchestrator and waits for user approval/denial.
// Returns {"decision":"approve"} or {"decision":"block","reason":"..."} to Claude.

async function main() {
  const url = process.env.CLAUDE_ORCHESTRATOR_URL
  const agentId = process.env.CLAUDE_AGENT_ID
  if (!url || !agentId) {
    process.stdout.write(JSON.stringify({ decision: 'approve' }))
    return
  }

  let raw = ''
  for await (const chunk of process.stdin) raw += chunk
  let toolInfo = {}
  try { toolInfo = JSON.parse(raw) } catch {}

  try {
    // POST to orchestrator — this call blocks until user responds (up to 2 min)
    const res = await fetch(`${url}/api/agents/${agentId}/hook/pre-tool`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(toolInfo),
      signal: AbortSignal.timeout(120_000),
    })
    const result = await res.json()
    if (result.approved) {
      process.stdout.write(JSON.stringify({ decision: 'approve' }))
    } else {
      process.stdout.write(JSON.stringify({
        decision: 'block',
        reason: result.reason ?? 'User denied this action.',
      }))
    }
  } catch {
    // Timeout or network error → allow by default so agent is not stuck
    process.stdout.write(JSON.stringify({ decision: 'approve' }))
  }
}

main()
