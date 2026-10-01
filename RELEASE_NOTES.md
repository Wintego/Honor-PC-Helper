## Honor PC Helper 1.12.0

New:

- **F12 takes a screenshot.** The key opens the screen snip overlay, the same as Win+Shift+S. Windows could not assign anything to it, not even through PC Manager: the key sends a firmware event and a scan code with no virtual key behind it. The app now listens for that event (`0x28E`), the same way it handles F7 and F8. ([#15](https://github.com/Wintego/Honor-PC-Helper/issues/15))
