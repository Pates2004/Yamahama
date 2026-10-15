# Yamahama — Yamaha SR-C30A remote for Windows

An unofficial Bluetooth Low Energy remote, written in C# / WPF with Polish and English interfaces. Test version: **0.16.0**. The project is not affiliated with, supplied by or endorsed by Yamaha Corporation. Product names belong to their owners. [Polska dokumentacja](README.md).

## Requirements and setup

Windows 10 2004 or later / Windows 11 **x64**, a BLE adapter and driver, a nearby soundbar and **.NET Desktop Runtime 10 x64** are required. The runtime is not bundled; if missing, the standard .NET host message appears. No installer, phone or administrator privileges are needed, but the application folder must be writable.

After building locally, run `installfiles/Yamahama.exe`. Keep `langs` with `pl.lng` and `en.lng` beside the EXE; the EXE alone is not a complete package. Downloading source files does not provide a ready-built application.

First-run setup requires a language, a Light or Dark theme and a successful save. X or Cancel exits incomplete setup; Escape does not close it. Settings later also provides the System theme, hints and **100–200%** text size.

Only one Yamahama instance runs for the current Windows user and session. Launching it again brings back the existing window, including when minimized or hidden in the tray. During initial setup it brings back that setup dialog. It does not repeat automatic connection or device power-on. This also applies to another copy of the program or a different `--config`; the current configuration stays active.

## Interface and capabilities

The main window has two tabs: **Soundbar control** for sound settings and **Devices** for discovery, connection, disconnection and saved soundbars. Other views open as separate application windows:

| Menu | View |
| --- | --- |
| **File → Settings…** | Appearance, main screen, controls and feedback, startup and power, closing, and profiles and application defaults. |
| **Help → Help and application information…** | Usage, limitations, touch-panel and reset guides. |
| **Help → Device information…** | Model, firmware and reading freshness. |
| **Help → Firmware** | Device version and update guidance. |
| **Help → Default sound settings — limitations** | Explains the limitations without changing the device. |
| **Help → Soundbar factory reset — instructions** | Procedure and warnings without sending a reset command. |

Auxiliary windows do not block normal control. Selecting the same menu command activates its existing window. Close, X or Escape closes only that auxiliary window; in Settings this discards changes and requires confirmation if preferences were changed. First-run setup keeps its own confirmation and is not part of this mechanism.

While a connected soundbar is off, sound controls remain keyboard-reachable, but an attempted change sends no command and reports: **The soundbar is off. Turn on its power to change settings.** Confirmed values remain unchanged. This also covers shortcuts, tray commands and applying a profile. Power, state refresh, device management and application preferences remain usable; sound adjustments resume after power-on is confirmed. This warning is not an optional hint and remains available when operation messages are disabled.

Settings has six keyboard-accessible tabs:

| Tab | Contents |
| --- | --- |
| **Appearance** | Language, theme and text size. Resetting this tab preserves the selected language. |
| **Main screen** | Nine visibility settings: power, input, volume, mute, subwoofer gain, sound mode, Clear Voice, Bass Extension and indicator brightness. |
| **Controls and feedback** | Device volume and subwoofer control methods, immediate selection application, hints, messages and readouts. |
| **Startup and power** | Windows startup, startup device, automatic connection, startup power-on and preventing the device from turning off. |
| **Closing** | Device power-off when closing the application, main-window tray hiding and a warning before closing the application. |
| **Profiles and application defaults** | The connected device's named profile list, a name field for saving a profile, and restoring defaults for the whole application without deleting profile data. |

**OK saves and applies all drafted preference changes.** There is no live preview before OK: language, theme, text scale, visibility, control method and application behavior remain unchanged. **Cancel**, Escape or closing Settings discards unsaved changes after confirmation if anything was changed; decline to return to editing. A save failure keeps the window open to fix the problem and retry, without treating the changes as accepted for this session only. If an operation is running, wait for it to finish and choose OK again.

