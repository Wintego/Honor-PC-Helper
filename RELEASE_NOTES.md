## Honor PC Helper 1.9.1

Driver export and import:

- The driver window can now save every third-party driver installed in the system to a single zip archive, and put drivers back from such an archive or from a single `.inf` file. Both operations run `pnputil` in a child process elevated once through UAC; the window reports how many packages were transferred and whether Windows asked for a restart.
- The export archive is the one to keep before reinstalling Windows: it does not depend on HONOR's catalogs still serving packages for the model.
- After an import the device inventory and the update check are rebuilt, so the versions on screen are the ones now installed.
- New command-line arguments `--export-drivers <archive.zip>` and `--import-drivers <archive.zip|folder|file.inf>`.

Driver window:

- The window now opens at its final height. The device list is built while the tray icon starts, so the rows are laid out before the window is shown instead of a second after it, and the window no longer grows in front of you.
- The list stays the same list across an update check. Row titles come from the local device inventory only; the check now merely fills in the release date, the available version and the colour, instead of rewriting every name with the title of the HONOR package.
