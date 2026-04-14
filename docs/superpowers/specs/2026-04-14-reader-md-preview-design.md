# Reader — Markdown Preview s Mermaid.js

**Datum:** 2026-04-14
**Stav:** Návrh (design)
**Autor:** Jan Nemec (ve spolupráci s Claude Code)

## 1. Motivace a cíl

Přidat do Claude Agent Orchestrator samostatnou view **Reader**, která slouží jako prohlížeč Markdown dokumentů (`.md`, `.markdown`, `.mdx`, `.txt`) s podporou:

- Mermaid.js diagramů
- syntax highlightingu v code blocích
- relativních obrázků
- otevírání souborů přes nativní dialog nebo drag-and-drop

Cíl je získat pohodlnou čtečku dokumentace / specifikací / poznámek, která je součástí existující aplikace a respektuje její vzhled.

## 2. Rozsah (scope)

**V rozsahu:**

- Nová route `/reader` + view `ReaderView.vue`, položka **"Reader"** v horní navigaci vedle History / Worktrees / Priorities / Tasks.
- Toolbar s tlačítky **Open file** a **Export PDF**, tabová lišta otevřených dokumentů.
- Levý resizable sidebar s **Table of Contents** (scroll-spy) a sekcí **Recent files**.
- Drag-drop overlay pro rychlý náhled souboru (lite mode — bez obrázků/reloadu).
- Hybridní model otevírání:
  - **Full mode** — uživatel zadá absolutní cestu přes `OpenFileDialog`; backend načte obsah, servíruje relativní obrázky, sleduje změny souboru pro live reload.
  - **Lite mode** — drag-drop nebo `<input type="file">`; soubor se čte přes `FileReader` bez backendu, obrázky s relativní cestou mají placeholder.
- Více tabů najednou, persistence stavu v `localStorage` (po reloadu se taby obnoví načtením z disku).
- Recent files (max 20, dedupe podle cesty, pouze full-mode otevření).
- Live reload přes backend `FileSystemWatcher` a SignalR.
- Export do PDF přes `window.print()` s dedikovaným print CSS.
- Vitest + @vue/test-utils testovací stack pro frontend, unit testy readeru.

**Mimo rozsah:**

- Procházení file system stromem (no file tree / file browser).
- Editace MD souborů (read-only).
- Verze pro mobilní zařízení (optimalizace mimo desktop).
- Backend unit testy pro `ReaderController` (backend zatím nemá testovací stack; zvážit samostatně).
- Podpora non-Markdown formátů nad rámec `.txt`.
- Elektron/Tauri-native integrace pro získání absolutní cesty z drag-drop (nemožné ve webovém prostředí).

## 3. Architektura

### 3.1 Umístění v projektu

```
claude-orchestrator-web/
  backend/
    Controllers/
      ReaderController.cs          ← NEW
    Services/
      FileWatcherService.cs        ← NEW (singleton, spravuje FileSystemWatcher instance)
    Hubs/
      ReaderHub.cs                 ← NEW  (/hubs/reader)

  frontend/
    src/
      views/
        ReaderView.vue             ← NEW
      components/reader/           ← NEW folder
        ReaderToolbar.vue
        ReaderTabs.vue
        ReaderSidebar.vue
        ReaderToc.vue
        ReaderRecent.vue
        ReaderPreview.vue
        OpenFileDialog.vue
        DropOverlay.vue
      stores/
        readerStore.ts             ← NEW
      services/
        readerApi.ts               ← NEW
        markdownRenderer.ts        ← NEW
        mermaidRenderer.ts         ← NEW
      router/
        index.ts                   ← UPDATE (přidat /reader route)
      App.vue / Navigation         ← UPDATE (nová nav položka)
```

### 3.2 Backend API

Nový controller `ReaderController.cs`.

| Metoda | Cesta | Vstup | Výstup |
|---|---|---|---|
| `GET` | `/api/reader/content?path=<abs>` | query `path` | `{ path, content, mtime }` nebo 400/404/500 |
| `GET` | `/api/reader/raw?path=<abs>` | query `path` | binární stream s `Content-Type` dle přípony |
| `POST` | `/api/reader/watch` | body `{ path }` | `200 OK` nebo chyba |
| `POST` | `/api/reader/unwatch` | body `{ path }` | `200 OK` |

**Whitelist přípon:**

- `/content`: `.md`, `.markdown`, `.mdx`, `.txt`
- `/raw`: `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.svg`