Each of the first five tabs has a **Restore default settings** button. Its confirmation identifies the current tab; the reset affects only that tab’s preferences, without changing the language, other tabs, saved devices or profiles. Only **OK** saves and applies the reset; **Cancel** discards it. Appearance no longer changes control visibility; restore that on the **Main screen** tab.

Switching tabs does not save changes. The startup-device selector is beside automatic connection and startup power-on. Automatic-connection and power-protection safety information is now in **Help** and remains available with hints disabled.

Controls include power/standby; TV, Optical, Bluetooth and Analog inputs; volume **0–50**, mute, **subwoofer gain −4…+4**; Stereo, Standard, 3D Movie, Game; Clear Voice, Bass Extension and indicator brightness. Model, firmware and state readings are available.

**Subwoofer gain** is a clearer label for the existing adjustment, not an additional feature or Bass Extension. The protocol was identified for SR-C30A interoperability, including analysis of Sound Bar Remote 2.50; support for other models is not claimed.

## Connection and defaults

Enable Bluetooth, leave the soundbar connected to power, choose **Search**, a device and **Connect and save**. Saving requires verified SR-C30A identity and fresh state, not merely a matching name. **File → Connect once** does not save a new device; use **Add device** later if needed.

Address and identity are checked again on connection. A changed address requires another search. Removing an entry removes its profiles but does not disconnect the session or alter Windows pairing. Close phone control if problems occur. **BLE does not transmit audio or select the Windows audio output.** Losing a connection does not trigger automatic reconnection.

| Setting | Default |
| --- | --- |
| `AutoConnectAtStartup` | **On**, connection only, without a power command. |
| `PowerOnAtStartup` | Off. |
| `PowerOffOnExit` | Off. |
| `KeepPoweredOn` | Off. |
| `StartWithWindows` | Off. |
| `VolumeControlMode`, `SubwooferControlMode` | **Slider**; buttons remain available. |
| `ValueDisplayMode` | **StaticText** — static text; read-only fields or no duplicated readouts are also available. |
| `CloseToTray` — closing the main window, including X and Alt+F4, hides it in the tray | Off. |
| `ConfirmExit` — confirmation of actual application exit | **On.** |

**New defaults apply to missing keys.** Explicit saved choices, including `false` and `Buttons`, remain unchanged.

Without a target, automatic connection **silently skips the attempt**, including on first launch. It does not choose from an existing list, even if several devices are saved. The first **newly remembered, verified** device becomes the target only when none exists. Remembering an existing entry again does not fill an empty selection; an existing target is not replaced automatically.

Startup power-on is independent: it requires a valid saved target, provides its own connection and turns the soundbar on only after confirming standby. Both options still mean one attempt per launch. Changing settings or restoring from the tray does not repeat it. Removing the target disables both startup actions without choosing another device. Power-on may play the previous input at its previous volume.

Windows startup applies at the current user's sign-in and may be blocked by Windows. A foreign `Yamahama` entry is not overwritten. Before moving the EXE, disable startup in the old location and save with OK, then re-enable it in the new location and save with OK again; copying configuration does not register startup.

## Power, tray and exit

**Power protection** operates after a powered-on state is confirmed in the current session, including in the tray. It does not wake a device already off when connected. State is checked about every 15 seconds; repeated power-on attempts/retries are limited to one per 60 seconds. Active BLE and an awake computer are required; factory auto-standby is not changed.

**Important:** the cause of standby is unknown. Protection may turn on a soundbar switched off by the remote, touch panel or application. Disable it to leave the device in standby.

**Turn off the soundbar when exiting the application** is independent and off by default. On actual exit, the application stops protection, drains cancelled operations and reads fresh state. It turns off only the currently connected, verified soundbar, including a one-time session; an already-off device is skipped. It never reconnects or sends PowerOn. Fresh confirmation is required within a bounded time. Failure or uncertainty produces a warning — application exit alone does not prove hardware power-off.

