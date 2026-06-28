<div align="center">

# 🏝 Codex Open Island

**Windows Desktop Codex Dynamic Island · Traffic-Light Status + Liquid-Glass Quota Sphere**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4.svg)]()

English · [简体中文](README.zh-CN.md)

<br/>

</div>

---

**Codex Open Island** is a lightweight Windows desktop widget that lives in your system tray, purpose-built for **Codex** users. It consolidates your Codex project status, remaining quota, and session list into a sleek "Dynamic Island" floating at the top of your screen.

- 🔴🟡🟢 **Traffic-Light Status Bar** — Real-time signal lights for Codex project state: working (green slow-blink), thinking (green fast-blink), needs approval (red fast-blink), completed (green steady + island bounce). Know what's happening without switching windows
- 🌐 **Liquid-Glass Quota Sphere** — Glass-textured circular water-fill indicator with green / yellow / red color mapping, showing Codex remaining quota percentage plus 5h and 7d window details
- 💻 **System Stats Bar** — Real-time CPU / RAM / GPU / network monitoring
- 🔀 **Model Switching** — Toggle between Codex default and DeepSeek V4 with a single click; quota sphere adapts automatically
- 📂 **Project List** — Auto-discovers recent projects from local Codex session logs; click a card to open it in the Codex desktop app
- 🎯 **Completion & Approval Bounce** — When Codex completes a task or needs your approval, the island pops to the front and bounces continuously until you click it
- 🔆 **Smart Fade** — Auto-fades to semi-transparent after 12 seconds of no mouse interaction, staying out of your way

Non-intrusive — stays collapsed at the top of your screen whether you're gaming, coding, or reading.

---

## 🗺 Features

| Area | Capabilities |
|---|---|
| **🔴🟡🟢 Traffic Lights** | 10 Codex project states · Red/Yellow/Green · Fast/Slow/Steady blink · Persistent bounce alerts |
| **🌐 Quota Sphere** | Liquid-glass water fill · Green(≥10%)/Yellow(<10%)/Red(0%) · 5h + 7d windows · Auto-refresh |
| **💻 System Monitor** | CPU / RAM / GPU / Network real-time |
| **🔀 Model Switch** | Codex / DeepSeek V4 dropdown · Persistent selection · Quota sphere linked |
| **📂 Projects** | SQLite + JSONL auto-discovery · Click to open in Codex Desktop · Resume threads |
| **🏝 Island UX** | Expand/Collapse · Drag to move · Double-click toggle · Hover restore · Auto-fade |
| **📌 Tray** | Show/Hide · Always on top · Exit |

## 🚥 Traffic Light States

| Signal | Light | Blink | Codex Events |
|------|:--:|------|-----------|
| **Ready** | 🟢 | Steady | `sessionstart` `idle` |
| **Thinking** | 🟢 | ⚡ Fast (320ms) | `reasoning` `task_started` |
| **Working** | 🟢 | 🐢 Slow (900ms) | `function_call` `pre_tool_use` |
| **ToolDone** | 🟢 | 🐢 Slow (900ms) | `function_call_output` `post_tool_use` |
| **Permission** | 🔴 | ⚡ Fast + 🏝Bounce | `approval_requested` `permission_request` |
| **Attention** | 🟡 | 🐢 Slow (900ms) | `notification` `needs_review` |
| **Blocked** | 🔴 | ⚡ Fast (320ms) | `failure` `error` `turn_aborted` |
| **Completed** | 🟢 | Steady + 🏝Bounce | `task_complete` `final_answer` |
| **Stale** | 🟡 | 🐢 Slow (900ms) | Data older than 10 min |
| **Paused** | ⚫ | Off | `pause` `paused` |

> 💡 **Completion / Permission**: the island pops to front and bounces continuously. Click anywhere on the island to dismiss.

## 🌐 Quota Colors

