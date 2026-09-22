# Honor PC Helper — HONOR MagicBook hardware control for Windows

[Русский](README.ru.md) | [中文](README.zh.md)

[![Latest release](https://img.shields.io/github/v/release/Wintego/Honor-PC-Helper?label=download)](https://github.com/Wintego/Honor-PC-Helper/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Wintego/Honor-PC-Helper/total)](https://github.com/Wintego/Honor-PC-Helper/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20x64-blue)](#requirements)

**Honor PC Helper** controls HONOR laptop hardware from the Windows tray: battery charge limit, keyboard backlight, performance mode, touchpad haptics and edge gestures, the microphone and camera function keys, and driver updates from HONOR's catalogs. It drives the HONOR BIOS WMI interface directly, so HONOR PC Manager does not have to be installed or running.

One portable `.exe` (~49 MB, self-contained): no installer, no service, no telemetry. The only background network request is the app's own update check against GitHub Releases — 2 minutes after start, then every 6 hours. HONOR's driver catalogs are queried only while **Drivers** is open.

![Honor PC Helper tray menu on a HONOR MagicBook: charge limit, keyboard backlight, performance mode and touchpad settings](Assets/Screenshot-en.png)

## Tray menu

| Menu item | Options | Effect |
| --- | --- | --- |
| **Charge limit** | Disabled (0–100%), Home 40–70%, Office 70–90%, Travel 95–100% | Writes both thresholds into the EC. They survive reboot and stay in effect while the app is not running |
| **Keyboard → Backlight** | Off, Weak, Strong | Sets the backlight level. Changes made from the keyboard are reflected in the menu |
| **Keyboard → Timeout** | Never, 15 s, 30 s, 1 min (default), 5 min | Idle timeout after which the firmware turns the backlight off |
| **Keyboard → Schedule** | On/off, turn-on hour, turn-off hour, level | Turns the backlight on and off at whole hours. Changing the level by hand suspends the schedule until the next boundary |
| **Touchpad → Vibration strength** | Low, Medium, High | Haptic feedback of the force pad. Shown only on models that have one |
| **Touchpad → Edge gestures** | Brightness (left edge), Volume (right edge) | Enables or disables the vertical one-finger edge swipes |
| **Performance mode** | On/off checkbox | Same switch as **Fn+P**. Requires AC power and at least 20% charge; turned off on sleep and when the charger is unplugged. The tray icon is filled while it is on |
| **Update to …** | — | Appears while a newer release exists. Downloads and installs it; the app restarts |
| **Drivers** | — | Opens the driver and BIOS window, see [Drivers](#drivers) |
| **Start with Windows** | On/off checkbox | Adds or removes an `HKCU\…\Run` entry |

Hovering the tray icon shows the live state: mode, backlight level, charge range, charge/discharge power in watts, CPU and battery temperature, both fan speeds. Sensors are polled at most once every 5 seconds and only while the pointer is on the icon.

## Function keys

**F7** mutes and unmutes the default recording device — the same mute Windows sound settings show — and sets the LED in the key to match. The LED follows a mute made anywhere else, such as from the volume mixer, and is restored at startup and after wake.

**F8** turns the camera off and on and raises a system notification with the new state. The device stays connected and the switch works mid-call: apps receive a black frame. The state survives a reboot. The first press may ask for administrator rights once.

**Fn+P** switches performance mode — the same switch as the tray menu item.

Backlight level, haptics and edge gestures are reapplied after resume (including modern standby) and after the touchpad reconnects. The charge limit is checked after resume as well, and the chosen range is written back when the EC has dropped it.

Interface language follows the Windows display language: English, Russian, Simplified Chinese.

## Requirements

- Windows 10 or 11, x64
- A HONOR laptop that exposes the HONOR BIOS WMI interface (`OemWMIMethod` in `root\WMI`) — the MagicBook family ships with it
- No .NET runtime: the release build is self-contained
- HONOR PC Manager does **not** have to be installed or running

Developed and verified on a HONOR MagicBook Pro 14 2026 (`ZQC-P`, BIOS 1.10, Windows 11 26200). The available features depend on the machine and the BIOS: touchpad haptics and edge gestures exist only on force-pad models, some machines expose fewer sensors. Anything the firmware does not answer is hidden from the menu.

## Install and run

1. Download `HonorPCHelper.exe` from the [latest release](https://github.com/Wintego/Honor-PC-Helper/releases/latest).
2. Run it — no installation, no dependencies. The icon appears in the system tray.
3. Left-click or right-click the icon to open the menu.

### Administrator rights

Hardware commands go through a Task Scheduler task named **Honor PC Helper Privileged Hardware**, which runs this same exe as the current user with the highest privileges and applies one pending command. The task is created the first time you change a hardware setting — that is the one and only UAC prompt. From then on every change, including sensor reads, goes through the task without elevation.

The left-edge brightness gesture additionally needs a one-time permission on the ACPI-WMI data block `abbc0f5b-8ea1-11d1-a000-c90629100000` (`HKLM\SYSTEM\CurrentControlSet\Control\WMI\Security`). The privileged task grants it to your account on first use; until then brightness falls back to `WmiSetBrightness`, which changes brightness in 3% steps without the Windows OSD.

## Drivers

**Drivers** in the tray menu opens a window with the BIOS version and the driver and software list. The device inventory is built at startup in the background, so the list is already populated when you open it.

- The machine is matched against HONOR's catalogs by BIOS `DeviceTypeEx`/`CVersion`, board and product identifiers, CPU model and memory size.
- Packages come from HONOR's update platform (`update.platform.hihonorcloud.com`) and, as a fallback, from the official support catalogs (`selfservice-ap/eu/cn.honor.com`). Links that answer 404/410 are dropped.
- Green means the installed version matches the offered one, red means an update, grey means the local version could not be determined. A version counts as an update only when both numbers are comparable.
- Clicking a version downloads the package, verifies it and asks where to save it: SHA-256 when the server publishes one, Authenticode signature on every `.exe`, and an Honor/Huawei publisher when the file did not come from an official HONOR host. Archives are unpacked with path-traversal protection.
- **The installer is never started for you.** You get a verified file and decide whether to run it.
- **Driver export and import.** The buttons in the list header save every third-party driver in the system to a single zip (`pnputil /export-driver`) and put them back from such an archive or from a single `.inf` (`pnputil /add-driver … /subdirs /install`). Both run in a child process elevated once per operation through UAC; after an import the list is rebuilt, and a required restart is reported separately.

The same window shows the Honor PC Helper version and offers the update when a newer release exists.

## Updating the app

The app checks GitHub Releases in the background — 2 minutes after start, then every 6 hours — and whenever the Drivers window is open. While a newer version is available, a red dot sits in the top-right corner of the tray icon and an **Update to …** item appears in the menu; it opens the Drivers window and starts the install.

The update is downloaded, then checked against the release asset size, the `sha256` digest published by GitHub, the PE header and the version resource. The running exe is renamed aside, the new build takes its place and starts as the ordinary user process — no administrator prompt, unless the exe lives in a write-protected folder such as `Program Files`. If anything fails, the previous build is put back. Leftovers are removed at the next start.

## Where it keeps things

| What | Where |
| --- | --- |
| Settings and cached state | `HKCU\Software\HonorPCHelper` |
| Autostart entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `HonorPCHelper` |
| Camera state | `HKLM\SOFTWARE\HONOR\ASvidDMFT\Settings\CameraStatus` |
| Log (1 MB, rotated to `.1`) | `%LocalAppData%\HonorPCHelper\HonorPCHelper.log` |
| Downloads (removed at next start) | `%LocalAppData%\HonorPCHelper\DriverUpdates`, `…\AppUpdates` |
| Privileged task | Task Scheduler → `Honor PC Helper Privileged Hardware` |

Charge thresholds and the backlight timeout are stored by the firmware itself, not by this app.

## Command line

Without arguments the app runs as a tray icon.

| Argument | Purpose |
| --- | --- |
| `--set-battery-mode <Disabled\|Home\|Office\|Travel>` | Applies the setting with elevation and registers the privileged task |
| `--set-keyboard-backlight <Off\|Low\|High>` | Same, for the backlight level |
| `--set-keyboard-backlight-timeout <seconds>` | Same, for the idle timeout (0 = never) |
| `--set-power-unlock <true\|false>` | Same, for performance mode |
| `--set-camera-off <true\|false>` | Same, for the camera switch |
| `--apply-…` | The same settings, applied silently by the privileged task |
| `--install-privileged-tasks` | Creates the privileged task without changing anything |
| `--uninstall-privileged-tasks` | Removes it (run from an elevated prompt) |
| `--export-drivers <archive.zip>` | Exports the driver store into an archive (requires administrator rights) |
| `--import-drivers <archive.zip\|folder\|file.inf>` | Adds the drivers to the store and installs them on the devices |
| `--restart-after <pid>` | Used by the updater: waits for the old process, then starts the tray |

Exit codes: `0` success, `1` failure, `2` the argument value was not understood.

## Troubleshooting

**Some menu items are missing.** Those features are not exposed by your BIOS or your touchpad.

**A setting does not apply.** Look at `%LocalAppData%\HonorPCHelper\HonorPCHelper.log`; every rejected BIOS command is logged with its error code. If the privileged task was deleted or the exe was moved, the next change re-registers it with one UAC prompt.

**Fn+P is not reflected in the menu.** The app listens to `OemWMIEvent`; if the WMI event subscription could not start, the reason is in the log.

**F7 or F8 does nothing.** They ride on the same `OemWMIEvent` subscription as Fn+P, so check the log for that first. The event codes are `0x287` (microphone) and `0x288` (camera) on a MagicBook Pro 14; if yours sends something else, open an issue with the codes your machine reports.

**F8 shows the notification but the camera keeps filming.** The model ships a camera without the Device MFT that reads `CameraStatus`. Open an issue with the laptop model and the camera's device ID from Device Manager.

**The brightness edge swipe changes brightness but shows no OSD.** The ACPI-WMI permission has not been granted yet — change any hardware setting once so the privileged task exists, then swipe again.

**The driver check reports nothing.** The app ignores the system proxy, but a firewall can still block `hihonorcloud.com` and `honor.com`.

## Uninstall

1. Untick **Start with Windows**, then **Exit**.
2. From an elevated prompt: `HonorPCHelper.exe --uninstall-privileged-tasks`.
3. Delete `HKCU\Software\HonorPCHelper`, `%LocalAppData%\HonorPCHelper` and the exe.

Set the charge limit to **Disabled** before removing the app if you want the battery to charge to 100% again — the thresholds live in the EC and stay there.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
.\build.ps1
```

The result is a single self-contained, compressed `dist\HonorPCHelper.exe`.

## Donate

Honor PC Helper is free, open source, and carries no ads or telemetry. If it saved you from installing HONOR PC Manager, you can support further development — any amount helps, and there is no obligation.

| Method | Address |
| --- | --- |
| **BTC** | `bc1qeek2nwwhpd4faw2cla6nzzhjes28p9pdkd52lh` |
| **ETH** | `0x3090134F22f62924843991c86f73cf53525B4432` |
| **GRAM** | `UQAIAv3WrIrgtY2ZmJHx7JWXqH_BTRlhY18slpF1Sh7iPvAV` |
| **USDT (TRC20)** | `TAWc6Yzs52ezkMD43PsKzFLgYpswnRBPwP` |
| **TBank** | https://www.tbank.ru/cf/3hp5vsr5mJ3 |

---

Not affiliated with, endorsed by or supported by HONOR. HONOR and MagicBook are trademarks of their respective owners.
