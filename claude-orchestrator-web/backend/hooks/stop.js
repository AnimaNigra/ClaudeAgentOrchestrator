#!/usr/bin/env node
'use strict'
// Claude Code Stop hook — fires when Claude finishes a response and waits for input.
// Signals the orchestrator to mark this agent as idle.
const url = process.env.CLAUDE_ORCHESTRATOR_URL
const agentId = process.env.CLAUDE_AGENT_ID
if (!url || !agentId) process.exit(0)
fetch(`${url}/api/agents/${agentId}/hook/stop`, { method: 'POST' }).catch(() => {})
