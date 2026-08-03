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
- **Dark mode**: switches the application surface and title bar to a dark appearance

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

## Edit individual actions

Select one recorded mouse or keyboard row and click **Edit action**, double-click it, or press `Enter`.

- Convert between mouse move, mouse click, mouse wheel, and keyboard key press.
- Edit mouse X/Y coordinates, button, wheel delta, key, and press duration.
- A click or key press is always written as a balanced Down/Up pair so editing cannot leave a button or key held down.
- When a Down/Up pair surrounds intermediate actions, such as mouse movement during a drag or keys inside a shortcut, those intermediate actions retain their original positions.
- Expand a collapsed mouse-movement group before editing one movement.

Select either the Down or Up row; MuMiClick locates and edits the matching pair. Random branches continue to use their dedicated branch editor. Control events such as clipboard assignment, Save-dialog wait, and window-text wait can be deleted and reinserted with their respective toolbar controls.

## Variables and clipboard events

Use **Variables** above the event list to define reusable name/value pairs. The optional **Group** column combines variables that should be used as one random pool; enter the same group name on each member.

Select the position where the value is needed and choose **Set clipboard**. The event dialog supports two modes:

- **Use one fixed variable** always places the selected variable on the clipboard.
- **Choose randomly from a variable group** independently and uniformly selects one group member each time the event is played.

Record or place `Ctrl+V` immediately after the clipboard event to paste the selected value into the active field.

Variables and clipboard events are stored inside the `.mumacro` file. Clipboard access is retried briefly when another application has it locked, and actual values are never written to logs.

## Random action bundles

**Random branch** groups actions already present in the current recording; it does not load another macro file.

1. Click **Random branch** and select mouse moves, clicks, or key events on the left. Shift-click selects a range.
2. Choose **Selection → Add branch** to make the first action bundle.
3. Select another set of current events and add at least one more bundle.
4. Apply the editor. The source actions are replaced by one random-branch row.

Each time playback reaches that row, exactly one bundle is selected uniformly at random. The original event order and relative timing inside the selected bundle are preserved. Select the random-branch row and open **Random branch** again to rename or edit its alternatives.

## Save As dialog stabilization

Enable **Wait for Save As dialog** in advanced mode when a recorded workflow opens a slow browser or file-save dialog. MuMiClick records a wait marker and pauses playback until the dialog and its input controls are ready. The timeout is configurable from 1 to 60 seconds and defaults to 15 seconds.

Existing macros can receive the same marker with **Insert Save wait** in the event list.

## Wait for text in Chrome or another window

Use **Wait for text** above the event list when the next action must not run until a message, label, button, or status text appears.

1. Select the event-list position where playback should pause and click **Wait for text**.
2. Select the Chrome or application window to monitor.
3. Enter the expected text and choose **Contains** or **Exact text**.
4. Set a timeout from 1 to 3600 seconds, or use `0` for unlimited waiting.

Playback checks the window's Windows accessibility tree every 350 ms. When the text appears, playback continues while compensating for the wait duration so later inputs do not bunch together. Pause/resume and emergency stop remain available during the wait. Chrome window titles may change during navigation; MuMiClick safely falls back to the same process and window class when the match is unambiguous.

This is accessibility-text detection, not OCR. Text painted only onto a canvas or image, or intentionally hidden from accessibility APIs, cannot be detected. Chromium documents that its accessibility tree is exposed to Windows automation and assistive-technology clients; see the [Chromium accessibility overview](https://chromium.googlesource.com/chromium/src/+/main/docs/accessibility/overview.md).

## Instant mouse movement

Enable **Jump before click** to skip intermediate mouse movement during playback and jump directly to the accurate coordinates immediately before clicks, wheel input, and drag endpoints. A small configurable delay, 30 ms by default, keeps drag detection reliable.

## Coordinate modes

- **Absolute screen** replays positions on the Windows virtual desktop and supports multi-monitor negative coordinates.
- **Relative to target window** finds the selected window again before playback and clicks the same client-area position even after the window moves. Playback is refused when the target cannot be found.

Use **Refresh** in the target-window picker after opening a target application that was not already listed.

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

The smoke suite covers safe defaults, language selection, single-key hotkey parsing, JSON round trips, KeyDown/KeyUp preservation, action-pair discovery across drag and shortcut events, mouse-to-key conversion, scan-code Down/Up generation, Save-dialog wait events, window-text triggers, contains/exact matching, real Win32 accessibility-tree text detection, Chrome accessibility connection, timestamp ordering, mouse-movement display grouping, variable groups, fixed and random clipboard events, random action bundles, branch selection, and the editor windows.

## Known limitations

- Low-level hooks and `SendInput` are Windows-only.
- UIPI requires MuMiClick to run at the same or higher integrity level as the target.
- Secure desktop, UAC credential screens, some anti-cheat software, and applications that reject synthetic input cannot be automated.
- Window-relative playback identifies windows by saved process, title, and class information; highly dynamic titles may require selecting the target again.
- Window-text triggers can detect only text exposed by the target application's Windows accessibility tree; canvas, image-only, protected, and inaccessible content requires a different trigger.
- Physical multi-monitor and IME scenarios still require manual validation on the intended machine.

## Privacy

Recording begins only after an explicit button or hotkey action. Macro data is stored locally, and logs never contain typed strings.

## License

Released under the [MuMiClick Community License](LICENSE). You may use, copy, modify, and redistribute the project, but modified versions must not pretend to be the official MuMiClick project or use its name and branding in a confusing way. Unlawful, deceptive, privacy-invasive, harmful, or malicious use is prohibited.
