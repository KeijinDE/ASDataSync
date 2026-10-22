# Changelog

## 1.5.0 (2026-10-22)

Easier setup, based on tester feedback.

* **Setup guide** on the first start, four steps: Game, Cloud, First sync,
  Done. Reopen it any time with **Setup guide** in the sidebar.
* **Cloud folder:** finds Google Drive for desktop, OneDrive and Dropbox and
  suggests a folder with one click. Explains that AzerothSync never logs into
  your cloud account: your cloud app syncs a normal folder, AzerothSync only
  copies files into it.
* **First sync:** compares save game, AddOns and addon settings and tells you
  what to do on this PC: upload, download, or choose which progress to keep.
  It checks again right before copying, in case another PC uploaded meanwhile.
* Clear messages when the game config folder (`benilla-config`) is missing or
  files are still busy because the cloud app is syncing.
* **Paths page:** every path explains what it is for, shows whether it was
  found, and has an **Open** button. The sync folder is marked as the one you
  usually need to choose.
* README: new Quick start and FAQ.
* Existing setups keep working, the guide is not forced on them.
* Not yet: Steam Deck / Linux support is being worked on.

## 1.4.0

* New look: dark window with bronze outlines and gold accents, a navigation
  with Play & Sync, Paths and Log, a clear Play button and a list of the three
  data areas with their state. Designed with Codex, see `docs/DESIGN.md`.
* Dialogs in the same style; Enter and Escape always mean Cancel or No.
* Link to the GitHub page in the window.
* Sync behaviour unchanged from 1.3.0.

## 1.3.0

Safety release, based on an external code review.

* Downloads only take a cloud state that matches the last upload. The
  manifest now records a fingerprint per area; a cloud copy that is still
  syncing or incomplete is left alone ("try again in a moment"). The
  Download button can still take it on purpose, after one confirmation.
* Copies go to a staging folder, are verified and only then swapped in. If
  anything fails, the old folder stays complete. An interrupted swap is
  repaired on the next start.
* Before uploading, the cloud is checked again; if another PC uploaded in the
  meantime, nothing is overwritten.
* Overlapping folders, drive roots, general Windows folders and linked
  folders (junctions, symbolic links) are refused before anything changes.
* Settings are locked while a sync runs, every run works on its own copy.
* Play keeps the window open and says so if the progress was not uploaded
  completely.
* Only one AzerothSync runs at a time; the game is checked again right before
  files are replaced.
* Backups get unique names (milliseconds and PC name) and are written
  completely before old ones are removed.
* Fingerprints no longer depend on the Windows language. Existing sync memory
  is converted automatically, without false conflicts.
* Status only reads, it no longer creates or converts anything.
* Sync memory and settings are written safely; an unreadable sync memory is
  reported and kept. Local log file `%LOCALAPPDATA%\AzerothSync\log.txt`.
* Enter in dialogs always means the safe choice (No or Cancel).
* Sizes below 1 MB are shown in KB.
* Engine tests in `tests/`.

## 1.2.0

* Progress bar with files and MB for checking, downloading and uploading.
  The cloud drive fetches files while they are read, so the download
  progress reflects the real transfer.
* Copied files keep their original modification time.

## 1.1.1

* A saved game config folder that no longer exists falls back to
  `benilla-config` in the game folder or `C:\Git\wenilla\benilla-config`,
  whichever exists.
* Windows file details (version, product, publisher, license) and an
  application manifest (`asInvoker`, never asks for administrator rights).
* Desktop shortcut created through the native Shell API instead of
  Windows Script Host.

## 1.1.0

* If `AzerothSync.exe` sits in the game folder, it uses that game automatically.
* Game config folder: taken from the game folder if `benilla-config` exists
  there, otherwise `C:\Git\wenilla\benilla-config` is suggested.
* Hints on start when the game or the game config folder is not found.
* Version shown in the window title.

## 1.0.0

* First release: sync of save game, AddOns and addon settings through a cloud
  folder, Play mode, one conflict question, backups, desktop shortcut.
