# Open Click

Lightweight Windows mouse utility: a configurable **click filter** (suppresses repeated button-down events inside a time window) and an interactive **mouse tester** (buttons, timing, double-click analysis, scroll, diagnostics).

Native Windows desktop app. No Electron, no web server, no injection, no memory modification, no network telemetry.

> A software filter can suppress some repeated click events, but it cannot physically repair a defective mouse switch or guarantee every unintended click is identified correctly.

## Features

- **Click filter**: per-button debounce (default), global debounce (optional), left-only, selected buttons; presets 0/50/100/150/200/300/500 ms + custom 0–2000 ms; `WH_MOUSE_LL` hook, `Stopwatch` monotonic timing, coherent down/up pairing, drag-and-drop protection (held buttons pass through), scroll never filtered.
- **Fail-safe**: default OFF on first install; global hotkey toggle (default `Ctrl+Alt+M`), header toggle, tray menu, 10 s temporary bypass, auto-disable after repeated hook errors, hook released on disable/exit.
- **Tester** (works without the filter via observe-only hook mode): vector mouse diagram, bounded event monitor with suppressed flags, per-button completed-click counters, double-click interval stats + histogram, rapid-click diagnostic highlighting (informational only), scroll test, timed session summary with copy/CSV.
- **Profiles**: create/rename/duplicate/delete/switch/default/import/export JSON + 6 built-ins (Off, Gentle 50 ms, Standard 100 ms, Strict 200 ms, Left Only, Custom).
- **Settings**: JSON in `%LOCALAPPDATA%\OpenClick\` (`settings.json`, `profiles\`, `logs\`), versioned schema, safe temp-file writes, corrupt-file backup + defaults, untrusted-import validation.
- **UI**: WPF, dark default + light theme, card layout, Arabic (RTL, Thamaniya-first font chain) + English, tray icon with accurate status, DPI scaling via WPF defaults.

## Build

Requires .NET 10 SDK on Windows.

```powershell
dotnet restore
dotnet build OpenClick.slnx -c Release
dotnet test OpenClick.slnx -c Release
dotnet publish src/OpenClick.App/OpenClick.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

Run: `publish/win-x64/OpenClick.exe`.

## Installer

Inno Setup script: `installer/innosetup.iss`. Build with Inno Setup 6+; install/uninstall removes only app files (settings under `%LOCALAPPDATA%\OpenClick` are preserved; delete manually for a full reset).

## Storage

- `%LOCALAPPDATA%\OpenClick\settings.json`
- `%LOCALAPPDATA%\OpenClick\profiles\*.json`
- `%LOCALAPPDATA%\OpenClick\logs\openclick.log` (bounded ~1 MB tail)

## Safety & limits

- User-level APIs only; no admin required. Elevated/protected windows, some games, remote sessions may ignore the hook (UIPI) — reported honestly in-app.
- Longer intervals suppress intentional fast clicks too. No per-app guarantees.
- See `TROUBLESHOOTING.md` and `ARCHITECTURE.md`.
