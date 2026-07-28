# MuMiClick

<img src="src/MuMiClick/Assets/MuMiClick-logo.png" alt="MuMiClick logo" width="96">

**English** · [한국어](README.ko.md)

<img width="1030" height="1021" alt="2026-07-28 15 45 34" src="https://github.com/user-attachments/assets/d073cebc-3b1e-4517-b088-bd15e8926a0c" />

MuMiClick is a practical mouse and keyboard macro recorder for Windows 10/11 x64. It is designed for simple recording and reliable playback without scripting, with scan-code keyboard injection, timestamp-based scheduling, and independent emergency-stop handling.

All macro data stays on your computer. MuMiClick has no ads, accounts, login, telemetry, or network data transfer.

## Download

- [Latest portable Windows release](https://github.com/AppStudioLB/MuMiClick/releases/latest)
- Requirements: Windows 10 or Windows 11, x64
- The portable single-file build includes the .NET runtime.

## Quick start

1. Run `MuMiClick.exe`.
2. Press **Record** or `F8`, then perform the mouse and keyboard actions you want to capture.
3. Press `F8` again or click **Stop**.
4. Choose the repeat count, playback speed, and loop delay, then click **Play** or press `F9`.
5. Press `F11` to pause or resume, and `F7` for emergency stop.
6. Use **Save** to create a local `.mumacro` JSON file. The most recently loaded or saved macro is restored at the next launch.

Enable **Easy mode** in the upper-right corner to keep only Record, Stop, Play, repeat count, and infinite repeat on screen.

## Settings and languages

Open **Settings** in the upper-right corner to edit all global hotkeys and the display language.

- `Auto (Windows)`: Korean when the Windows display language is Korean; English otherwise
- `한국어`: always use Korean
- `English`: always use English

The default global hotkeys are:

| Action | Default |
| --- | --- |
| Start / stop recording | `F8` |
| Start playback | `F9` |
| Pause / resume | `F11` |
| Emergency stop | `F7` |

Single keys and combinations such as `Ctrl+Alt+F8` are supported. Hotkeys must be unique and may fail to register if another application already owns them.

## Event list

Consecutive mouse-move events are collapsed into one row to keep recordings readable. Use the arrow on an individual group or **Expand moves** to inspect the original events.

- Shift-click selects a continuous range.
- Ctrl-click adds or removes individual rows from the selection.
- Deleting a collapsed movement group removes every movement event represented by that group.
- Delete also works from the keyboard.

Grouping changes only how the list is displayed. Saved macro data and playback timing retain the original movement events.

## Save As dialog stabilization

Enable **Wait for Save As dialog** in advanced mode when a recorded workflow opens a slow browser or file-save dialog. MuMiClick records a wait marker and pauses playback until the dialog and its input controls are ready. The timeout is configurable from 1 to 60 seconds and defaults to 15 seconds.

Existing macros can receive the same marker with **Insert Save wait** in the event list.

## Instant mouse movement

Enable **Jump before click** to skip intermediate mouse movement during playback and jump directly to the accurate coordinates immediately before clicks, wheel input, and drag endpoints. A small configurable delay, 30 ms by default, keeps drag detection reliable.

## Coordinate modes

- **Absolute screen** replays positions on the Windows virtual desktop and supports multi-monitor negative coordinates.
- **Relative to target window** finds the selected window again before playback and clicks the same client-area position even after the window moves. Playback is refused when the target cannot be found.

## Input reliability and safety

- Recording uses `WH_MOUSE_LL` and `WH_KEYBOARD_LL`.
- Playback uses scan-code `SendInput`, with separate KeyDown and KeyUp events.
- Mouse and keyboard events share one timestamp-ordered timeline.
- Playback schedules against the recording start timestamp, avoiding accumulated relative-sleep drift.
- Injected input is marked with `dwExtraInfo` and excluded from recording.
- Held keys and mouse buttons are tracked and forcibly released after stop, error, or completion.
- **Stop on physical input** is enabled by default and immediately cancels playback when the user operates the real mouse or keyboard.
- Emergency stop has an independent cancellation path and also works during loop delays.
- Per-monitor-v2 DPI awareness and `MOUSEEVENTF_VIRTUALDESK` support 125%/150% scaling and multi-monitor layouts.

Windows UIPI can block a normal process from injecting input into an elevated application. Use **Restart as administrator** and approve UAC when controlling such a target.

## Build

```powershell
dotnet build MuMiClick.sln -c Release
dotnet publish src\MuMiClick\MuMiClick.csproj -c Release -p:PublishProfile=Portable-win-x64 -o .\release\MuMiClick-win-x64
```

## Automated tests

```powershell
dotnet run --project tests\MuMiClick.SmokeTests\MuMiClick.SmokeTests.csproj -c Release
```

The smoke suite covers safe defaults, language selection, single-key hotkey parsing, JSON round trips, KeyDown/KeyUp preservation, Save-dialog wait events, timestamp ordering, and mouse-movement display grouping.

## Known limitations

- Low-level hooks and `SendInput` are Windows-only.
- UIPI requires MuMiClick to run at the same or higher integrity level as the target.
- Secure desktop, UAC credential screens, some anti-cheat software, and applications that reject synthetic input cannot be automated.
- Window-relative playback identifies windows by saved process, title, and class information; highly dynamic titles may require selecting the target again.
- Physical multi-monitor and IME scenarios still require manual validation on the intended machine.

## Privacy

Recording begins only after an explicit button or hotkey action. Macro data is stored locally, and logs never contain typed strings.
