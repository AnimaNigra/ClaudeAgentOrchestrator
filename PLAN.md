# Claude Agent Orchestrator – Projektový plán

## Přehled projektu

Terminálová TUI aplikace (Python + Rich/Textual) pro orchestraci více Claude Code CLI agentů na Windows. Aplikace umožňuje spouštět, řídit a monitorovat paralelní agenty z jednoho dashboardu s MCP zpětným kanálem a desktopovými notifikacemi.

---

## Architektura

```
┌──────────────────────────────────────────────────┐
│              TUI Dashboard (Textual)              │
│  ┌────────┐ ┌────────┐ ┌────────┐ ┌───────────┐ │
│  │Agent 1 │ │Agent 2 │ │Agent 3 │ │  Log/Feed │ │
│  │[status]│ │[status]│ │[status]│ │           │ │
│  │progress│ │progress│ │progress│ │ timeline  │ │
│  └────────┘ └────────┘ └────────┘ └───────────┘ │
│  ┌──────────────────────────────────────────────┐ │
│  │            Command Input Bar                 │ │
│  └──────────────────────────────────────────────┘ │
└──────────────┬───────────────────────────────────┘
               │ subprocess management
    ┌──────────┼──────────┐
    │          │          │
┌───▼───┐ ┌───▼───┐ ┌───▼───┐
│claude │ │claude │ │claude │
│-p ... │ │-p ... │ │-p ... │
│  CLI  │ │  CLI  │ │  CLI  │
└───┬───┘ └───┬───┘ └───┬───┘
    │         │         │
    └────┬────┴─────────┘
         │ MCP tool calls
   ┌─────▼──────┐
   │ MCP Server │
   │ (FastMCP)  │
   │ zpětný     │
   │ kanál      │
   └─────┬──────┘
         │ WebSocket / HTTP
   ┌─────▼──────┐
   │ TUI update │
   │ + notif.   │
   └────────────┘
```

---

## Technický stack

- **Python 3.11+**
- **Textual** – TUI framework (nástupce Rich pro interaktivní aplikace)
- **FastMCP** – Python knihovna pro vytvoření MCP serveru (`pip install fastmcp`)
- **asyncio** – asynchronní řízení procesů
- **subprocess / asyncio.create_subprocess_exec** – spouštění Claude CLI
- **win10toast-click** nebo **plyer** – Windows desktop notifikace
- **winsound** – zvukové notifikace (built-in Python na Windows)

---

## Struktura projektu

```
claude-orchestrator/
├── README.md
├── requirements.txt
├── pyproject.toml
│
├── orchestrator/
│   ├── __init__.py
│   ├── main.py                 # Vstupní bod – spuštění TUI
│   ├── app.py                  # Textual App – hlavní TUI layout
│   │
│   ├── agents/
│   │   ├── __init__.py
│   │   ├── manager.py          # AgentManager – spouštění/zastavení agentů
│   │   ├── agent.py            # Agent dataclass – stav jednoho agenta
│   │   └── process.py          # AsyncProcess wrapper pro claude CLI
│   │
│   ├── mcp_server/
│   │   ├── __init__.py
│   │   └── server.py           # FastMCP server – zpětný kanál od agentů
│   │
│   ├── notifications/
│   │   ├── __init__.py
│   │   └── notifier.py         # Desktop + zvukové notifikace (Windows)
│   │
│   ├── ui/
│   │   ├── __init__.py
│   │   ├── dashboard.py        # Dashboard widget – přehled agentů
│   │   ├── agent_card.py       # Widget pro jednoho agenta
│   │   ├── log_panel.py        # Panel s logem událostí
│   │   └── command_bar.py      # Vstupní řádek pro příkazy
│   │
│   └── config/
│       ├── __init__.py
│       └── settings.py         # Konfigurace (cesty, modely, limity)
│
├── agents_config/
│   └── example_tasks.json      # Příklad konfigurace úloh pro agenty
│
└── CLAUDE.md                   # Instrukce pro Claude Code agenty
```

---

## Fáze implementace

### Fáze 1: Základ – Agent Manager + CLI wrapper

**Cíl:** Spouštět Claude Code CLI jako subprocess a číst jeho výstup.

**Úkoly:**

1. Vytvoř `orchestrator/agents/agent.py`:
   - Dataclass `Agent` s atributy: `id`, `name`, `status` (idle/running/done/error), `task`, `output`, `created_at`, `finished_at`
   - Enum `AgentStatus` pro stavy

2. Vytvoř `orchestrator/agents/process.py`:
   - Třída `ClaudeProcess` obalující `asyncio.create_subprocess_exec`
   - Metoda `run(task: str, **kwargs)` volající: `claude -p "{task}" --output-format stream-json`
   - Asynchronní čtení stdout po řádcích (stream-json formát)
   - Podpora parametrů: `--max-turns`, `--allowedTools`, `--append-system-prompt`
   - Podpora `--continue` a `--resume <session-id>` pro navazování

