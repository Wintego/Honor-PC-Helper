## Honor PC Helper 1.11.0

Updates:

- **A new version now announces itself in the tray.** Until now the app only learned about its own updates while the Drivers window was open, so a release could sit unnoticed for weeks. The app checks GitHub Releases in the background — 2 minutes after start, then every 6 hours — and while a newer version is available it paints a red dot in the top-right corner of the tray icon and adds an **Update to …** item to the menu. The item opens the Drivers window and starts the same download-and-replace that the version link there performs, so the install is the one that was already tested: checksum, PE header and version resource are verified before the running exe is swapped.

Function keys:

- **F8 turns the camera off and on again, with a system notification.** The keyboard only raises a BIOS event (`0x288`); switching the camera was done by HONOR PC Manager, so once it is gone the key does nothing. The app now drives the same value that HONOR PC Manager did — `HKLM\SOFTWARE\HONOR\ASvidDMFT\Settings\CameraStatus`, which the Device MFT inside the camera driver reads while processing each frame. The device stays connected and nothing is disabled: the switch is instant, works mid-call, apps just start receiving a black frame, and the state survives a reboot. The registry key belongs to administrators, so the first press may ask for rights once; after that the app writes the value itself.
