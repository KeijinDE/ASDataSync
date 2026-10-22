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
| `src/Setup.cs` | Read only cloud detection, path checks and first sync advice |
| `src/Wizard.cs` | Four step setup guide, visible path checks and Explorer actions |
| `src/App.cs` | Window logic, conflict dialog, `--play` mode, desktop shortcut, log file |
| `src/Theme.cs`, `src/Look.cs` | Look since 1.4.0 (see `docs/DESIGN.md`), design preview mode |
| `build.ps1` | Compiles with `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (C# 5, .NET Framework 4.x). Optional `-DeployTo <folder>` |
| `AzerothSync.exe` | Build output, about 35 KB, no installation |

An older PowerShell script `AzerothSync.ps1` (modes `run`, `pull`, `push`,
`status`, `install`) is **deprecated** since 1.3.0. It uses the old format 1
fingerprints and none of the safety checks; do not use it next to the exe.
The exe reads its old `state.json` values and converts them.

## Paths

| What | Default |
|---|---|
| Game | `AzerothSurvivors.exe` next to `AzerothSync.exe` if present (always wins), otherwise the setting, otherwise `C:\Games\AzerothSurvivors\AzerothSurvivors.exe` |
| Save game | `%APPDATA%\wenilla-rogue\` (`Saves\*.sav`, `characters.txt`, `characters.before-import.txt`, `language.txt`) |
| Game config | setting, otherwise `<game folder>\benilla-config\` if it exists, otherwise `C:\Git\wenilla\benilla-config` (contains `AddOns\`, `saved\`, `config.toml`, `layout\`, `chat\`, `camera\`, `cache\`) |
| Sync folder | the folder containing `AzerothSync.exe`, if it already holds `manifest.json` or `data\`; otherwise must be chosen |
| Per PC settings | `%LOCALAPPDATA%\AzerothSync\settings.ini` (keys `GamePath`, `CloudDir`, `SaveDir`, `ConfigDir`, `SetupDone`) |
| Per PC sync memory | `%LOCALAPPDATA%\AzerothSync\state.json` |
| Local backups | `%LOCALAPPDATA%\AzerothSync\backups\<area>_<yyyy-MM-dd_HH-mm-ss-fff>_<PC>.zip` (last 20 per area) |
| Log | `%LOCALAPPDATA%\AzerothSync\log.txt` (about 1 MB, previous one `log.old.txt`) |

Settings order: `settings.ini`, else `config.json` from the old script, else defaults.

## Sync folder layout

```
<sync folder>\
  AzerothSync.exe
  HANDOVER.md             this file (optional copy)
  manifest.json           {"Format": 2, "Device", "Time", "Areas": {"save": {"Hash", "Files", "Bytes"}, ...}} of the last upload
  data\save\              save game
  data\addons\            content of benilla-config\AddOns
  data\saved\             content of benilla-config\saved (SavedVariables)
  history\                cloud backups before each upload (<area>_<time>.zip, last 10 per area)
