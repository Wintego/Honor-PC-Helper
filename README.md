# Honor PC Helper — HONOR MagicBook hardware control for Windows

[Русский](README.ru.md) | [中文](README.zh.md)

[![Latest release](https://img.shields.io/github/v/release/Wintego/Honor-PC-Helper?label=download)](https://github.com/Wintego/Honor-PC-Helper/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Wintego/Honor-PC-Helper/total)](https://github.com/Wintego/Honor-PC-Helper/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20x64-blue)](#requirements)

**Honor PC Helper** is a free, open-source Windows tray utility that controls HONOR MagicBook hardware: **battery charge limit**, **keyboard backlight**, **performance mode**, **touchpad haptics and edge gestures**, and **global hotkeys**. It talks to the HONOR BIOS WMI interface directly — the same interface HONOR PC Manager uses — so it works as a lightweight PC Manager alternative on a laptop where you would rather not install the vendor suite.

A single portable `.exe`: no installer, no background services, no telemetry.

![Honor PC Helper tray menu on a HONOR MagicBook: battery charge limit, keyboard backlight, performance mode and touchpad settings](Assets/Screenshot-en.png)

## Features

- **Battery charge limit** — cap charging (for example at 70-80%) to extend battery lifespan on a laptop that stays plugged in
- **Keyboard backlight** — turn it on or off, set the idle timeout, schedule automatic evening switch-on; the level is restored after sleep and modern standby
- **Performance mode** — switch between balanced (smart) and performance mode without opening PC Manager
- **Hardware monitoring** — temperatures, fan speed and charge/discharge power in watts, right in the tray tooltip
- **Touchpad** — haptic feedback strength, edge gestures (brightness on the left edge, volume on the right), screen brightness by swiping the left edge with the native Windows OSD
- **Global hotkeys** — window and media shortcuts, reassignable from the tray menu
- **Autostart** — launches with Windows
- **Interface languages** — English, Russian and Simplified Chinese, selected automatically from the Windows display language

## Requirements

- **Windows 10 or 11, x64**
- A **HONOR laptop** that exposes the HONOR WMI BIOS interface — the MagicBook family (MagicBook 14, 15, 16, X14, X16, Pro, Art) ships with it
- No .NET runtime needed: the release build is self-contained
- HONOR PC Manager does **not** have to be installed or running

Feature availability depends on the model and BIOS version: touchpad haptics only appear on models with a force pad, and some machines expose fewer sensors. Unsupported items are hidden from the tray menu.

## Download and use

1. Download `HonorPCHelper.exe` from the [latest release](https://github.com/Wintego/Honor-PC-Helper/releases/latest).
2. Run it. No installation, no dependencies — the icon appears in the system tray.
3. Click the tray icon to open the menu.

The first time you change a hardware setting, Windows asks for administrator privileges to create a scheduled task. After that the app runs without elevation prompts.

## Hotkeys

Defaults: `Alt+M` — minimize the window under the cursor, `Alt+X` — play/pause, `Alt+C` — next track, `Alt+Z` — previous track.

To change a shortcut, click the matching item in the tray menu and press the new combination. At least one modifier is required — Ctrl, Alt or Win. `Esc` cancels, `Del` disables the shortcut. Changes apply immediately and are stored in the registry; the "Reset shortcuts to defaults" item restores the original values.

If a combination is already taken by another application, a balloon tip reports it and the menu item is marked as "in use".

## Configuration

The settings file is optional. To override the defaults, create a `config.json` next to the exe:

```json
{
  "brightnessStepPercent": 5,
  "sensorRefreshIntervalMs": 5000,
  "touchpadBrightnessEnabled": true,
  "hotkeysEnabled": true
}
```

Changes take effect after restarting the application.

## FAQ

### How do I limit battery charging on a HONOR MagicBook without PC Manager?

Open the tray menu, pick **Battery** and choose a charge range. The thresholds are written through the HONOR BIOS WMI interface and survive a reboot, exactly as if PC Manager had set them.

### How do I turn the keyboard backlight on, or stop it from switching off?

Use the **Keyboard backlight** submenu: turn it on or off and set the idle timeout. A schedule can also enable the backlight automatically in the evening.

### Is this a HONOR PC Manager replacement?

For hardware settings, yes — charge limit, backlight, performance mode and touchpad behaviour. It does not update drivers and it does not do phone-to-PC multi-screen collaboration; keep PC Manager if you need those.

### Does it need administrator rights?

Only once, to create the scheduled task that applies hardware commands. Day-to-day use runs unelevated.

### Some menu items are missing on my model

Those features are not exposed by your BIOS. Fan and temperature readings, touchpad haptics and edge gestures vary between MagicBook models and firmware versions.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
.\build.ps1
```

The output file lands in `dist\HonorPCHelper.exe`.

---

Not affiliated with, endorsed by or supported by HONOR. HONOR and MagicBook are trademarks of their respective owners.
