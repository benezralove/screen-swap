# ScreenSwap

Swap every window between your two monitors with one hotkey. Maximised stays
maximised, minimised stays minimised, normal windows keep their layout.

**Hotkey: `Ctrl + Alt + X`**

## Running it

Double-click `ScreenSwap.exe`. It sits in the system tray and does nothing until
you press the hotkey.

**Autostart runs from a scheduled task, not the Run key.** The Run key approach
was tried first and silently failed to fire at logon, so it was replaced with a
Task Scheduler entry named `ScreenSwap`:

- Trigger: at logon, for this user, with a 15-second delay so the taskbar tray
  is ready before the icon is added
- Repeats every 5 minutes, indefinitely
- Multiple instances: **ignore new**
- Execution time limit: none (the default 3-day limit would kill a tray app)

The repetition plus `IgnoreNew` is what makes it self-healing. While the app is
alive the task counts as Running, so each 5-minute tick is a no-op and the
running process is left completely alone — same process id, no restart. If the
app ever dies, the task is no longer Running, so the next tick relaunches it.
Worst case you are without it for five minutes.

Manage it:

```
schtasks /query /tn ScreenSwap /v /fo list     :: check it
schtasks /run   /tn ScreenSwap                 :: test it without rebooting
schtasks /change /tn ScreenSwap /disable       :: turn autostart off
schtasks /change /tn ScreenSwap /enable        :: turn it back on
```

The **Start with Windows** tick in the tray menu still writes the old Run key
value. Leave it unticked — the scheduled task is doing the job, and having both
would launch two copies.

The task stores the exe's current path, so **if you move this folder, re-register
the task** or autostart will silently break.

### Running it elevated

Windows will not let a normal-privilege program move a window owned by an
elevated one. To lift that, re-register the task with highest privileges from an
**administrator** PowerShell:

```powershell
$exe = "C:\Users\Jaz\Projects\screen-swap\ScreenSwap.exe"
$t = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$t.Delay = "PT15S"
Register-ScheduledTask -TaskName ScreenSwap -Force `
  -Action (New-ScheduledTaskAction -Execute $exe) -Trigger $t `
  -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew) `
  -Principal (New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest)
