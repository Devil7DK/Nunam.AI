# Adding a machine to the index

Each machine with files runs its own `Nunam.Indexer`. It reads files locally and writes straight to the homelab database; nothing is copied off the machine except extracted text, metadata and a scaled-down preview image.

## Prerequisites

- **.NET 10** — the SDK to publish (or build elsewhere and copy the output plus the runtime).
- **Visual C++ 2015–2022 x64 runtime** (Windows), for OCR: [vc_redist.x64.exe](https://aka.ms/vs/17/release/vc_redist.x64.exe). Most desktops already have it; a fresh install doesn't, and then scanned pages and photos are indexed without their text. The installer warns when it's missing.
- **ffmpeg** with `ffprobe`, for video metadata and frames. Windows: `winget install Gyan.FFmpeg`. Linux: the distro package. Without it videos are still indexed by name.
- **Network access** to the homelab database port (5432 by default).
- **The Syncfusion license key** (Community license, Document SDK, version 35). Bumping the Syncfusion packages to a new major version needs a new key.

Tesseract (OCR), PDFium and SkiaSharp come with the NuGet packages.

## Settings

Everything machine-specific goes in `appsettings.Local.json` next to the executable (on Linux, `/etc/nunam-indexer/appsettings.Local.json`) — start from `appsettings.Local.example.json` in `deploy/indexer/windows` or `deploy/indexer/linux`:

| Setting | |
|---|---|
| `ConnectionStrings:Postgres` | The machine's own database login: run `docker/add-indexer-login.sh <machine-name>` on the homelab, save the certificate it prints as `nunam-db.crt` in the install folder, and paste the connection string with that file's full path. Don't share one login between machines. |
| `Syncfusion:LicenseKey` | Without it documents are parsed in evaluation mode. |
| `Indexer:WatchPaths` | Folders to index, recursively. A whole drive works too (`/`, `C:\`); see `SkipSystemFolders`. |
| `Indexer:MachineName` | Name shown in the UI; defaults to the hostname. The machine is identified by hostname, so renaming here keeps its files. |
| `Indexer:ExcludePatterns` | Defaults skip `.git`, `node_modules`, `bin`, `obj` and similar. Use `/` as the separator on every OS. Patterns match only below the watch path, so a watch path that is itself a `bin` folder is still indexed. |
| `Indexer:SkipSystemFolders` | `true` by default: inside a watch path, skip the operating system and installed programs (on Linux `/usr`, `/etc`, `/var`, `/opt`, `/proc` and the like; on Windows `Windows`, `Program Files`, `ProgramData`, the recycle bin, the page file), app data in home folders (hidden folders such as `~/.config` and `~/.local`, and `AppData` on Windows), and package caches (`.cache`, `.nuget/packages`, `.venv`). A watch path inside one of those, such as `/opt/wiki`, is still indexed. |
| `Indexer:RemoteManagement` | `false` by default. `true` lets admins and the machine's owners change some settings and restart, pause or resume the indexer from the **Machines** page. Only this file can turn it on. |
| `Indexer:FFmpegBinaryFolder` | Only if ffmpeg isn't on `PATH`. |
| `Indexer:OcrLanguage` | `eng` by default. For more, put the `.traineddata` files (e.g. `tam`) together with `eng.traineddata` in a folder, set `Indexer:OcrTessDataPath` to it, and use `eng+tam`. |

The file holds secrets: on Windows only Administrators, SYSTEM and the service can read it, on Linux only root and the service.

## Windows

Build the installer on a Linux machine with the .NET SDK and msitools (`dnf install msitools`, `apt install wixl`):

```bash
deploy/indexer/windows/build-msi.sh   # artifacts/packages/nunam-indexer-<version>-x64.msi
```

Run the MSI on the Windows machine. It installs the indexer to `C:\Program Files\Nunam\Indexer` with its own .NET runtime, and registers the `Nunam.Indexer` service. The service stays stopped until the settings are filled in. Then open Start → **Configure Nunam Indexer**. It asks for administrator rights, opens `appsettings.Local.json` in Notepad and waits for you to save and close it. It then sets the service to delayed automatic start that restarts on failure, grants read access to the watch paths and starts it. Run it again after changing the watch paths. Installing a newer MSI keeps the settings and starts the service again on its own. OCR needs the [Visual C++ 2015–2022 x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe); video needs ffmpeg on `PATH` or `Indexer:FFmpegBinaryFolder`.

The service runs as its own virtual account, `NT SERVICE\Nunam.Indexer`, which can read only what it is given. A subfolder that blocks inherited permissions stays unreadable, and its files are logged as failed. With a whole drive or `C:\Users` as the watch path, each profile's own folders (Documents, Desktop, Pictures, …) are granted instead of the drive. `AppData` and hidden folders are left out, and the rest of the drive is readable to the Users group anyway.

The account can't reach network shares. To index a share, run the script from an elevated PowerShell with `-Account DOMAIN\user` for an account that can; it asks for the password:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Program Files\Nunam\Indexer\Configure-Indexer.ps1" -Account DOMAIN\user
```

An upgrade puts the service back on the virtual account, so run that again after one. `-Account LocalSystem` reads everything without grants, but then a flaw in a file parser would control the whole machine.

Warnings and errors go to Event Viewer → Windows Logs → Application.

## Linux

Build the packages on a Linux machine with the .NET SDK and Docker:

```bash
deploy/indexer/linux/build-packages.sh   # artifacts/packages/nunam-indexer_<version>_amd64.deb and nunam-indexer-<version>-1.x86_64.rpm
```

Install the one that fits: `sudo apt install ./nunam-indexer_0.1.0_amd64.deb` (Debian 12 or later, Ubuntu) or `sudo dnf install ./nunam-indexer-0.1.0-1.x86_64.rpm` (Fedora). The .NET runtime is included; ffmpeg (video) and leptonica (OCR) come as recommended packages. Then:

1. Fill in `/etc/nunam-indexer/appsettings.Local.json`, save the database certificate as `/etc/nunam-indexer/nunam-db.crt`, and use that path in the connection string. The file is readable only by root and the service.
2. The service runs as the `nunam` system user, which can't read anyone's home folder. Let it read each watch path:
   ```bash
   sudo setfacl -m u:nunam:x ~                                  # pass through the home folder, not list it
   sudo setfacl -R -m u:nunam:rX -m d:u:nunam:rX ~/Documents     # read everything in it, now and later
   ```
   With `/` as the watch path, do this for each home folder that should be indexed; a home folder the service can't enter is skipped.
3. `sudo systemctl enable --now nunam-indexer`. Logs: `journalctl -u nunam-indexer`.

Installing a newer package keeps the settings and restarts the service. The service is sandboxed: it sees the filesystem read-only and writes only to its own `/tmp`.

## What happens next

On start the indexer registers the machine, scans every watch path and queues each new or changed file; files deleted while it was off, and files a removed watch path or a new exclude pattern no longer covers, are dropped. A watch path that is missing at start (an unplugged drive) keeps its entries. After that it follows changes as they happen. Files become keyword-searchable as soon as they're parsed, and searchable by meaning once the AI worker has embedded them — the status line in the UI shows how far it has got.

Only admins see the new machine at first. To let others search it, open *Index status* in the UI and use *Share* on its row; make the machine's main user an owner so they can share it themselves.

## Managing it from the web UI

**Machines** in the UI's header lists every machine you can see. Each indexer reports to the database every 30 seconds, and sends its log entries every few seconds. A machine that hasn't reported for 90 seconds shows as offline. Everyone who can see a machine sees whether it's online, its watch paths and its job queue. Admins and the machine's owners also see its logs (kept 7 days) and the settings in effect, with where each value came from. Passwords and the license key are never sent.

With `Indexer:RemoteManagement` on, admins and owners can also do these from the same page:

- **Pause and resume parsing.** Changes are still queued while paused. Restarting the service resumes.
- **Restart and rescan.** Only the workers restart, so the Windows or systemd service keeps running.
- **Retry failed files.**
- **Change settings:** machine name, watch paths, exclude patterns, skipping system folders, size limits and OCR. A changed setting overrides the machine's own value until it's cleared, and saving restarts the indexer.

Settings that run programs or load data files (ffmpeg, tessdata, the connection string) can only be changed on the machine. On Windows, a new watch path also needs *Configure Nunam Indexer* run on the machine, so the service account may read it.

All of this goes through the database: the indexer opens no port.

## Removing a machine

Uninstall it (Windows: *Settings → Apps → Nunam Indexer*; Linux: `sudo apt remove nunam-indexer` or `sudo dnf remove nunam-indexer`). On Windows `appsettings.Local.json` stays in the install folder; also remove the read grants (`icacls <watch path> /remove "NT SERVICE\Nunam.Indexer"` for each). Then delete its rows; files and jobs go with the machine:

```sql
DELETE FROM machines WHERE name = 'laptop';
```
