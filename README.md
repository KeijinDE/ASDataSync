# AzerothSync

Play Azeroth Survivors on more than one Windows PC and keep your progress.
AzerothSync copies your save game, your AddOns and your addon settings through
a folder that your cloud drive (Google Drive, OneDrive, Dropbox, ...) already
keeps in sync between your computers.

> Unofficial fan tool. Not affiliated with the developers of Azeroth Survivors.

## Quick start

1. Install **Google Drive for desktop**, **OneDrive** or **Dropbox** on every
   PC and sign in. Wait until its folder or drive appears in Windows Explorer.
   For Google, use [Google Drive for desktop](https://www.google.com/drive/download/).
2. Download `AzerothSync.exe` from the [releases](../../releases) and put it in
   your game folder, next to `AzerothSurvivors.exe`.
3. Start it on the PC with your latest progress. Follow the four setup steps:
   **Game**, **Cloud**, **First sync**, **Done**. Choose a folder inside your cloud
   drive, for example `G:\My Drive\AzerothSync`. A new folder is created on upload.
4. On this first PC, choose **Upload this PC's progress**. Wait until your
   cloud app has finished uploading.
5. On every other PC, repeat setup with the **same cloud folder** and choose
   **Download to this PC**. Setup compares save game, AddOns and addon settings and lists the differing
   areas. If both sides hold different progress, it shows **Keep this PC's
   progress (upload)** and **Use the cloud progress (download)** instead:
   check the displayed times and pick one. One complete state wins, the other
   is backed up. If everything already matches, just click **Next**.
6. Create the desktop shortcut in the last step. From now on, always start
   with **Azeroth Survivors (Sync)**. Close the game and let your cloud app
   finish transferring before switching PCs.

![Setup guide, step 2: choose your cloud folder](docs/design/wizard-cloud.png)

![Setup guide, step 3: first sync](docs/design/wizard-first-sync.png)

> **Steam Deck / Linux:** not supported yet. Google Drive for desktop does not
> exist for Linux, and the game runs inside Proton there. Support is being
> worked on.

## Which folder is which

| Folder | What it is for | Typical example |
|---|---|---|
| Game | The executable that starts the game, usually found automatically | `C:\Games\AzerothSurvivors\AzerothSurvivors.exe` |
| Sync folder | The shared folder inside your cloud drive. Usually the only path you choose. Use the same folder on all PCs; drive letters can differ. | `G:\My Drive\AzerothSync` |
| Save game folder | This PC's characters and progress, found automatically | `%APPDATA%\wenilla-rogue` |
| Game config folder | This PC's AddOns and addon settings, found automatically | `C:\Git\wenilla\benilla-config` (the game installer's default) |

**Paths** explains and checks each path. **Choose...** changes it; **Open**
opens the folder in Explorer. A green check means the path looks right on this
PC. It does not confirm that the cloud app has finished transferring.

## FAQ

**Does AzerothSync log into my Google account?**

No. Your cloud app signs in and transfers files. AzerothSync only copies files
into the local folder provided by that app. It never asks for your password.

**Which PC do I set up first?**

The PC with your latest progress. Upload there first. Then download on your
other PCs. Setup checks all three areas, including AddOns and addon settings,
and asks which state to keep when both sides differ. If everything already
matches or there is nothing yet, continue with Next. After a successful sync,
you can also continue; any remaining differences stay visible.

**Setup says the game config folder is missing.**

Choose **Choose game config folder** in First sync and select `benilla-config`.
The guide lists areas not synced on this PC. If you intentionally only want
save games, click **Continue without AddOns** before Next.

**Setup says files are busy or keeps checking.**

Large AddOns folders and streamed cloud files can take time to check. The window
stays responsive. If files cannot be read, wait for the cloud app to finish,
then click **Check again**. Sync and Next stay unavailable until the check succeeds.

**Can I use the Google Drive website?**

The website alone does not work. Install Google Drive for desktop and sign in
so that your drive appears as a folder or drive letter in Windows Explorer.

**Where is benilla-config?**

`C:\Git\wenilla\benilla-config` is the game installer's default config
location. It is not a developer-only path. Some installations put
`benilla-config` next to the game instead. It contains `AddOns` and `saved`.

![AzerothSync window with sample data](docs/screenshot.png)

## What it does

* **Play** button (or desktop shortcut): downloads the newest state from the
  sync folder, starts the game, waits until you close it and uploads your
  progress.
