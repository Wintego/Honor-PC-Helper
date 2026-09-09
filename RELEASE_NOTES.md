## Honor PC Helper 1.9.2

Charge limit:

- The chosen charge range is restored after wake. On some models the battery controller drops the thresholds when the lid is closed and opened again, and the tray menu then showed "Disabled" because that is what the EC reported. The app now re-checks the thresholds three times over the first ten seconds after wake and writes the chosen range back if it is gone — through the background task, so no UAC prompt appears.
- The mode picked in the menu is stored separately from the mode read out of the EC, so a firmware reset can no longer overwrite the choice.
- The modes are now named by the level they stop at - "Home (up to 70%)" instead of "Home (40-70%)". Both thresholds are still written to the EC, but the lower one does not describe what a person sees: with a limit set, connecting the charger below that limit starts charging straight away instead of waiting for the battery to fall to the lower number.