With tray hiding enabled and confirmed with OK, closing the main window, including **X, Alt+F4 and Close in the Windows system menu**, hides it and preserves BLE. **This is not an exit, does not ask to exit and does not turn off the soundbar.** Auxiliary windows close; any drafted Settings changes need a separate decision. Explicit **File → Exit** or **Exit Yamahama in the tray icon menu** still exits regardless of tray hiding. Actual exit confirmation is on by default; disable it on the Closing tab and save with OK if desired. Cancelling confirmation keeps the application running. Closing an auxiliary window alone does not exit. The icon menu can restore the window, control the device, refresh, disconnect, cancel or exit.

## Adjustment, configuration and profiles

Choose sliders/buttons independently in Settings and confirm with OK. Actual sound adjustments immediately request a change; a slider preview **is not confirmation**. The screen reader receives the last confirmed `RangeValue`. Subwoofer adjustment may need several steps, each with a fresh reading. After errors or cancellation, some changes may already have occurred — use **Refresh**, rather than assuming rollback.

Input, sound mode and indicator brightness lists apply immediately by default; Apply buttons can be enabled. In first-run setup and Settings, **Current value display** has three choices: **Read-only fields**, **Static text** (the default) and **None**. None hides only duplicated parameter readouts; it does not remove essential control names and states, connection and power state, errors or warnings.

Routine operation messages default to off, while confirmed state-change announcements default to on. These settings are independent of the value display mode and are saved. Disabling them does not disable standard NVDA control-name and state feedback. Disabling hints does not remove essential names, state, errors or warnings.

Each saved device can have **multiple named sound profiles**: mode, Clear Voice, Bass Extension and subwoofer gain. Select a profile in the list to apply or delete it; to create another, enter a different name in **Profile name** and save the current sound. Saving needs fresh state; saving under an existing name, applying and deleting require confirmation. A legacy single profile is migrated into the named list without losing its settings. Applying requires an already-on device and does not change input, master volume, mute or pairing. An error stops the operation without automatic rollback. A profile is not a factory reset. **Saving, applying or deleting a profile and soundbar actions are independent of drafted application preferences:** they run when selected, with their own confirmations where required, do not wait for OK and are not undone by Cancel. Reset guide commands are in the **Help** menu and only open instructions; they send no reset command.

`config.json` is created **beside the EXE**, not relative to the terminal; a write error does not redirect data to AppData. Explicit file selection: `Yamahama.exe --config "D:\Yamahama\config.json"`. Do not edit it while running or publish it: it contains device names and addresses. Older AppData files are not migrated automatically. Move compatible configuration only after closing the application and checking what you overwrite; the old `ui-preferences.json` does not replace `config.json`.

**Readout-setting compatibility:** `ValueDisplayMode` stores the string name `ReadOnlyFields`, `StaticText` or `None`. When the new key is absent, legacy `UseReadOnlyValueFields` set to `true` means `ReadOnlyFields`, while `false` or an absent legacy key means `StaticText`. An explicit new mode, including `None`, takes precedence regardless of key order or key-name casing. Migration occurs in memory: loading or a validation failure does not rewrite the source file. Saving writes only the new readout key, not the obsolete switch; `SchemaVersion` remains 1, and other preferences and saved devices are preserved.

**Restore default settings** on the **Profiles and application defaults** tab affects the whole application, not just profiles. It requires a separate confirmation of that scope and stages default preferences in the draft; only **OK** saves and applies them, while **Cancel** discards them. It keeps completed first-run setup, language, saved devices and profiles — it does not reopen setup. It also stages clearing the startup target, so automatic connection silently remains inactive after confirmation until a target is selected. This is not a soundbar reset or removal of pairing. Resetting an individual tab also waits for OK; there is no longer a separate button that restores both the theme and control visibility.

## Keyboard and accessibility

