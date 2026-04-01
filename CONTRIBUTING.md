# Contributing

Thanks for your interest in contributing to Claude Agent Orchestrator!

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (LTS)
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code)

## Development Setup

```bash
# Clone the repo
git clone https://github.com/AnimaNigra/ClaudeAgentOrchestrator.git
cd ClaudeAgentOrchestrator

# Install frontend dependencies
cd claude-orchestrator-web/frontend
npm install

# Start the dev server (backend + frontend + pty-proxy)
cd ../backend
dotnet run
```

The app will be available at `http://localhost:6001`.

## Project Structure

- `claude-orchestrator-web/backend/` — ASP.NET Core 9 backend (SignalR, REST API, PTY management)
- `claude-orchestrator-web/frontend/` — Vue 3 + Vite frontend
- `claude-orchestrator-web/backend/pty-proxy/` — Node.js proxy for node-pty
- `claude-orchestrator-web/tests/` — xUnit tests

## Making Changes

1. Fork the repo and create a feature branch
2. Make your changes
3. Run the tests: `dotnet test claude-orchestrator-web/tests/`
4. Make sure the app builds: `dotnet build claude-orchestrator-web/backend/`
5. Open a pull request with a clear description of what you changed and why

## Code Style

- **Backend:** Follow existing C# conventions (PascalCase for public members, async/await)
- **Frontend:** Vue 3 Composition API, Tailwind CSS for styling
- **Language:** All UI text and code comments in English

## Reporting Issues

Please open an issue on GitHub with:
- Steps to reproduce
- Expected vs actual behavior
- Your OS and .NET/Node versions
