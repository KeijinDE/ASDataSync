# AzerothSync: technical handover

This document explains AzerothSync completely, so that a person or any AI
agent can help with problems without knowing how it was built.

## Purpose

Azeroth Survivors is a Windows game (Rust client with a WoW 1.12 interface
and Lua addons). AzerothSync lets one person play it alternately on several
Windows PCs. It moves the save game, the AddOns and the addon settings through
a folder that a cloud drive (Google Drive, OneDrive, Dropbox, ...) syncs.

**It does not merge.** Per area one complete state wins, the overwritten one
is backed up as a ZIP file first. Playing on two PCs at the same time is not
supported.

Why no merging: the `.sav` files start with `RSE1:` followed by Base64. Decoded,
the data is encrypted (entropy about 7.9 bits per byte, not Deflate, ZLib,
GZip or Brotli).

## Components

| File | Purpose |
|---|---|
| `src/Engine.cs` | All sync logic, no UI (settings, hashing, mirroring, backups, conflicts) |
| `src/App.cs` | WinForms window, conflict dialog, `--play` mode, desktop shortcut |
| `build.ps1` | Compiles with `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (C# 5, .NET Framework 4.x). Optional `-DeployTo <folder>` |
| `AzerothSync.exe` | Build output, about 35 KB, no installation |

An older PowerShell script `AzerothSync.ps1` (modes `run`, `pull`, `push`,
`status`, `install`) uses the same hash, the same `state.json` and the same
cloud layout. Switching between script and exe causes no false conflicts. The
script asks conflicts per area instead of once.

## Paths

| What | Default |
|---|---|
| Game | `C:\Games\AzerothSurvivors\AzerothSurvivors.exe` (setting) |
| Save game | `%APPDATA%\wenilla-rogue\` (`Saves\*.sav`, `characters.txt`, `characters.before-import.txt`, `language.txt`) |
| Game config | `<game folder>\benilla-config\` (`AddOns\`, `saved\`, `config.toml`, `layout\`, `chat\`, `camera\`, `cache\`) |
| Sync folder | the folder containing `AzerothSync.exe`, if it already holds `manifest.json` or `data\`; otherwise must be chosen |
| Per PC settings | `%LOCALAPPDATA%\AzerothSync\settings.ini` (keys `GamePath`, `CloudDir`, `SaveDir`, `ConfigDir`) |
| Per PC sync memory | `%LOCALAPPDATA%\AzerothSync\state.json` |
| Local backups | `%LOCALAPPDATA%\AzerothSync\backups\<area>_<yyyy-MM-dd_HH-mm-ss>.zip` (last 20 per area) |

Settings order: `settings.ini`, else `config.json` from the old script, else defaults.

## Sync folder layout

```
<sync folder>\
  AzerothSync.exe
  HANDOVER.md             this file (optional copy)
  manifest.json           {"Device": "<PC name>", "Time": "<ISO time>"} of the last upload
  data\save\              save game
  data\addons\            content of benilla-config\AddOns
  data\saved\             content of benilla-config\saved (SavedVariables)
  history\                cloud backups before each upload (<area>_<time>.zip, last 10 per area)
```

Very old layout: the save game was directly in `data\`. It is moved to
`data\save\` automatically.

## Logic

Per area there is a SHA256 fingerprint over all files: lines
`relative path in lower case:FILE SHA256`, sorted with the current culture
case insensitive (same as PowerShell `Sort-Object`), joined with `\n`, UTF8.
`NONE` means the folder is missing, `EMPTY` means it has no files.
`state.json` stores per area the fingerprint of the last sync on this PC:
`{"Hashes": {"save": "...", "addons": "...", "saved": "..."}, "LastSync": "...", "Device": "..."}`.
The old format `{"LastHash": "..."}` counts as `save`.

**Download, per area** (Play and the automatic part):
* cloud empty: nothing
* local equals cloud: only update memory
* local empty or unchanged since last sync: take cloud
* cloud unchanged since last sync: local is newer, keep
* otherwise: conflict

**Upload, per area:**
* local empty: nothing
* local equals cloud: only update memory
* cloud empty or unchanged since last sync: upload
* otherwise: conflict

**Conflicts** are collected and asked **once** for all areas: *Use cloud*,
*Keep local* or *Cancel*. On download, *Keep local* stores the cloud
fingerprint as known, so the next upload overwrites the cloud without asking.

**Download / Upload buttons** force the direction: one confirmation listing all
differing areas, no conflict questions.

**Mirroring:** the target becomes an exact copy. Files missing in the source
are deleted in the target, empty folders too. `language.txt` in the save game
is never copied and never deleted.

**Safety:** while a process with the game's executable name runs, nothing is
downloaded or uploaded. If the game config folder is missing on a PC, AddOns
and addon settings are skipped with a warning. In `--play` mode the window
closes 4 seconds after success and stays open after an error.

## Errors and fixes

| Message or symptom | Cause and fix |
|---|---|
| `No sync folder set` | Choose a folder inside the cloud drive in the window. |
| `Sync folder not reachable` | The cloud drive is not running or uses another drive letter. Start it or choose the folder again. |
| `The game is running` | Close the game. Check Task Manager for `AzerothSurvivors.exe`. |
| `Game not found` | Choose the game executable in the window. |
| `... not found, skipped. Set the game config folder above.` | The `benilla-config` folder is elsewhere. Choose it in the window. |
| Conflict dialog on the first run on a new PC | Normal, there is no memory yet. Choose *Use cloud*. |
| Game shows an empty or old state after a download | The game might reject saves from another PC. Restore `%LOCALAPPDATA%\AzerothSync\backups\save_*.zip` into `%APPDATA%\wenilla-rogue\`. |
| Progress missing on the other PC | The cloud drive uploads in the background. Do not shut down right after playing. Check the time in `manifest.json`. |
| Wrong choice made | Local: unzip `backups\<area>_<time>.zip` into the local folder. Cloud: unzip `history\<area>_<time>.zip` into `data\<area>\`, then click *Download*. |
| Exe does not start | .NET Framework 4.8 missing (rare). Install it from Microsoft. |

## Notes for developers

* C# 5 only (old csc): no `$""`, no `?.`, no `nameof`.
* Add more areas in the `Engine` constructor (`Sets`). Not synced on purpose:
  `config.toml` (graphics, device specific), `cache`, `layout`, `chat`,
  `camera`, `saved-variables.lua`.
* Tools that run inside the Claude desktop app (an MSIX package) only write
  virtualized copies to `%APPDATA%` and `%LOCALAPPDATA%`. Let the user run
  setup and syncs themselves.
* Test the engine without UI by compiling `src/Engine.cs` with a small console
  `Main` that sets `Settings` to temporary folders and answers `AskConflict`
  and `Confirm` automatically.
