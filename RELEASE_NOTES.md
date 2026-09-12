## Honor PC Helper 1.9.3

Driver updates:

- Drivers and BIOS are matched to the exact model again. The shortcut that maps a machine to its HONOR support catalog also accepted the platform code, which several models share, so a MagicBook 16 2026 (JGC-N) was shown the catalog of a MagicBook Pro 14 2026 (ZQC-P). A machine is now identified by its own model code and processor, and every other machine resolves its catalog from the HONOR product tree as before.
- This is what made an audio driver stay out of date however often it was installed: the offered package was built for a Realtek codec, and the machine has a Senary one, so the installer had nothing to update. The right package is offered now.
- For the same reason a BIOS update could be offered that the model's own support page does not list, because it belongs to a different board. A BIOS version from another model can no longer appear.
