## Honor PC Helper 1.11.1

Fixes:

- **The menu no longer reports a setting as applied when the BIOS rejected it.** Hardware settings go through the privileged scheduled task, and the tray treated a command as done as soon as the task picked it up — before it ran. If the firmware refused, say, a charge limit, the check mark still moved to the new mode. The task now reports the outcome back, and a rejected command shows the BIOS error instead of pretending to succeed or asking for administrator rights a second time.
- **No more repeated UAC prompts.** If you declined the administrator prompt, it came back every time the pointer rested on the tray icon, because the tooltip reads the sensors through the privileged task. Restoring the keyboard backlight after wake and the backlight schedule could also raise the prompt on their own. Background actions — sleep, wake, the schedule timer — now never ask for rights; the sensors and the brightness gesture ask at most until you decline once in a session; menu items ask as before.
- **The app no longer quits on an error while leaving performance mode.** A declined prompt or a failed command when the laptop went to sleep or was unplugged in performance mode could terminate the process. F8 and the brightness gesture handle the same failures quietly and write them to the log.

Performance:

- **Half the work after every wake.** The backlight level and timeout are restored with a single privileged command instead of two, so each screen wake starts three helper processes instead of six.
- **The mic key light is written only when mute actually changes.** It used to be rewritten on every move of the microphone volume slider, and twice on every F7 press.
- **The driver inventory waits a minute after start.** Listing signed drivers is the heaviest WMI query in the app and used to compete with the other startup apps at sign-in. It now runs a minute later — or immediately if the Drivers window is opened sooner.
- WMI objects of the brightness gesture are released right away instead of piling up until garbage collection.
