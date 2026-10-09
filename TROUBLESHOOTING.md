# Open Click — Troubleshooting

## Filter won't enable / "Could not install the mouse hook"

- Another utility already hooked input, or you're in an elevated/remote/secured session (UIPI). The app leaves input unfiltered and shows the Win32 error. Try: close other mouse tools, run the session normally (not elevated remote), restart the app.
- Logs: `%LOCALAPPDATA%\OpenClick\logs\openclick.log` → Settings → Export diagnostic logs.

## Clicks still get through in a game / elevated window

Expected limitation: Windows UIPI blocks low-level hooks from affecting elevated/protected processes. No workaround is offered (would require unsafe/invasive techniques). Use the tester to confirm the hook works in normal windows.

## Drag-and-drop or holds feel stuck

The engine never suppresses holds (DOWN while logically held passes through) and UPs pair with suppressed DOWNs. If you hit an edge case: press the toggle hotkey (`Ctrl+Alt+M`), header toggle, or tray → Disable Filter. Report steps + export logs.

## Double-clicks blocked

Your interval is longer than your natural double-click speed. Lower it (try 50 ms) or use Left-Only/Selected-Buttons mode. The UI warns that long intervals suppress intentional fast clicks.

## Tester shows nothing

Press **Start listening** (tester runs the hook in observe-only mode; the filter need not be enabled). If hook install fails, the same error as above applies.

## Hotkey doesn't work

It's likely taken by another app — you'll see a warning. Change it in Settings (e.g. `Ctrl+Alt+K`); it applies to the main window session.

## Settings corrupted

The app backs up the bad file (`settings.json.corrupt-*.bak`) and loads defaults. Reconfigure, or restore manually from the backup.

## Clean uninstall

Uninstall via the installer/Programs; then optionally delete `%LOCALAPPDATA%\OpenClick` to remove settings, profiles, and logs.