3. Vytvoř `orchestrator/agents/manager.py`:
   - Třída `AgentManager` spravující slovník aktivních agentů
   - Metody: `spawn_agent(name, task, **kwargs)`, `kill_agent(id)`, `list_agents()`, `get_agent(id)`
   - Maximální počet paralelních agentů (konfigurovatelné, default 5)
   - Event systém – callback při změně stavu agenta

**Testy:** Spusť jednoho agenta s jednoduchým úkolem, ověř že se výstup správně čte.

---

### Fáze 2: MCP Server – zpětný kanál

**Cíl:** Agenti mohou aktivně reportovat svůj stav orchestrátoru.

**Úkoly:**

1. Vytvoř `orchestrator/mcp_server/server.py` pomocí FastMCP:
   - Tool `report_progress(agent_id: str, status: str, message: str, progress_pct: int = -1)`:
     - Stavy: "started", "working", "blocked", "done", "error"
     - Uloží zprávu do sdílené fronty (asyncio.Queue)
     - Vrátí potvrzení agentovi
   - Tool `get_task(agent_id: str)`:
     - Agent si vyzvedne svůj úkol z fronty
   - Tool `request_input(agent_id: str, question: str)`:
     - Agent požádá uživatele o vstup, orchestrátor zobrazí dotaz v TUI
   - Tool `share_result(agent_id: str, result: str, artifact_path: str = "")`:
     - Agent sdílí výsledek s orchestrátorem a případně cestu k souboru

2. MCP server se spustí na stdio transportu
   - Každý Claude CLI agent se spouští s: `claude mcp add orchestrator -- python path/to/server.py`
   - Alternativně: přidat do globální konfigurace `~/.claude/settings.json`

3. Vytvoř `CLAUDE.md` šablonu pro agenty:
   ```markdown
   ## Pravidla orchestrace
   - Na začátku úlohy zavolej tool `report_progress` se statusem "started"
   - Každých několik kroků zavolej `report_progress` se statusem "working" a popisem co děláš
   - Pokud potřebuješ vstup od uživatele, zavolej `request_input`
   - Po dokončení zavolej `report_progress` se statusem "done" a `share_result` s výsledkem
   - Při chybě zavolej `report_progress` se statusem "error" a popisem chyby
   ```

**Testy:** Spusť agenta s MCP serverem, ověř že zprávy tečou zpět do orchestrátoru.

---

### Fáze 3: TUI Dashboard

**Cíl:** Interaktivní terminálové rozhraní s přehledem agentů.

**Úkoly:**

1. Vytvoř `orchestrator/app.py` – hlavní Textual App:
   - Layout: horní panel (agent karty), pravý panel (log), spodní panel (command bar)
   - Reaktivní aktualizace při změně stavu agentů
   - Klávesové zkratky:
     - `n` – nový agent (otevře dialog pro zadání úkolu)
     - `k` – kill vybraného agenta
     - `q` – ukončení orchestrátoru
     - `Tab` – přepínání mezi agenty
     - `Enter` – odeslání příkazu

2. Vytvoř `orchestrator/ui/agent_card.py`:
   - Widget zobrazující: jméno agenta, status (barevný indikátor), aktuální úkol, poslední zpráva, uplynulý čas, progress bar (pokud agent reportuje %)
   - Barevné kódování statusu:
     - 🟢 zelená = running/working
     - 🔵 modrá = idle
     - ✅ bílá = done
     - 🔴 červená = error
     - 🟡 žlutá = blocked/waiting for input

3. Vytvoř `orchestrator/ui/log_panel.py`:
   - Scrollovatelný log všech událostí od všech agentů
   - Formát: `[čas] [agent_name] zpráva`
   - Barevné odlišení podle agenta
   - Maximální počet řádků v bufferu (default 500)

4. Vytvoř `orchestrator/ui/command_bar.py`:
   - Input widget pro příkazy:
     - `spawn <name> <task>` – spusť nového agenta
     - `kill <id|name>` – zastav agenta
     - `send <id|name> <message>` – pošli zprávu agentovi (continue session)
     - `list` – vypiš všechny agenty
     - `detail <id|name>` – zobraz detail agenta (celý výstup)
     - `clear` – vyčisti log
     - `help` – zobraz nápovědu
   - Autocomplete pro jména agentů
   - Historie příkazů (šipka nahoru/dolů)

**Testy:** Spusť TUI, vytvoř agenty přes command bar, ověř že se karty aktualizují v reálném čase.

---

### Fáze 4: Notifikace

