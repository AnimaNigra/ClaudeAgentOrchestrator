#!/usr/bin/env node
/**
 * PTY Proxy — spawns a command inside a pseudo-terminal and bridges I/O.
 *
 * stdin  protocol (UTF-8 text lines):
 *   INPUT:<base64>\n         → forward decoded bytes to PTY stdin
 *   RESIZE:<cols>x<rows>\n  → resize the PTY
 *
 * stdout protocol (UTF-8 text lines):
 *   DATA:<base64>\n          → base64-encoded PTY output chunk
 *
 * Environment variables:
 *   PTY_CMD   – executable to spawn (default: "claude")
 *   PTY_ARGS  – JSON array of arguments (default: ["--dangerously-skip-permissions"])
 *   PTY_CWD   – working directory
 *   PTY_COLS  – initial columns  (default: 220)
 *   PTY_ROWS  – initial rows     (default: 50)
 */

const nodePty = require('node-pty');
const readline = require('readline');

const cmd  = process.env.PTY_CMD  || 'claude';
const args = JSON.parse(process.env.PTY_ARGS || '["--dangerously-skip-permissions"]');
const cwd  = process.env.PTY_CWD  || process.cwd();
const cols = parseInt(process.env.PTY_COLS || '220', 10);
const rows = parseInt(process.env.PTY_ROWS || '50',  10);

// Spawn the target process inside a PTY
const ptyProcess = nodePty.spawn(cmd, args, {
  name: 'xterm-256color',
  cols,
  rows,
  cwd,
  env: { ...process.env, TERM: 'xterm-256color', COLORTERM: 'truecolor' },
});

// Forward PTY output → stdout as base64 lines
ptyProcess.onData(data => {
  const b64 = Buffer.from(data, 'utf8').toString('base64');
  process.stdout.write('DATA:' + b64 + '\n');
});

// Proxy exits when PTY exits
ptyProcess.onExit(({ exitCode }) => {
  process.exit(exitCode ?? 0);
});

// Read commands from stdin
const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });

rl.on('line', line => {
  if (line.startsWith('INPUT:')) {
    const bytes = Buffer.from(line.slice(6), 'base64');
    ptyProcess.write(bytes.toString('binary'));
  } else if (line.startsWith('RESIZE:')) {
    const parts = line.slice(7).split('x');
    const c = parseInt(parts[0], 10);
    const r = parseInt(parts[1], 10);
    if (c > 0 && r > 0) ptyProcess.resize(c, r);
  }
});

// If parent closes stdin, kill the PTY
rl.on('close', () => ptyProcess.kill());
