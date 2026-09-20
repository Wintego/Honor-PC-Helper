## Honor PC Helper 1.10.0

Function keys:

- **F7 mutes the microphone again, and the LED in the key follows it.** The keyboard only raises a BIOS event: muting the input and lighting the LED were both done by HONOR PC Manager, so once it is gone the key does nothing and the light stays wherever it was left. The app now toggles the default recording device — the same mute Windows sound settings show — and drives the LED through the HONOR BIOS WMI interface. A mute made anywhere else, such as from the volume mixer, moves the LED too, and the LED is restored at startup and after wake.