```

A scheduled task with highest privileges starts elevated at logon **without** a
UAC prompt, which is why this is better than ticking "run as administrator" on
the exe.

## Log

`%LOCALAPPDATA%\ScreenSwap\screenswap.log` records every start, whether the
hotkey registered, every swap and its counts, clean exits, session end at
shutdown, and any crash. Tray menu → **Open log file**.

Reading it when the icon is missing:

- **A `start` line ~15-20 seconds after you logged in** → autostart worked. If the
  icon is gone now, it died later; look for what comes after that line.
- **`application exit (clean)`** → something asked it to quit.
- **`session ending`** → Windows closed it at shutdown or logoff. Normal.
- **`FATAL` or `UI EXCEPTION`** → it crashed, and the stack trace is right there.
- **Nothing after the last `swap` line** → it was terminated from outside, by
  Task Manager or a cleanup/security tool. Windows leaves no crash record for a
  kill, so silence is itself the diagnosis.

To check whether the machine actually restarted, do not trust uptime — Fast
Startup keeps the old boot time across normal shutdowns. Use these instead:

```powershell
# real boots
Get-WinEvent -FilterHashtable @{LogName='System'; Id=27} -MaxEvents 5 | Select TimeCreated
# last logon (explorer restarts at every logon)
Get-Process explorer | Select StartTime
```

Tray menu:

- **Swap screens now** — same as the hotkey
- **Swap and show report** — swaps, then opens a text report of every window it moved
- **Tray icon** — 16 designs to choose from, each previewed in the menu at real size
- **Icon colour** — Auto (matches your taskbar), Always white, Always black
- **Show all icons side by side** — opens a sheet of all 16 on both dark and light backgrounds
- **Start with Windows** — tick to launch automatically at login
- **Exit** — quits

Double-clicking the tray icon also swaps.

## Tray icon options

Currently set to **2 — Monitors with stands**.

Pick from the **Tray icon** submenu — each entry shows the actual glyph next to
its name, so you can see what you are choosing. The choice applies instantly and
is remembered.

| # | Name | # | Name |
|---|------|---|------|
| 0 | Two screens + arrows | 8  | Numbered screens |
| 1 | Circular swap        | 9  | Diagonal split square |
| 2 | Monitors with stands | 10 | Vertical swap |
| 3 | Double arrow         | 11 | Cycle loop |
| 4 | Split panels         | 12 | Split rectangle |
| 5 | Diagonal swap arrows | 13 | Shuffle paths |
| 6 | A / B boxes          | 14 | Bold X |
| 7 | Facing chevrons      | 15 | Arrows in boxes |

`icon-options.png` in this folder shows all 16 at 48px, 24px and 16px on both a
dark and a light taskbar. Regenerate it any time with `ScreenSwap.exe /icons`.

**Icon colour** defaults to Auto, which draws a white glyph on a dark taskbar and
a dark glyph on a light one, and redraws itself if you switch Windows between
light and dark mode. Your taskbar is currently dark, so the glyph is white.

Your choices are stored in the registry under `HKCU\Software\ScreenSwap`
(`IconStyle` and `IconColour`). Delete that key to go back to defaults.

## Rebuilding

```
build.cmd
```

No SDK or downloads required. It uses the C# compiler already built into
Windows (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).

The build prints one warning, `CS0162: Unreachable code detected`. That is
expected — `SCALE_TO_FIT` is a compile-time constant, so the compiler can see
one branch of it is dead.

## Changing the hotkey

Edit the top of `ScreenSwap.cs`:

```csharp
public const uint HOTKEY_MODS = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
public const uint HOTKEY_VK   = 0x58;            // 'X'
public const string HOTKEY_TEXT = "Ctrl + Alt + X";
```

`HOTKEY_VK` is a virtual-key code — `0x41`–`0x5A` are `A`–`Z`, `0x70`–`0x7B` are
`F1`–`F12`. Combine `MOD_CONTROL`, `MOD_ALT`, `MOD_SHIFT`, `MOD_WIN`. Then run
`build.cmd` again.

If the hotkey is already taken by another app, ScreenSwap shows a tray warning
on startup and you can still use the tray menu.

## Behaviour options

Also at the top of `ScreenSwap.cs`:

- `SCALE_TO_FIT = true` — a window keeps the same *proportion* of the screen.
  A half-width window stays half-width. Recommended, because your monitors are
  different sizes (2560x1600 and 3840x2160), so pixel sizes do not carry over.
- `SCALE_TO_FIT = false` — a window keeps its exact pixel size and is only moved
  across, clamped so it stays on screen.
- `RESTORE_FOREGROUND = true` — puts focus back where it was after a swap.

## Command line

| Flag       | What it does                                                         |
|------------|----------------------------------------------------------------------|
| *(none)*   | Runs in the tray with the hotkey active                              |
| `/once`    | Swaps once and exits — useful for binding to a shortcut or scheduler |
| `/report`  | Swaps once, then opens a report in Notepad                           |
| `/dryrun`  | Changes nothing. Writes `%TEMP%\screenswap-report.txt` listing what it *would* move |
| `/icons`   | Rebuilds `icon-options.png` and opens it                              |
| `/iconsheet` | Same, but writes the file without opening it                       |

`/dryrun` is the one to reach for if something misbehaves — it shows every
window it can see, which monitor it thinks the window is on, and where it would
land.

## Known limits

- **Windows running as administrator will not move.** Windows blocks a normal
  program from moving a window owned by an elevated one (UIPI). ScreenSwap
  counts these and warns you. To include them, right-click `ScreenSwap.exe` and
  **Run as administrator**. For that to happen automatically at login, use Task
  Scheduler with "Run with highest privileges" instead of the Start with Windows
  tick, otherwise you get a UAC prompt every boot.
- **More than two monitors**: it swaps the leftmost and rightmost, and leaves
  windows on any monitor in between alone.
- **Z-order** can shuffle slightly. Focus is restored, stacking order is not
  guaranteed.
- Windows that are hidden, tool windows, tooltips and dropdowns are deliberately
  skipped, as are the cloaked ghost windows that Store apps leave behind.

## Why not just swap the monitors in Display Settings?

If you drag the two displays past each other in Display Settings, windows keep
their coordinates and so appear to swap screens — no window-moving needed. That
trick only works cleanly when both monitors are the same resolution. Yours are
2560x1600 and 3840x2160, so the virtual desktop would change shape and Windows
would start rescuing off-screen windows. Moving the windows is the correct
approach for this setup.