**Cíl:** Desktop a zvukové upozornění při důležitých událostech.

**Úkoly:**

1. Vytvoř `orchestrator/notifications/notifier.py`:
   - Třída `Notifier` s metodami:
     - `notify_done(agent_name, summary)` – agent dokončil úlohu
     - `notify_error(agent_name, error)` – agent narazil na chybu
     - `notify_input_needed(agent_name, question)` – agent potřebuje vstup
   - Windows implementace:
     - Desktop toast: použij `plyer` (`pip install plyer`) – funguje cross-platform
     - Zvuk: `winsound.PlaySound("SystemAsterisk", winsound.SND_ALIAS)` pro done
     - Zvuk: `winsound.PlaySound("SystemHand", winsound.SND_ALIAS)` pro error
   - Konfigurovatelné: zapnout/vypnout zvuky, zapnout/vypnout toasty

2. Napoj notifikace na události z AgentManager a MCP serveru:
   - Když MCP server přijme `report_progress` se statusem "done" → `notify_done`
   - Když MCP server přijme `report_progress` se statusem "error" → `notify_error`
   - Když MCP server přijme `request_input` → `notify_input_needed`
   - Když agent subprocess skončí (neočekávaně) → `notify_error`

**Testy:** Spusť agenta, ověř že při dokončení přijde Windows toast notifikace a zvuk.

---

### Fáze 5: Konfigurace a polish

**Cíl:** Konfigurovatelnost, error handling, UX vylepšení.

**Úkoly:**

1. Vytvoř `orchestrator/config/settings.py`:
   - TOML nebo JSON konfigurační soubor: `~/.claude-orchestrator/config.toml`
   - Nastavení:
     ```toml
     [general]
     max_agents = 5
     log_buffer_size = 500

     [claude]
     default_model = "sonnet"    # sonnet, haiku, opus
     default_max_turns = 20
     default_allowed_tools = []  # prázdné = všechny
     append_system_prompt = ""   # extra instrukce pro všechny agenty

     [notifications]
     desktop_enabled = true
     sound_enabled = true
     notify_on_done = true
     notify_on_error = true
     notify_on_input_needed = true

     [mcp]
     server_auto_register = true  # automaticky přidat MCP server ke každému agentovi
     ```

2. Error handling:
   - Graceful shutdown – při ukončení TUI zastav všechny agenty
   - Reconnect – pokud agent spadne, nabídni restart
   - Timeout – pokud agent nereportuje déle než X minut, označ jako "unresponsive"

3. UX vylepšení:
   - Uložení a načtení "task templates" – předdefinované úlohy
   - Export logu do souboru
   - Agent detail view – celý výstup agenta v scrollovatelném panelu
   - Skupiny agentů – možnost spustit předdefinovanou sadu agentů najednou

---

## Požadavky na prostředí

```
# requirements.txt
textual>=0.50.0
fastmcp>=0.1.0
plyer>=2.1.0
tomli>=2.0.0        # pro TOML config (Python <3.11)
```

**Prerekvizity:**
- Python 3.11+
- Node.js (pro Claude Code CLI)
- Claude Code CLI nainstalované globálně: `npm install -g @anthropic-ai/claude-code`
- Platný Anthropic API klíč nebo Claude předplatné

---

## Spuštění

```bash
# Instalace
cd claude-orchestrator
pip install -e .

# Spuštění
python -m orchestrator.main

# Nebo po instalaci
claude-orchestrator
```

---

## Poznámky pro implementaci

1. **MCP registrace**: Při prvním spuštění se orchestrátor pokusí automaticky zaregistrovat svůj MCP server do `~/.claude/settings.json`, aby byl dostupný všem Claude Code sessions. Pokud to selže, vypíše instrukce pro manuální registraci.

2. **Stream-JSON parsing**: Claude CLI s `--output-format stream-json` posílá NDJSON (newline-delimited JSON). Každý řádek je jeden JSON objekt s informacemi o průběhu. Parser musí být odolný vůči nevalidním řádkům.

3. **Windows specifika**:
   - Používej `asyncio.WindowsProactorEventLoopPolicy()` pro subprocess support
   - Cesty používej s `pathlib.Path` pro kompatibilitu
   - Testuj s Windows Terminal (lepší Unicode podpora než cmd.exe)

4. **Souběh MCP a CLI**: MCP server běží jako samostatný process, ne uvnitř TUI event loopu. Komunikace mezi MCP serverem a TUI přes `asyncio.Queue` nebo jednoduché HTTP/WebSocket.

5. **Postupná implementace**: Začni Fází 1 a 3 paralelně – agent manager + základní TUI. Pak přidej MCP (Fáze 2), notifikace (Fáze 4) a config (Fáze 5). Každá fáze by měla být funkční a testovatelná samostatně.
