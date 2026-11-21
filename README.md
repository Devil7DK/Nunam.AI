# Nunam.AI

Self-hosted search for every file on your home network. An indexer on each computer reads documents, OCRs scans and collects photo, song and video details; a vision model on your own GPU describes images; and one search box finds files by keyword or by meaning. Nothing leaves your network.

Website and docs: <https://nunam.dineshkumar.dev>

## Docs

- [How Nunam works](docs/overview.md)
- [Deploying the server](docs/deployment.md)
- [Adding a machine to the index](docs/adding-a-machine.md)

## Repository

| Folder | What it is |
|---|---|
| `Nunam.Core` | Entities, database context and migrations, AI client, job queue |
| `Nunam.Indexer` | The per-machine indexer (Windows service, systemd unit) |
| `Nunam.AIWorker` | Describes images and embeds files, on the server |
| `Nunam.API` | Search, previews, accounts and machine management |
| `Nunam.WebUI` | The React web UI |
| `docker/` | Compose stack built from source, Postgres init scripts |
| `deploy/` | Indexer installers (MSI, deb, rpm) and the server stack on registry images |
| `website/` | The public website |

Build with the .NET 10 SDK: `dotnet build Nunam.slnx`.

## License

[GNU Affero General Public License v3.0 or later](LICENSE). Running a modified Nunam as a service for others means offering them its source.
