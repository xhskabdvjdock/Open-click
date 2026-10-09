# Open Click — Manual test record

Environment: Windows 10.0.26200 x64, .NET SDK 10.0.401, self-contained `win-x64` publish.

## Automated (all passing, `dotnet test OpenClick.slnx -c Release`)

27/27 MSTest: filter timing (outside/inside window, per-button independence,
global window, left-only, zero-interval, invalid intervals, drag-hold protection,
disable-mid-hold, triple-click, reset, simultaneous buttons, counters),
config/profile CRUD + import validation + export round-trip, click-counter and
double-click analyzer stats, bypass pass-through, hook Start/Stop lifecycle,
message mapping (L/R/M/X1/X2, move/wheel/unknown pass-through), and an end-to-end
hook-callback test (allow/suppress return codes + queued observations).

## Live app checks (this machine, verified)

- [x] Published exe launches; main window appears (`MainWindowTitle=Open Click`,
      non-zero handle); no errors in `%LOCALAPPDATA%\OpenClick\logs\openclick.log`.
- [x] First run creates `%LOCALAPPDATA%\OpenClick\settings.json`
      (filter `Enabled=false`, dark theme) and 6 built-in profiles.
- [x] `WH_MOUSE_LL` installs and uninstalls cleanly in-process (test
      `StartStop_Lifecycle_IsClean` runs against the real OS hook API).
- [x] Real rapid-click suppression path verified through the actual
      `HookCallback` (return `(IntPtr)1` + paired UP consumed). OS event
      delivery itself (`SetWindowsHookEx` dispatch) is the standard Windows
      mechanism, not re-proven here.

## NOT performed here (requires a physical mouse + interactive session)

- [ ] Intentional double-click feel at various intervals.
- [ ] Drag-and-drop in Explorer, context menus, held-button steering.
- [ ] Simultaneous L+R, focus change mid-press, toggle-while-held by hand.
- [ ] Arabic RTL visual review, light theme visual review, DPI scaling review.
- [ ] Elevated-window / game / RDP behavior (expected UIPI limits).
- [ ] Installer build/run (script provided, Inno Setup not executed here).

Suggested checklist for a human pass: enable Standard Protection (100 ms),
single/double/triple click in Notepad, drag a file in Explorer, hold L and move,
open/close context menu, scroll a page, switch to Arabic + light theme,
minimize-to-tray → toggle from tray → exit; confirm input is normal throughout
and after exit.
