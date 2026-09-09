## Honor PC Helper 1.9.2

Charge limit:

- The chosen charge range is restored after wake. On some models the battery controller drops the thresholds when the lid is closed and opened again, and the tray menu then showed "Disabled" because that is what the EC reported. The app now re-checks the thresholds three times over the first ten seconds after wake and writes the chosen range back if it is gone — through the background task, so no UAC prompt appears.
- The mode picked in the menu is stored separately from the mode read out of the EC, so a firmware reset can no longer overwrite the choice.
