# Open Click — Architecture

Selected stack: **C# / .NET 10 / WPF** (not WinUI 3: WPF needs no extra workload, supports self-contained `win-x64` publish, WinForms `NotifyIcon` tray, and `RegisterHotKey` interop directly).

## Projects

- `src/OpenClick.Core` (`net10.0`, no OS calls): `ClickFilterEngine`, `FilterConfig`/`FilterMode`, monotonic clocks, `ClickCounter`, `DoubleClickAnalyzer`, `TestSessionSummary`, `ProfileManager`, `SettingsService`, `SimpleLogger`. Fully unit-testable with `ManualClock`.
- `src/OpenClick.App` (`net10.0-windows`, WPF + WinForms tray): `Input/MouseHookService` (`WH_MOUSE_LL`), `Input/GlobalHotkeyService`, `Services/AppState` (singleton: engine + hook + settings + profiles + tester aggregates + 150 ms UI poll pump), `Services/LocalizationService`/`ThemeService`/`TrayService`/`StartupService`, `Views/*` (Dashboard/Filter/Tester/Profiles/Settings), `MainWindow` shell.
- `tests/OpenClick.Tests` (`net10.0`, MSTest): 23 tests over engine/config/profiles/tester/bypass.

## Data flow

1. `WH_MOUSE_LL` callback (hook thread): map `wParam` to button/down/up; wheel/move pass through untouched. If temporary bypass or foreground-process bypass → `engine.RecordPassThrough` (no timing effect) → allow. Else `engine.ProcessEvent` (monotonic ms) → suppress returns `(IntPtr)1`, else `CallNextHookEx`. Enqueue bounded `HookObservation` (cap 2000, drop overflow). **No I/O, no UI, no allocation-heavy work in callback; try/catch with fail-safe auto-disable after 5 consecutive errors.**
2. UI dispatcher timer (150 ms) drains ≤256 observations per tick → updates `ClickCounter`/`DoubleClickAnalyzer`/scroll totals/bounded `EventHistory` (limit from settings, default 300) → single `StateChanged` → views refresh text only (no full redraw).
3. Filter toggle: `AppState.SetFilterEnabled` starts hook (fail → engine stays disabled + honest error) or stops hook immediately (input back to normal). Tester "Start listening" starts hook in observe-only mode (`Engine.Enabled=false`), so the tester never requires filtering.

## Down/up pairing (core safety rule)

- Only DOWNs are timing-filtered. Each suppressed DOWN increments `SuppressedDowns`; the next UP for that button is consumed (suppressed) so apps never see unmatched releases.
- DOWN while `LogicalDown` (held/drag) is always allowed without moving the timing window — favors drag-and-drop over aggressive filtering.
- Disable-during-hold: pending suppressed UPs still consumed; accepted holds release normally. `ResetTiming` (profile/mode/interval change) clears windows but preserves hold state; `ResetAll` clears everything.

## Persistence

`SettingsService` (`%LOCALAPPDATA%\OpenClick\settings.json`, schema v1): load → migrate → validate → on failure back up to `*.corrupt-*.bak` + defaults; save via `.tmp` + move. `ProfileManager` (`profiles\*.json`): skips invalid files, seeds 6 built-ins when empty; import path treats JSON as untrusted (validate, re-id on collision).

## Hotkeys / tray / startup

`RegisterHotKey` (toggle default `Ctrl+Alt+M`, optional bypass) handled via `HwndSource` hook; registration failure shows a warning, never enables filtering silently. Tray (`NotifyIcon`) rebuilt on every state change so status can't lie; exit removes icon + unhooks + unregisters. Startup via `HKCU\...\Run` only after explicit checkbox consent. Default filter state: **disabled**; opt-in "start enabled" persisted explicitly.

## What was deliberately NOT built

- No per-process injection, no memory editing, no driver, no keyboard interception, no network calls. Optional process bypass is read-only foreground-window name matching (cached 500 ms), best-effort and documented.
