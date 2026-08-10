## What's new

- The `config.json` settings file is no longer read. Its three options had sensible values already, and the file was one more thing to keep next to the exe: the sensor refresh interval and the touchpad brightness gesture are now built in, and a leftover `config.json` is simply ignored.
- The brightness step of the left-edge touchpad gesture went from 5% to 3%, so a swipe adjusts the backlight more gently. This applies to the fallback path only - on machines where the HONOR firmware handles the step it stays at the firmware's 10%.
