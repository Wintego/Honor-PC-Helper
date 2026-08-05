## What's new

- Left-edge touchpad brightness no longer requires the virtual HID driver: it goes through the native ACPI-WMI path and Windows still shows its usual OSD. The `driver` folder and its installation are gone.
- The tray menu opens faster: settings are read from an already open registry key instead of reopening it for every value.
- Fixed granting ACPI brightness access rights: the UAC prompt finished silently and changed nothing.
- A command rejected by the BIOS is no longer retried.
- Touchpad error messages are now translated into English and Chinese.