| State | Color | Condition |
|------|:--:|------|
| **Healthy** | 🟢 `#32D74B` | remaining ≥ 10% |
| **Low** | 🟡 `#FFD60A` | 0 < remaining < 10% |
| **Empty** | 🔴 `#FF453A` | remaining = 0% |
| **Loading** | ⚫ Gray | Initial read |
| **Error** | 🔴 Red | Read failure (timeout/not logged in) |
| **Stale** | ⚫ Gray | Data outside freshness window |
| **Unknown** | ⚫ Gray | Third-party model (no quota data) |

---

## 📦 Installation

> **Self-contained** — .NET 8 runtime bundled. No dependencies. Windows 10/11 x64. Double-click to run.

Download from [Releases](../../releases):

- 📦 `CodexOpenIsland-vX.Y.Z-win-x64.zip` — Extract and run `CodexOpenIsland.exe`

> ⚠️ Unsigned executable. Windows SmartScreen will block it. Click **More info → Run anyway**.

## 🏃 Quick Start

1. Download and extract `CodexOpenIsland-vX.Y.Z-win-x64.zip`
2. Double-click `CodexOpenIsland.exe`
3. The island appears at the top of your screen; a tray icon appears
4. Run Codex in your terminal: `codex`
5. The island auto-detects project status and displays it

Tray right-click menu: **Show Codex Island** / **Hide** / **Always on top** / **Exit**

## 🛠 Build from Source

Requires Windows + .NET 8 SDK.

```powershell
git clone https://github.com/mooyu-king/codex-open-island.git
cd codex-open-island
dotnet build CodexIsland.sln -c Release
```

Publish a self-contained exe (no .NET runtime required):

```powershell
dotnet publish src/CodexIsland.App/CodexIsland.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/publish
```

Output: `artifacts/publish/CodexIsland.App.exe` (~155 MB, ready to distribute)

## 🏗 Architecture

```
┌─────────────────────────────────────────────────┐
│                 CodexIsland.App                  │
│  MainWindow.xaml  ·  Controls  ·  ViewModels    │
│  (WPF MVVM · SignalLightBar · QuotaSphere)      │
├─────────────────────────────────────────────────┤
│                CodexIsland.Core                  │
│  Models  ·  Quota  ·  Signals                    │
│  (QuotaHealthMapper · ProjectSignalMapper)      │
├─────────────────────────────────────────────────┤
│               CodexIsland.Hooks                 │
│  (Codex hook → writes status.json)              │
└─────────────────────────────────────────────────┘
```

| Project | Responsibility |
|---|---|
| **CodexIsland.App** | WPF UI: island window, signal light bar, quota sphere, system stats, MVVM |
| **CodexIsland.Core** | Business logic: Codex quota (JSON-RPC stdio), signal mapping, session log parsing, SQLite queries |
| **CodexIsland.Hooks** | CLI hook: runs as Codex hook subprocess, writes events to `%LOCALAPPDATA%\CodexIsland\status.json` |

Data flow:

```
Codex CLI (stdio JSON-RPC)
   │ account/rateLimits/read
   ▼
CodexQuotaService ──► QuotaHealthMapper ──► QuotaSphere UI

~/.codex/sessions/*.jsonl + state_*.sqlite
   │ Python SQLite query + JSONL parsing
   ▼
LocalProjectSignalService ──► ProjectSignalMapper ──► SignalLightBar UI
```

## 🙏 Acknowledgements

This project draws heavy inspiration and design from three outstanding open-source projects:

- **[codex-led-widget](https://github.com/xicunwus2025-sys/codex-led-widget/)** — Codex quota JSON-RPC stdio protocol, liquid-glass quota sphere visual design, red/yellow/green quota color mapping. Thanks for the complete local Codex quota reading solution
- **[Agent-Signal-Bar-Windows](https://github.com/ridyang/Agent-Signal-Bar-Windows/)** — AI Agent traffic-light signal taxonomy, signal aggregation priorities, Windows tray integration approach. Thanks for the elegant agent status language
- **[open-island-windows](https://github.com/ludiwangfpga/open-island-windows/)** — WPF transparent floating window dynamic island framework, expand/collapse interaction model, system stats monitoring, Windows packaging practices. Thanks for the complete Windows dynamic island reference implementation

## 📄 License

[MIT](LICENSE) © 2025 mooyu-king