```

Very old layout: the save game was directly in `data\`. It is moved to
`data\save\` automatically.

## Logic

**Before anything changes,** all paths are validated (`Engine.Validate`):
local areas, sync folder and the AzerothSync data folder must not be equal or
nested, local areas must not be drive roots or general Windows folders, and
junctions or symbolic links are refused (cloud placeholders such as OneDrive
files are fine).

Per area there is a SHA256 fingerprint over all files (format 2): lines
`relative path, invariant lower case:FILE SHA256`, sorted ordinal, joined with
`\n`, UTF8. `NONE` means the folder is missing, `EMPTY` means it has no files.
`state.json` stores per area the fingerprint of the last sync on this PC with
the prefix `2:`:
`{"Hashes": {"save": "2:...", "addons": "2:...", "saved": "2:..."}, "LastSync": "...", "Device": "..."}`.
Values without prefix are format 1 (culture dependent, from 1.2.0 and the
PowerShell script); they are recognized by computing the format 1 fingerprint
of both sides and mapped, so the switch causes no false conflicts. The old
format `{"LastHash": "..."}` counts as `save`.

The manifest stores per area the fingerprint of the last upload. A cloud area
that differs from it is incomplete or still syncing.

**Download, per area** (Play and the automatic part):
* cloud empty: nothing
* local equals cloud: only update memory
* cloud differs from the manifest fingerprint: leave it, report "incomplete or still syncing" (not with the Download button)
* local empty or unchanged since last sync: take cloud
* cloud unchanged since last sync: local is newer, keep
* otherwise: conflict

**Upload, per area:**
* local empty: nothing
* local equals cloud: only update memory (and record it in the manifest if missing)
* cloud empty or unchanged since last sync: upload, but only if the cloud is still unchanged right before writing
* otherwise: conflict

`Pull`, `Push` and `Play` return true only if everything that should happen
did happen. In `--play` mode the window closes only then.

**Conflicts** are collected and asked **once** for all areas: *Use cloud*,
*Keep local* or *Cancel*. On download, *Keep local* stores the cloud
fingerprint as known, so the next upload overwrites the cloud without asking.

**Download / Upload buttons** force the direction: one confirmation listing all
differing areas, no conflict questions.

**Mirroring:** the source is copied into `<target>.azsync-new`, its
fingerprint is checked against the one from the decision (a source that
changed meanwhile aborts with nothing replaced), local only files such as
`language.txt` are carried over, then the target is renamed to
`<target>.azsync-old`, the new folder takes its place and the old one is
deleted. If the target is in use, nothing is replaced. A crash in between is
repaired on the next download or upload (old folder restored or leftovers
removed).

**Safety:** only one AzerothSync runs per Windows user (mutex). While a
process with the game's executable name runs, nothing is downloaded or
uploaded; this is checked again right before files are replaced. Every run
works on a copy of the settings, the settings are locked in the window while
it runs. `Status` only reads. If the game config folder is missing on a PC,
AddOns and addon settings are skipped with a warning. Dialogs default to the
safe choice. `state.json`, `settings.ini` and `manifest.json` are written via
a temporary file; an unreadable `state.json` is kept as
`state.json.broken-<time>` and reported.

Not handled (known limits): a completely emptied area is not uploaded (an
empty local area is treated as "nothing there"); two PCs playing offline at
the same time end in a conflict question, not in a merge.

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
| `does not match the last upload yet (incomplete or still syncing)` | The cloud app has not finished downloading the other PC's upload. Wait and try again. If you changed the cloud folder by hand on purpose, use *Download*. |
| `the cloud changed during this sync, NOT uploaded` | Another PC uploaded at the same time. Sync again and decide in the conflict dialog. |
| `The source changed while copying` | Files changed during the copy (cloud app still writing). Nothing was replaced, sync again. |
| `The folder is in use and was not replaced` | Explorer, an editor or the game holds a file in that folder. Close it and try again. |
| `... overlap ...`, `drive root`, `general Windows folder`, `Linked folders` | A folder setting is dangerous. Fix it in the window. |
| `AzerothSync is already running` | Another window is open, use that one. |
| Exe does not start | .NET Framework 4.8 missing (rare). Install it from Microsoft. |
| Anything else | Look at `%LOCALAPPDATA%\AzerothSync\log.txt`, it contains the full error. |

## Notes for developers

* C# 5 only (old csc): no `$""`, no `?.`, no `nameof`.
* Add more areas in the `Engine` constructor (`Sets`). Not synced on purpose:
  `config.toml` (graphics, device specific), `cache`, `layout`, `chat`,
  `camera`, `saved-variables.lua`.
* Tools that run inside the Claude desktop app (an MSIX package) only write
  virtualized copies to `%APPDATA%` and `%LOCALAPPDATA%`. Let the user run
  setup and syncs themselves.
* Tests: `powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1`
  compiles `src/Engine.cs` with `tests/EngineTests.cs` and runs normal flows
  and the safety cases from the review in temporary folders. `Engine.BeforeApply`
  lets tests change files between decision and copy.
* The PowerShell script `AzerothSync.ps1` is deprecated. It does not know
  format 2 fingerprints and would ask conflict questions after the exe ran.

## Setup since 1.5.0

`Setup.cs` never writes files. `FindClouds` detects cloud client folders;
`CheckGame`, `CheckCloud`, `CheckSave` and `CheckConfig` explain the paths.
`InspectCloud` reads the cloud manifest. `AdviseFirstSync` recommends upload,
download, a direction choice, no action or returning to the cloud step.
It compares every available area (save game, AddOns, addon settings), returns
AlreadyInSync when they match and lists differing labels in FirstSyncAdvice.Areas.
The engine remains responsible for validating paths and confirming replacements.
FindClouds() caches detection per process; Search again uses FindClouds(true).
The window warms detection on a background thread after Shown. Cloud path checks
show Checking… until detection and background inspection finish. The cloud step
keeps its candidate list across redraws; Search again explicitly refreshes it.
While scanning, Looking for cloud apps… is shown, candidates and Search again
are disabled, and Choose another folder remains usable. Sequence numbers discard
older scan and inspection results, including after path changes or a busy sync.
Neither detection nor inspection starts while busy. Preview remains sample only.

`Wizard.cs` presents Game, Cloud, First sync and Done in the main window.
The guide opens when `SetupDone` is false, including when launched with
`--play`; it never launches the game automatically after setup. Existing
settings with a cloud folder but no SetupDone key are treated as configured.
Finish stores `SetupDone=1`. Closing the guide or window stores 1 if a cloud
folder is set, otherwise 0. The guide can always be reopened from the sidebar.
Settings are saved through the existing per PC settings path.

All sync buttons use `Run` and its settings snapshot, game checks and busy
lock. The wizard is disabled during work and shows progress. After completion
it refreshes advice. Immediately before a wizard transfer, advice is recomputed;
a changed Fingerprint refreshes step 3 with a notice and does not start work.
All advice hashing runs on a background thread with a cloned settings snapshot.
Step 3 caches results; entering the step, path changes, Check again and completed
syncs request new advice. Advanced only redraws. Sequence numbers discard results
after leaving the guide, closing the window or starting a newer check. While
checking, the guide shows Checking your files and no transfer buttons. Checks
never start while a sync is busy. Sidebar navigation resets wizardActive, so
completed runs only refresh a visible guide and successful --play runs still close.
FixConfigFolder shows a direct config picker and skipped areas. Next requires
an explicit Continue without AddOns. Other states also warn about skipped areas.
RetryLater shows its title and message, only Check again, and disables Next.
Recommended downloads/uploads call Pull(false)/Push(false), retaining completeness
and conflict checks. Only the two explicit AskWhichIsNewer choices use force.
Except for FixConfigFolder and RetryLater, Next is enabled for AlreadyInSync, NothingYet or a successful wizard transfer,
even if differences remain; those areas stay visible. Failed or cancelled work
does not unlock Next. Changing a path clears the recorded wizard success.
Path checks run on Leave, after choosing a path, after runs or after a 400 ms
input pause, rather than on each keystroke. The first PC uploads; other PCs download after their cloud
app finishes transferring. The installer default config location is
`C:\Git\wenilla\benilla-config`.

The design preview uses sample path checks, cloud candidates and first sync
advice. It never probes those sample folders or loads real settings.
`AzerothSync.DesignPreview.exe --capture` renders the overview, Paths and two
wizard steps to PNG files under `docs`, including minimum window size examples.
UI tests cover all advice actions, busy locking and setup close behaviour.
