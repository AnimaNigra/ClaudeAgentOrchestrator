#!/usr/bin/env node
'use strict'
// Claude Code Statusline hook — receives structured JSON with usage data on stdin.
// Outputs a compact status string to stdout (displayed in Claude's status bar)
// and forwards the full JSON to the orchestrator backend.
const url = process.env.CLAUDE_ORCHESTRATOR_URL
const agentId = process.env.CLAUDE_AGENT_ID

async function main() {
  let raw = ''
  for await (const chunk of process.stdin) raw += chunk

  let data = {}
  try { data = JSON.parse(raw) } catch {}

  // Output for Claude Code's own status bar
  const model = data.model?.display_name ?? ''
  const ctx = data.context_window?.used_percentage
  const rate = data.rate_limits?.five_hour?.used_percentage
  let parts = []
  if (model) parts.push(model)
  if (ctx != null) parts.push(`ctx ${ctx}%`)
  if (rate != null) parts.push(`rate ${Math.round(rate)}%`)
  process.stdout.write(parts.join(' | '))

  // Forward to orchestrator
  if (url && agentId) {
    await fetch(`${url}/api/agents/${agentId}/hook/statusline`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: raw,
    }).catch(() => {})
  }
}
main()
