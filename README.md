# AzerothSync

Play Azeroth Survivors on more than one Windows PC and keep your progress.
AzerothSync copies your save game, your AddOns and your addon settings through
a folder that your cloud drive (Google Drive, OneDrive, Dropbox, ...) already
keeps in sync between your computers.

> Unofficial fan tool. Not affiliated with the developers of Azeroth Survivors.

## What it does

* **Play** button (or desktop shortcut): downloads the newest state from the
  sync folder, starts the game, waits until you close it and uploads your
  progress.
* Syncs three areas separately:

  | Area | Local folder | In the sync folder |
  |---|---|---|
  | Save game | `%APPDATA%\wenilla-rogue` (without `language.txt`) | `data\save` |
  | AddOns | `<game folder>\benilla-config\AddOns` | `data\addons` |
  | Addon settings | `<game folder>\benilla-config\saved` | `data\saved` |

* Backs up whatever it overwrites as a ZIP file, locally and in the cloud.
* Asks **one** question if both sides changed, never one per file or area.

It does **not merge** save games. The save files are encrypted, so one complete
state always wins. Do not play on two PCs at the same time.

## Requirements

* Windows 10 or 11 (uses .NET Framework 4, which is part of Windows)
* A cloud drive that shows up as a normal folder on every PC

## Setup

1. Download `AzerothSync.exe` from the [releases](../../releases) and put it into a
   folder inside your cloud drive, for example `Google Drive\AzerothSync`.
   That folder becomes your sync folder.
2. Start `AzerothSync.exe` and check the four paths at the top. Use `...` to
   change them:
   * **Game**: your `AzerothSurvivors.exe`
   * **Sync folder**: the folder from step 1
   * **Save game folder**: normally `%APPDATA%\wenilla-rogue`
   * **Game config folder**: the `benilla-config` folder next to the game
3. Click **Upload** on the PC that has your current progress.
4. Click **Desktop shortcut**. From now on start the game with
   *Azeroth Survivors (Sync)* on your desktop.
5. On every other PC: open the same `AzerothSync.exe` from your cloud drive,
   check the paths, click **Desktop shortcut**, then play. The first time it
   will ask about a conflict, choose **Use cloud**.

Settings are stored per PC in `%LOCALAPPDATA%\AzerothSync\settings.ini`.

## Buttons

| Button | What happens |
|---|---|
| Play | Download (asks only on a real conflict), start the game, upload after closing |
| Download | You want the cloud state: one confirmation, then everything that differs is replaced |
| Upload | You want your local state in the cloud: one confirmation, then everything that differs is replaced |
| Status | Shows per area whether it is in sync, or which side is newer |
| Desktop shortcut | Creates a shortcut that runs `AzerothSync.exe --play` |

## How it decides

AzerothSync remembers a fingerprint (SHA256) of every area after each sync.
On the next sync it compares local, cloud and the remembered fingerprint:

* Only the cloud changed: download without asking.
* Only your PC changed: keep it, upload after playing.
* Both changed: one dialog for all affected areas: **Use cloud**, **Keep local** or **Cancel**.

Backups:

* Local backups before a download: `%LOCALAPPDATA%\AzerothSync\backups\` (last 20 per area)
* Cloud backups before an upload: `<sync folder>\history\` (last 10 per area)

To restore, unzip a backup into the matching folder.

## Troubleshooting

See [docs/HANDOVER.md](docs/HANDOVER.md) for technical details, error messages and fixes.

## Building

No IDE needed. The C# compiler of .NET Framework is part of Windows:

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Optional: `-DeployTo "<sync folder>"` copies the exe and the handover document there.

## License

MIT, see [LICENSE](LICENSE).