- **Tab / Shift+Tab** — controls and visible parameter readouts; **Space, Enter and arrows** — standard control interaction.
- Enter a profile list or a saved or discovered device list with **one Tab press**; **arrow keys** immediately change selection without another Tab. The next Tab leaves the list.
- **Ctrl+Tab** — switch main-window tabs or the six Settings tabs, depending on the active window. Arrow keys on a header select a tab; Tab enters its content.
- Sliders: arrows step by 1; **Page Up / Page Down** changes volume by 5 and subwoofer by 1; **Home / End** selects minimum/maximum.
- **Ctrl + plus/minus** — volume; **Alt + plus/minus** — subwoofer; numeric-keypad keys also work.
- **Alt** — menus, which are excluded from the normal Tab / Shift+Tab sequence; **Win+B** — notification area; **Shift+F10** — selected icon's menu.
- **Escape** — auxiliary window or open dropdown, not the main window or mandatory setup. In Settings it acts like Cancel: unsaved changes require confirmation before being discarded.

The optional **Application sections** and **Settings categories** tab-group names follow the hints setting and disappear when hints are disabled and confirmed with OK. Individual main-window tab names and all six Settings tab names remain available.

UI Automation exposes names, labels, states and notifications; important information does not rely on colour alone. **Full NVDA speech evaluation is not confirmed.**

## Manual, firmware and limitations

Official Yamaha sources: [online guide](https://manual.yamaha.com/av/22/src30a/en-US/), [full English PDF](https://download.yamaha.com/files/tcm:39-1545200), [language selector](https://manual.yamaha.com/av/22/src30a/language.html). The manual is not part of the application; project guides are not an authorized translation.

Guides: [firmware](docs/en/firmware-sr-c30a.txt), [Child Lock](docs/en/panel-dotykowy.txt), [reset](docs/en/reset-sr-c30a.txt), [adapters](docs/en/adaptery-bluetooth.txt). The first three are also available offline in Help. The application does not switch adapters, **flash firmware** or download packages. No SR-C30A package was found in the catalogues checked on 2 October 2026; this does not establish 1.00 as the latest version. Follow the manufacturer's procedure for the correct model and package.

No unverified BLE commands are exposed for Child Lock, CEC, Bluetooth/standby settings, factory auto-standby, DRC, installation tuning or factory reset. The guides describe physical procedures; an ordinary BLE adapter is not an IR transmitter.

## Source, build and verification

Code: [protocol and models](src/YamahaRemote.Core/), [Windows application](src/YamahaRemote.Windows/). [Tests](tests/) cover protocol, configuration and WPF flows with a controller test double, including failures, cancellation and unconfirmed readings.

On Windows with the **.NET 10 SDK**, from the source root:

```powershell
.\build.ps1
dotnet test YamahaRemote.slnx -c Release
```

The script preserves previous builds in `D:\work\Snapshots\<project folder name>`, protects configuration and creates `installfiles` with the EXE and `langs`. It uses `SelfContained=false`, `PublishSelfContained=false`, `UseAppHost=true`, `OutputType=WinExe`, without Native AOT. Do not run concurrent operations that modify the same build outputs.

Results apply to a particular build. **Managed tests do not confirm physical power cycles, every command's hardware effects or full NVDA speech.** There is no guarantee of zero bugs or compatibility with every adapter. Report the version and reproduction steps, and remove private data from diagnostics.

Build outputs go to `D:\work\Workspace\<project folder name>\build`, while the current release goes to the local `installfiles`. The same layout on C: is used only if drive D: does not exist.

## Publication and license

Public repository: [Pates2004/Yamahama](https://github.com/Pates2004/Yamahama).

Copyright (C) 2026 Patryk (Pates2004). Original project code and guides are available under the **GNU General Public License, version 3 — GPL-3.0-only**. See [LICENSE](LICENSE) for the complete terms; the program is provided without warranty. The project license does not cover separate manufacturer materials or third-party dependencies.

The repository provides source code and tests. Build the application with the command above. Private configuration, diagnostics, archived builds, the Android application and the downloaded Yamaha PDF are excluded. The official manual remains available through the manufacturer's links.

Dependencies retain their own terms: [.NET](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [WPF](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT), [xUnit](https://github.com/xunit/xunit/blob/main/LICENSE), [Microsoft Test Platform](https://github.com/microsoft/vstest/blob/main/LICENSE) and [Windows SDK](https://aka.ms/WinSDKLicenseURL). The script publishes a local framework-dependent build; the project license does not replace those component terms.
