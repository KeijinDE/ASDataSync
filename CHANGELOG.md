# Changelog

## 1.1.1

* A saved game config folder that no longer exists falls back to
  `benilla-config` in the game folder or `C:\Git\wenilla\benilla-config`,
  whichever exists.

## 1.1.0

* If `AzerothSync.exe` sits in the game folder, it uses that game automatically.
* Game config folder: taken from the game folder if `benilla-config` exists
  there, otherwise `C:\Git\wenilla\benilla-config` is suggested.
* Hints on start when the game or the game config folder is not found.
* Version shown in the window title.

## 1.0.0

* First release: sync of save game, AddOns and addon settings through a cloud
  folder, Play mode, one conflict question, backups, desktop shortcut.