**Chybové odpovědi:**

- `400 { error: "Unsupported extension", allowed: [...] }` — nepovolená přípona.
- `400 { error: "Invalid path" }` — prázdná nebo nedostupná cesta.
- `404 { error: "File not found", path }` — soubor neexistuje.
- `500 { error: <msg> }` — I/O chyba.

**Bezpečnost:** Všechny cesty normalizovány přes `Path.GetFullPath`. Backend běží lokálně, whitelist přípon chrání před nechtěným servírováním citlivých souborů přes `/raw`.

### 3.3 SignalR hub

Nový hub `/hubs/reader`:

- Server → client event `FileChanged(path, newMtime)` — triggered z `FileSystemWatcher.Changed`.
- Server → client event `WatchFailed(path)` — když watcher 3× po sobě selže, frontend zobrazí toast.

### 3.4 Frontend komponenty

**`ReaderView.vue`** — top-level; obsahuje `ReaderToolbar`, `ReaderTabs`, `ReaderSidebar`, `ReaderPreview`, `DropOverlay`. Řídí lifecycle (SignalR connect na mount, disconnect na unmount).

**`ReaderToolbar.vue`** — tlačítka `Open file` (otevře `OpenFileDialog`) a `Export PDF` (`window.print()`).

**`ReaderTabs.vue`** — renderuje taby z `readerStore.tabs`; emit `activate(id)`, `close(id)`.

**`ReaderSidebar.vue`** — resizable (persist. šířka v `localStorage`, stejný pattern jako sidebar width commit `a89844f`). Obsahuje `ReaderToc` a `ReaderRecent`.

**`ReaderToc.vue`** — výpis `headings` aktivního tabu; scroll-spy highlight dle scroll pozice `ReaderPreview`.

**`ReaderRecent.vue`** — collapsible, max 20 položek, klik = `readerStore.openFromRecent(path)`.

**`ReaderPreview.vue`** — renderuje aktivní tab přes `markdownRenderer`, po mountu spouští `mermaidRenderer.renderAll()` na `.mermaid` blocích. Obrázky v `<img>` prefixuje na `/api/reader/raw?path=<resolvedAbsPath>` (full mode) nebo placeholder (lite mode).

**`OpenFileDialog.vue`** — modal s textovým polem pro absolutní cestu. Tlačítka: `Browse` (otevře `<input type="file">` pro výběr — z něj extrahuje alespoň název; user musí doplnit adresář), `Paste from clipboard` (vloží obsah schránky), `Open`. Submit validuje, že cesta není prázdná.

**`DropOverlay.vue`** — fixed full-screen overlay, aktivuje se na `dragenter` do view, deaktivuje na `dragleave`/`drop`. Po dropu emit `file-dropped(File)`.

### 3.5 Pinia store (`readerStore.ts`)

```ts
interface ReaderTab {
  id: string                     // uuid
  path: string | null            // abs. cesta, null v lite mode
  displayName: string            // filename
  content: string                // raw Markdown
  mtime: number | null
  mode: 'full' | 'lite'
  headings: Array<{ level: number; text: string; id: string; line: number }>
  scrollY: number
}

interface ReaderState {
  tabs: ReaderTab[]
  activeTabId: string | null
  recentFiles: Array<{ path: string; displayName: string; openedAt: number }>
  sidebarWidth: number
}
```

**Akce:**

- `openFromPath(path)` — full mode; volá `readerApi.getContent`, existující tab se stejnou `path` se jen aktivuje (dedupe), jinak `addTab`. Volá `readerApi.watch`.
- `openFromFile(file: File)` — lite mode; `FileReader.readAsText`, `addTab` s `path: null`, `mode: 'lite'`.
- `closeTab(id)` — odstraní tab; pokud byl aktivní, aktivuje sousední. Volá `readerApi.unwatch` pokud `mode === 'full'`.
- `activateTab(id)` — set `activeTabId`.
- `handleFileChanged(path, mtime)` — najde tab, refetchne content, zachová `scrollY`.
- `addRecent(path, displayName)` — dedupe, max 20, sort podle `openedAt` desc.

**Persistence do `localStorage`:** `tabs` (bez `content` — znovu načteme z disku při boot), `recentFiles`, `sidebarWidth`. Klíč `claude-orchestrator-reader-state` (v1). Při deserializaci tolerujeme chybějící pole (default values).

### 3.6 Rendering pipeline

**`markdownRenderer.ts`** — wrapper nad `markdown-it`:

- Pluginy: `markdown-it-anchor` (id na nadpisech), vlastní heading extractor (do `headings` array).
- `highlight` callback: `highlight.js` pro known jazyky, fallback na escape-only.
- Mermaid bloky (` ```mermaid `) → `<div class="mermaid">...</div>` (místo `<pre><code>`).
- Obrázky: vlastní renderer pravidlo — v full mode přepíše `src` na `/api/reader/raw?path=<resolve(baseDir, src)>`, v lite mode na data-URL placeholder.

**`mermaidRenderer.ts`** — lazy `import('mermaid')` při prvním použití; init s `{ startOnLoad: false, theme: <app theme>, securityLevel: 'strict' }`. `renderAll(container)` najde `.mermaid` bloky a zavolá `mermaid.run({ nodes })`. Chyba = červeně zabarvený code block s error message.

### 3.7 Live reload

1. `openFromPath` volá `POST /api/reader/watch { path }`.
2. Backend `FileWatcherService` vytvoří (nebo reuse) `FileSystemWatcher` pro adresář; filter na filename; event `Changed` → debounce 100 ms → SignalR `FileChanged(path, mtime)`.
3. Frontend `ReaderView` má SignalR subscriber; na event volá `readerStore.handleFileChanged`.
4. `closeTab` (full mode) volá `POST /api/reader/unwatch`; backend dereferencuje watcher a pokud nikdo jiný nesleduje adresář, dispose.

### 3.8 Export PDF

- Tlačítko `Export PDF` volá `window.print()`.
- Dedikovaný `@media print` CSS:
  - skrýt navigaci, toolbar, tab bar, sidebar, drop overlay;
  - pouze `ReaderPreview` viditelný; padding/font upravený pro tisk;
  - Mermaid SVG a `<img>` se tisknou beze změny;
  - page-break-inside: avoid pro code bloky a obrázky.
- Uživatel v nativním print dialogu zvolí "Save as PDF".

## 4. Data flow

### 4.1 Otevření přes dialog (full mode)

```
User klik Open → OpenFileDialog → user zadá cestu → submit
  → readerStore.openFromPath(path)
  → readerApi.getContent(path) → { content, mtime }
  → readerStore tab append + setActive
  → readerApi.watch(path)
  → ReaderPreview render: markdownRenderer → DOM
    → po mountu: mermaidRenderer.renderAll()
    → obrázky servírované přes /api/reader/raw
  → ReaderToc se naplní z headings
  → recentFiles update
```

### 4.2 Drag-drop (lite mode)

```
File dropped do DropOverlay → emit file-dropped(File)
  → readerStore.openFromFile(file)
  → FileReader.readAsText(file)
  → tab append s mode='lite', path=null
  → render pipeline beze změny, obrázky mají placeholder
  → NO watch, NO recent (není cesta)
```

### 4.3 Live reload

```
External editor uloží soubor
  → backend FileSystemWatcher.Changed (debounced)
  → SignalR FileChanged(path, mtime)
  → readerStore.handleFileChanged(path, mtime)
  → readerApi.getContent → obnoví tab.content
  → ReaderPreview re-render; ReaderToc aktualizovat; scrollY zachována
```

## 5. Chyby a edge cases

| Situace | Chování |
|---|---|
| Neexistující cesta | Toast "File not found", tab se neotevře |
| Nepodporovaná přípona | Toast "Only .md/.markdown/.mdx/.txt supported" |
| Permission denied při čtení | Toast s chybou z backendu |
| Soubor > 5 MB | Konfirm dialog "File is large (X MB), render anyway?" |
| Mermaid parse error | Blok vykreslen jako code block s červeným borderem a error msg (Mermaid oficiální pattern) |
| Drag-drop non-MD souboru | Toast "Only .md/.markdown/.txt supported" (přípona se čte z `File.name`) |
| Recent file neexistuje | 404 při otevření → odstranit z `recentFiles` |
| Watcher 3× selže | SignalR `WatchFailed` → toast "Live reload stopped for X" |
| Otevření stejného souboru 2× | `openFromPath` najde existující tab se stejnou `path` a jen `activateTab` |
| Obrázek v lite mode | `<img>` s placeholderem + tooltip "Open file via path dialog to load images" |
| Zavření posledního tabu | `activeTabId = null`; preview zobrazí empty state s velkým CTA |
| Reload stránky | Z `localStorage` obnovíme taby (bez contentu), pro každý `full` tab refetch `getContent`; lite taby po reloadu nejdou obnovit → varianta: při persistenci lite taby ignorujeme |

## 6. Testování

### 6.1 Testovací stack

- **Vitest** (první class Vite integrace, Jest-compatible API)
- **@vue/test-utils** (mount komponent)
- **jsdom** (DOM environment, `FileReader`, `localStorage`)
- **@testing-library/vue** (sémantické queries, čitelnější testy)

**Skripty v `package.json`:**

```json
"test": "vitest run",
"test:watch": "vitest",
"test:ui": "vitest --ui"
```

**Struktura:** co-located `*.test.ts` vedle zdrojáků.

### 6.2 Unit testy — priorita 1 (čisté funkce / stores)

- **`markdownRenderer`**
  - Render MD na HTML, sémantické zachování nadpisů
  - Extrakce `headings` (level, text, id, line)
  - Mermaid bloky transformované na `<div class="mermaid">`
  - Escape HTML v content, XSS sanity
  - Obrázky: v full mode prefixnuté na `/api/reader/raw?path=...`; v lite mode placeholder
- **`readerStore`**
  - `addTab`: dedupe podle `path` (stejný soubor = activate existující tab)
  - `closeTab`: aktivace sousedního při zavření aktivního; null pokud poslední
  - `addRecent`: limit 20, dedupe, ordering by `openedAt` desc
  - `handleFileChanged`: update content, zachová `scrollY`
  - Persistence: serialize + deserialize round-trip; tolerance k chybějícím/korupčním polím
- **`readerApi`**
  - Request URL/body stavba
  - Parsování error response (`400`, `404`, `500`)
  - Mock `fetch` a ověřit volání

### 6.3 Unit testy — priorita 2 (komponenty)

- **`OpenFileDialog`**: tlačítko Open je disabled při prázdné cestě; submit emit s trimnutou cestou
- **`ReaderToc`**: click na heading emit `scroll-to(id)`; scroll-spy vybírá správný nadpis podle simulovaného `scrollY`
- **`DropOverlay`**: aktivace na `dragenter`, deaktivace na `dragleave`/`drop`; emit `file-dropped(File)` s dropnutým souborem
- **`ReaderTabs`**: render tabů ze store; click emit `activate`; křížek emit `close`

### 6.4 Integration (volitelné, první verze může vynechat)

- `ReaderView` full mount s mock `readerApi`: otevření souboru → tab → TOC → přepnutí tabů

### 6.5 Cíl pokrytí první verze

Priorita 1 + 2 = cca 30–40 testů.

### 6.6 Manuální akceptační checklist

Automatizované testy nepokryjí vizuální chování (Mermaid skutečný render, print CSS, drag-drop v reálném prohlížeči). Manuální ověření po implementaci:

1. Otevřít MD s nadpisy → TOC se vygeneruje a scroll-spy označuje aktivní nadpis.
2. MD s Mermaid code blockem → vykreslí se diagram.
3. MD s `![](./img.png)` (full mode, obrázek existuje) → zobrazí se.
4. Drag-drop MD → otevře se v lite mode, obrázky mají placeholder.
5. Editovat soubor externě → auto-reload do 1 s, scroll pozice zachována.
6. Otevřít 3 taby, reload stránky → taby se obnoví (full), content re-fetched z disku.
7. Otevřít stejný soubor 2× → přepne na existující tab.
8. Export PDF → print dialog, náhled obsahuje jen preview bez nav.
9. Recent files → po otevření 21 souborů se nejstarší vyhazuje.
10. Resizable sidebar → šířka persistuje přes reload.

## 7. Závislosti (npm)

Nové:

- `markdown-it`
- `markdown-it-anchor`
- `highlight.js`
- `mermaid`
- `uuid` (generování tab ID)

Dev:

- `vitest`
- `@vue/test-utils`
- `@testing-library/vue`
- `jsdom`
- `@types/markdown-it`, `@types/markdown-it-anchor`

## 8. Otevřené body / zvážit později

- Fulltext search v otevřeném dokumentu (z původního brainstormingu — vynecháno v této iteraci).
- Theme toggle (první verze automaticky převezme vzhled aplikace).
- Export HTML (stand-alone soubor s embedded CSS) — zvážit, pokud PDF nebude stačit.
- Backend unit testy pro `ReaderController` — samostatná iterace.
- Obnova lite tabů po reloadu (pravděpodobně nikdy — File API handles nejsou persistovatelné).
