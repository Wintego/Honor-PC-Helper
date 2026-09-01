## Honor PC Helper 1.9.0

Download size:

- The portable executable is now compressed: 117 MB → 49 MB. Startup time is unchanged.

Responsiveness:

- The tray icon appears immediately after launch. Everything that talks to WMI and HID — the touchpad readers, the Fn+P event subscription, the first sensor read — is now brought up in the background instead of on the way to the first painted icon.
- The tray menu no longer enumerates every HID device in the system each time it opens: the touchpad is looked up once and the result is kept until the device is reconnected.
- Applying a setting from the menu no longer waits in 50 ms steps. The privileged task is polled from 5 ms, and its registration is verified once per session instead of on every command, which also removes a Task Scheduler COM call from each sensor refresh.
- The four tray icons are drawn once and reused, so a burst of system theme notifications no longer redraws them.

Driver check:

- Requests to the HONOR catalogs are compressed and reuse connections; the slow `Win32_PnPSignedDriver` inventory is enumerated in a single pass.
- Version and identifier parsing uses compiled regular expressions.

Fixes:

- The driver window no longer leaks font handles every time it is opened.
- A sensor read or a display wake-up that happens within a few seconds of booting is no longer swallowed by its own rate limit.
- A touchpad setting written to a device path that went stale is retried once against a freshly enumerated device.
- The log file is rotated to `.1` instead of being deleted when it reaches 1 MB.

Documentation:

- The READMEs now describe what each menu item actually does, which interfaces and endpoints are used, where settings, logs and downloads are stored, the command-line arguments, and how to uninstall.
