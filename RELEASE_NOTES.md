## Honor PC Helper 1.13.0

New:

- **HUNTER mode is recognised.** On gaming models such as the MagicBook Pro 16 HUNTER, the tooltip shows "HUNTER" and the tray icon shows the performance state. The performance mode item is greyed out while HUNTER is on, because only PC Manager knows how to leave it. The app reads the mode from firmware on every sensor refresh, so it now also notices when PC Manager changes the mode. ([#7](https://github.com/Wintego/Honor-PC-Helper/issues/7))
- **Target fan speed.** On models whose firmware reports it, such as the MagicBook Pro 16 HUNTER, the tooltip shows the speed the firmware is driving each fan towards next to the actual one: `3420→3400 / 3180→3200`. The MagicBook Pro 14 does not report it and keeps showing actual speeds only. ([#7](https://github.com/Wintego/Honor-PC-Helper/issues/7))

Fixed:

- The fan line no longer gets cut off in the Russian tooltip: it ran past the 127-character limit. The labels are shorter now, and if the line with target speeds still does not fit, the tooltip shows only the actual speeds.
- BIOS commands now always go to the `HWMI_0` WMI instance, the one PC Manager uses. Before, they went to whichever instance Windows listed first, and some laptops also have `HWMI_1`.
