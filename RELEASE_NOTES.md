## Honor PC Helper 1.13.1

Fixed:

- The Drivers window no longer fails on laptops that have a device without a driver. Such a device made the whole driver list throw `NullReferenceException`, so the window showed nothing. ([#10](https://github.com/Wintego/Honor-PC-Helper/issues/10))
- A failed update check is no longer logged as an error. When GitHub answers with something other than success, such as 502 Bad Gateway or 403 from the rate limit, the app writes one INFO line and keeps the update it has already found, so the dot on the tray icon stays. ([#12](https://github.com/Wintego/Honor-PC-Helper/issues/12))
