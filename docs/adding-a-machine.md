# Adding a machine to the index

Each machine with files runs its own `Nunam.Indexer`. It reads files locally and sends what it finds to the server's API; nothing is copied off the machine except extracted text, metadata and a scaled-down preview image.

## The quick way

As an admin, open **Machines** → **Add machine** in the web UI, optionally name the machine, and choose **Get the command**. It shows one command per system:

- **Windows**, in PowerShell opened as administrator: `& ([scriptblock]::Create((irm '<server>/api/agent/install.ps1'))) -Server '<server>' -Code '<code>'`
- **Linux** (Debian 12 or later, Ubuntu, Fedora): `curl -fsSL '<server>/api/agent/install.sh' | sudo sh -s -- --server '<server>' --code '<code>'`

Run it on the machine. It downloads the indexer from the server, installs it, trades the one-time code for the machine's own token, asks which account the service runs as (see below) and starts it. The dialog notices when the machine connects. With **Manage it from here** ticked (the default), set its watch paths on the machine's *Settings* tab; otherwise in `appsettings.Local.json` on the machine.

The code works once, for an hour. The command downloads the newest package the server has ([indexer packages](deployment.md)); if it has none, use the command under *Indexer already installed?* after installing the package by hand, as below.

**Reconnect** on a machine's page does the same for a machine that is already in Nunam: it gives the machine a new token and keeps its files. Use it after reinstalling the machine, or to update an indexer older than 0.4.

## Prerequisites

- **Tesseract**, to read text in photos and scanned PDFs. Windows: `winget install UB-Mannheim.TesseractOCR`. Linux: the packages recommend it. Without it those files are still indexed, without their text.
- **ffmpeg** with `ffprobe`, for video metadata and frames. Windows: `winget install Gyan.FFmpeg`. Linux: the distro package. Without it videos are still indexed by name.
- **Network access** to the web UI's address. Use HTTPS if anyone could watch the network: the machine's token goes with every request.

PDFium (PDF rendering) and SkiaSharp (images) come with the indexer.

## Settings

Everything machine-specific goes in `appsettings.Local.json` next to the executable (on Linux, `/etc/nunam-indexer/appsettings.Local.json`) — start from `appsettings.Local.example.json` in `deploy/indexer/windows` or `deploy/indexer/linux`:

| Setting | |
|---|---|
| `Server:Url`, `Server:Token` | The web UI's address and the machine's token. The install command fills them in; so does `Nunam.Indexer enroll --server <url> --code <code>` with a code from *Add machine*. Don't copy a token to another machine. |
| `Server:CertificateSha256` | Only for a server with a self-signed certificate: its SHA-256 in hex. That certificate is then accepted and no other. `enroll` takes it as `--certificate-sha256`. |
| `Indexer:WatchPaths` | Folders to index, recursively. A whole drive works too (`/`, `C:\`); see `SkipSystemFolders`. |
| `Indexer:MachineName` | Name shown in the UI; defaults to the hostname. The machine is identified by hostname, so renaming here keeps its files. |
| `Indexer:ExcludePatterns` | Defaults skip `.git`, `node_modules`, `bin`, `obj` and similar. Use `/` as the separator on every OS. Patterns match only below the watch path, so a watch path that is itself a `bin` folder is still indexed. |
| `Indexer:SkipSystemFolders` | `true` by default: inside a watch path, skip the operating system and installed programs (on Linux `/usr`, `/etc`, `/var`, `/opt`, `/proc` and the like; on Windows `Windows`, `Program Files`, `ProgramData`, the recycle bin, the page file), app data in home folders (hidden folders such as `~/.config` and `~/.local`, and `AppData` on Windows), and package caches (`.cache`, `.nuget/packages`, `.venv`). A watch path inside one of those, such as `/opt/wiki`, is still indexed. |
| `Indexer:RemoteManagement` | `false` by default. `true` lets admins and the machine's owners change some settings and restart, pause or resume the indexer from the **Machines** page. Only this file can turn it on; the install command does when *Manage it from here* is ticked. |
| `Indexer:FFmpegBinaryFolder` | Only if ffmpeg isn't on `PATH`. |
| `Indexer:OcrLanguage` | `eng` by default. Use `eng+tam` and the like for more; each language needs its Tesseract data (`apt install tesseract-ocr-tam`, `dnf install tesseract-langpack-tam`, or a ticked language in the Windows installer). `Indexer:OcrTessDataPath` points at a different tessdata folder. |
| `Indexer:OcrTesseractPath` | Only if `tesseract` isn't on `PATH` (or, on Windows, in `C:\Program Files\Tesseract-OCR`). |
| `Indexer:Parsers`, `OcrEngine`, `PdfRenderer`, `Commands`, `PluginFolder` | Which parser reads which extension, and other programs or plugins to read files with. See [extending the indexer](extending-the-indexer.md). |

The file holds the machine's token: on Windows only Administrators, SYSTEM and the service can read it, on Linux only root and the service.

## Windows

Build the installer on a Linux machine with the .NET SDK and msitools (`dnf install msitools`, `apt install wixl`):

```bash
deploy/indexer/windows/build-msi.sh   # artifacts/packages/nunam-indexer-<version>-x64.msi
```

The install command runs this MSI for you. By hand: run it on the Windows machine. It installs the indexer to `C:\Program Files\Nunam\Indexer` with its own .NET runtime, and registers the `Nunam.Indexer` service, which stays stopped until the machine is connected. Connect it in PowerShell as administrator with the command under *Indexer already installed?* in *Add machine*, then open Start → **Configure Nunam Indexer**. It asks for administrator rights and which account to run as (below). Without watch paths or remote management it opens `appsettings.Local.json` in Notepad and waits for you to save and close it. It then sets the service to delayed automatic start that restarts on failure, grants read access to the watch paths if needed, those set in the web UI included, and starts it. Run it again after changing the watch paths. Installing a newer MSI keeps the settings and starts the service again on its own. OCR needs Tesseract (the configure script warns when it can't find it); video needs ffmpeg on `PATH` or `Indexer:FFmpegBinaryFolder`.

The script asks which account the service runs as:

- **Its own account**, `NT SERVICE\Nunam.Indexer` (the default). It reads only what the script grants it. A subfolder that blocks inherited permissions stays unreadable, and its files are logged as failed. With a whole drive or `C:\Users` as the watch path, each profile's own folders (Documents, Desktop, Pictures, …) are granted instead of the drive. `AppData` and hidden folders are left out, and the rest of the drive is readable to the Users group anyway. It can't reach network shares.
- **Your account**. The indexer reads everything you can, network shares included, and nothing is granted. The script asks for your Windows password, checks it, and gives the account the *Log on as a service* right. When you change your password, run the script again and choose your account, or the service fails to start. A Windows Hello PIN won't do; it needs the account's password.
- **Another account**, `DOMAIN\user` or `.\user`, the same way. `LocalSystem` reads everything without a password, but then a flaw in a file parser would control the whole machine.

Upgrades keep the account. From a script, pass it instead of answering:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Program Files\Nunam\Indexer\Configure-Indexer.ps1" -Account DOMAIN\user -Credential (Get-Credential DOMAIN\user)
```

Warnings and errors go to Event Viewer → Windows Logs → Application.

## Linux

Build the packages on a Linux machine with the .NET SDK and Docker:

```bash
deploy/indexer/linux/build-packages.sh   # artifacts/packages/nunam-indexer_<version>_amd64.deb and nunam-indexer-<version>-1.x86_64.rpm
```

The install command installs the one that fits and does steps 1–3. By hand: `sudo apt install ./nunam-indexer_0.4.0_amd64.deb` (Debian 12 or later, Ubuntu) or `sudo dnf install ./nunam-indexer-0.4.0-1.x86_64.rpm` (Fedora). The .NET runtime is included; ffmpeg (video) and Tesseract with English (OCR) come as recommended packages. Then:

1. Connect it with the command under *Indexer already installed?* in *Add machine*. It writes the server's address and the machine's token to `/etc/nunam-indexer/appsettings.Local.json`, which only root and the service can read. Watch paths go there too, unless they are set in the web UI.
2. Choose the user it runs as. The install command asks when it runs in a terminal. `sudo nunam-indexer-account` asks, or name one directly:
   - **Yourself**, `sudo nunam-indexer-account $USER`. It reads everything you can, mounted shares included, and nothing needs granting. It still runs sandboxed, read-only.
   - **The `nunam` system user** (the default, `sudo nunam-indexer-account --default`). It can't read anyone's home folder, so let it read each watch path:
     ```bash
     sudo setfacl -m u:nunam:x ~                                  # pass through the home folder, not list it
     sudo setfacl -R -m u:nunam:rX -m d:u:nunam:rX ~/Documents     # read everything in it, now and later
     ```
     With `/` as the watch path, do this for each home folder that should be indexed; a home folder the service can't enter is skipped.

   The choice is a systemd drop-in, `/etc/systemd/system/nunam-indexer.service.d/account.conf`, so upgrades keep it. Root is refused.
3. `sudo systemctl enable --now nunam-indexer`. Logs: `journalctl -u nunam-indexer`.

Installing a newer package keeps the settings and restarts the service. The service is sandboxed: it sees the filesystem read-only and writes only to its own `/tmp`.

## What happens next

On start the indexer signs in to the server, scans every watch path and queues each new or changed file; files deleted while it was off, and files a removed watch path or a new exclude pattern no longer covers, are dropped. A watch path that is missing at start (an unplugged drive) keeps its entries. After that it follows changes as they happen. Files become keyword-searchable as soon as they're parsed, and searchable by meaning once the AI worker has embedded them — the status line in the UI shows how far it has got.

Only admins see the new machine at first. To let others search it, open it under *Machines* and use *Share*; make the machine's main user an owner so they can share it themselves.

## Managing it from the web UI

**Machines** in the UI's header lists every machine you can see. Each indexer reports to the server every 30 seconds, and sends its log entries every few seconds. A machine that hasn't reported for 90 seconds shows as offline. Everyone who can see a machine sees whether it's online, its watch paths and its job queue. Admins and the machine's owners also see its logs (kept 7 days) and the settings in effect, with where each value came from. Passwords are never sent.

With `Indexer:RemoteManagement` on, admins and owners can also do these from the same page:

- **Pause and resume parsing.** Changes are still queued while paused. Restarting the service resumes.
- **Restart and rescan.** Only the workers restart, so the Windows or systemd service keeps running.
- **Retry failed files.**
- **Change settings:** machine name, watch paths, exclude patterns, skipping system folders, size limits, OCR, and which built-in parser reads each extension. A changed setting overrides the machine's own value until it's cleared, and saving restarts the indexer.

Settings that run programs or load code or data files (ffmpeg, Tesseract, commands, plugins, tessdata, the server and token) can only be changed on the machine. On Windows, a new watch path also needs *Configure Nunam Indexer* run on the machine, so the service account may read it.

All of this goes through the server's API, which the indexer polls every few seconds: the indexer opens no port.

## Removing a machine

As an admin, open the machine under **Machines** and choose **Remove**. Its files, jobs and logs go with it, and its token stops working. Then uninstall the indexer on the machine (Windows: *Settings → Apps → Nunam Indexer*; Linux: `sudo apt remove nunam-indexer` or `sudo dnf remove nunam-indexer`). On Windows `appsettings.Local.json` stays in the install folder; if the service ran as its own account, also remove the read grants (`icacls <watch path> /remove "NT SERVICE\Nunam.Indexer"` for each).