* Syncs three areas separately:

  | Area | Local folder | In the sync folder |
  |---|---|---|
  | Save game | `%APPDATA%\wenilla-rogue` (without `language.txt`) | `data\save` |
  | AddOns | `<game config folder>\AddOns` | `data\addons` |
  | Addon settings | `<game config folder>\saved` | `data\saved` |

  The game config folder is the game's `benilla-config` folder, see
  [How paths are found](#how-paths-are-found).

* Backs up whatever it overwrites as a ZIP file, locally and in the cloud.
* Asks **one** question if both sides changed, never one per file or area.

It does **not merge** save games. The save files are encrypted, so one complete
state always wins. Do not play on two PCs at the same time.

## Requirements

* Windows 10 or 11 (uses .NET Framework 4, which is part of Windows)
* A cloud drive that shows up as a normal folder on every PC

Settings are stored per PC in `%LOCALAPPDATA%\AzerothSync\settings.ini`.
You can reopen the four steps at any time with **Setup guide** in the sidebar.

## How paths are found

| Path | How AzerothSync finds it |
|---|---|
| Game | `AzerothSurvivors.exe` next to `AzerothSync.exe` always wins. Otherwise the saved setting, otherwise `C:\Games\AzerothSurvivors\AzerothSurvivors.exe`. |
| Sync folder | The saved setting. If none is saved and `AzerothSync.exe` sits in a folder that already contains synced data (`manifest.json` or `data\`), that folder. Otherwise you choose it. |
| Save game folder | The saved setting, otherwise `%APPDATA%\wenilla-rogue`. |
| Game config folder | The saved setting if the folder exists. Otherwise `benilla-config` in the game folder, otherwise `C:\Git\wenilla\benilla-config`. If none of them exists, choose it, or AddOns and addon settings are skipped. |

Everything you change in the window is saved for this PC only.

## Buttons

| Button | What happens |
|---|---|
| Play | Download (asks only on a real conflict), start the game, upload after closing |
| Download | You want the cloud state: one confirmation, then everything that differs is replaced |
| Upload | You want your local state in the cloud: one confirmation, then everything that differs is replaced |
| Check status | Shows per area whether it is in sync, or which side is newer |
| Desktop shortcut (Paths) | Creates a shortcut that runs `AzerothSync.exe --play` |

The **Log** view shows the full technical log of the session. The look is
described in [docs/DESIGN.md](docs/DESIGN.md).

## How it decides

AzerothSync remembers a fingerprint (SHA256) of every area after each sync.
On the next sync it compares local, cloud and the remembered fingerprint:

* Only the cloud changed: download without asking.
* Only your PC changed: keep it, upload after playing.
* Both changed: one dialog for all affected areas: **Use cloud**, **Keep local** or **Cancel**.

Backups:

* Local backups before a download: `%LOCALAPPDATA%\AzerothSync\backups\` (last 20 per area)
* Cloud backups before an upload: `<sync folder>\history\` (last 10 per area)

To restore, delete the content of the matching folder (keep `language.txt`
in the save game folder) and unzip the backup into it, so no newer files
stay behind. After restoring a cloud backup into `data\<area>`, click
**Download** on each PC.

Safety: AzerothSync only takes a cloud state that matches the last upload
(a copy that is still syncing is left alone), copies into a temporary folder
and swaps it in only after checking it, and refuses overlapping or linked
folders. Details in [docs/HANDOVER.md](docs/HANDOVER.md).

## Antivirus and SmartScreen

AzerothSync is a small, new and unsigned program, which some scanners
distrust on principle. Being open about it:

* [VirusTotal scan of v1.1.1](https://www.virustotal.com/gui/file/5083a8826b6b44d80ab43fd6b9d6b6237f1765d99666e40c3f2ef41ccaa2c946):
  **2 of 70** scanners that analysed it flag it, **SecureAge APEX** ("Malicious") and **Trapmine**
  ("Suspicious.low.ml.score"). Both are verdicts from machine learning models,
  not a match for known malware. All other scanners, including Microsoft
  Defender, find nothing.
* Windows SmartScreen may show "Windows protected your PC" on the first start,
  because the exe has no code signature. Click **More info**, then **Run anyway**.

What the program does is fully visible in [`src/`](src): it reads and copies
files in the folders listed above, writes ZIP backups, starts the game and
creates a desktop shortcut on request. No network access of its own, no
installation, no administrator rights. If you prefer, build the exe yourself
from the source (see [Building](#building)).

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
