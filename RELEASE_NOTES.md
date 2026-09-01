## Honor PC Helper 1.8.3

Application updates:

- Updating the portable executable no longer asks for administrator rights. Windows allows a running exe to be renamed, so the new build is put in place by the application itself; the elevation prompt is left only for the case where the executable lives in a write-protected folder such as Program Files.
- After such an update the application no longer restarts with administrator rights. The new build is started by the ordinary user process, the way it was started before the update.
- The update no longer depends on a PowerShell helper and no longer waits for the application to exit before replacing the file, so the restart is immediate.
- If the replacement fails, the previous build is put back instead of leaving the folder without an executable, and declining the elevation prompt is treated as a cancelled update rather than an error.
- Downloaded update files and the replaced build are removed on the next start instead of being kept in the local application data folder.
