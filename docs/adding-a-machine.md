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

Everything machine-specific goes in `appsettings.Local.json` next to the executable — start from `deploy/indexer/appsettings.Local.example.json`:

| Setting | |
|---|---|
| `ConnectionStrings:Postgres` | The machine's own database login: run `docker/add-indexer-login.sh <machine-name>` on the homelab, save the certificate it prints as `nunam-db.crt` in the install folder, and paste the connection string with that file's full path. Don't share one login between machines. |
| `Syncfusion:LicenseKey` | Without it documents are parsed in evaluation mode. |
| `Indexer:WatchPaths` | Folders to index, recursively. A whole drive works too (`/`, `C:\`); see `SkipSystemFolders`. |
| `Indexer:MachineName` | Name shown in the UI; defaults to the hostname. The machine is identified by hostname, so renaming here keeps its files. |
| `Indexer:ExcludePatterns` | Defaults skip `.git`, `node_modules`, `bin`, `obj` and similar. Use `/` as the separator on every OS. Patterns match only below the watch path, so a watch path that is itself a `bin` folder is still indexed. |
| `Indexer:SkipSystemFolders` | `true` by default: inside a watch path, skip the operating system and installed programs (on Linux `/usr`, `/etc`, `/var`, `/opt`, `/proc` and the like; on Windows `Windows`, `Program Files`, `ProgramData`, the recycle bin, the page file), app data in home folders (hidden folders such as `~/.config` and `~/.local`, and `AppData` on Windows), and package caches (`.cache`, `.nuget/packages`, `.venv`). A watch path inside one of those, such as `/opt/wiki`, is still indexed. |
| `Indexer:FFmpegBinaryFolder` | Only if ffmpeg isn't on `PATH`. |
| `Indexer:OcrLanguage` | `eng` by default. For more, put the `.traineddata` files (e.g. `tam`) together with `eng.traineddata` in a folder, set `Indexer:OcrTessDataPath` to it, and use `eng+tam`. |

The file holds secrets; the Windows installer restricts it to Administrators and SYSTEM.

## Windows

From the repository root, in an elevated PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\indexer\install-windows.ps1
```

`-ExecutionPolicy Bypass` applies to that run only; Windows' default policy blocks unsigned scripts.

The first run publishes to `C:\Program Files\Nunam\Indexer`, creates `appsettings.Local.json` and opens it in Notepad. Fill it in and run the script again: it installs the `Nunam.Indexer` service (automatic, delayed start, restarts on failure) and starts it. Later runs update the binaries and keep the settings.

The service runs as its own virtual account, `NT SERVICE\Nunam.Indexer`, which can read only what it is given: each run grants it read access to every watch path in `appsettings.Local.json` (so after adding a watch path, run the script again). A subfolder that blocks inherited permissions stays unreadable, and its files are logged as failed. The account can't reach network shares; to index a share, install with `-Account DOMAIN\user` for an account that can. `-Account LocalSystem` reads everything without grants, but then a flaw in a file parser would control the whole machine.

Warnings and errors go to Event Viewer → Windows Logs → Application.

## Linux

See the commands at the top of `deploy/indexer/nunam-indexer.service`: publish to `/opt/nunam/indexer`, add `appsettings.Local.json` (mode 600), install and enable the unit. Logs: `journalctl -u nunam-indexer`.

## What happens next

On start the indexer registers the machine, scans every watch path and queues each new or changed file; files deleted while it was off, and files a removed watch path or a new exclude pattern no longer covers, are dropped. A watch path that is missing at start (an unplugged drive) keeps its entries. After that it follows changes as they happen. Files become keyword-searchable as soon as they're parsed, and searchable by meaning once the AI worker has embedded them — the status line in the UI shows how far it has got.

Only admins see the new machine at first. To let others search it, open *Index status* in the UI and use *Share* on its row; make the machine's main user an owner so they can share it themselves.

## Removing a machine

Stop and remove the service (`sc.exe delete Nunam.Indexer`) and, on Windows, the read grants (`icacls <watch path> /remove "NT SERVICE\Nunam.Indexer"` for each). Then delete its rows; files and jobs go with the machine:

```sql
DELETE FROM machines WHERE name = 'laptop';
```
