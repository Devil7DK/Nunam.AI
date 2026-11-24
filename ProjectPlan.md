# Nunam — Project Plan

> **Nunam** (नूनम्, Sanskrit) — *"certainly / surely / indeed"*
> A self-hosted, AI-powered file search engine for your local network.

---

## 1. Overview

Nunam indexes all files across multiple machines on the same local network (desktop, laptop, homelab server) and provides a fast, intelligent web-based search interface. It supports keyword search, full-text content search, metadata search, and natural language queries powered by a locally hosted LLM.

### Goals
- Index files and metadata across multiple machines (documents, images, audio, video)
- Use OCR and a local multimodal LLM to understand images, scans and video frames
- Provide a web UI accessible from any device on the local network
- Support natural language queries (e.g. "show me documents related to land/property")
- Everything self-hosted: LLM, embeddings, indexer, database, API, UI

### Non-Goals
- Cloud sync or remote access outside the LAN
- Real-time collaborative editing
- File management (rename, move, delete)

---

## 2. Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     LOCAL NETWORK                        │
│                                                          │
│  ┌──────────────┐   ┌──────────────┐                    │
│  │   Desktop    │   │   Laptop     │                     │
│  │  (Indexer)   │   │  (Indexer)   │                     │
│  └──────┬───────┘   └──────┬───────┘                    │
│         │                  │                             │
│         └────────┬─────────┘                            │
│                  │                                       │
│          ┌───────▼────────┐                             │
│          │    Homelab     │                              │
│          │                │                              │
│          │  ┌───────────┐ │                              │
│          │  │ PostgreSQL│ │                              │
│          │  │ +pgvector │ │                              │
│          │  └─────┬─────┘ │                              │
│          │        │       │                              │
│          │  ┌─────▼─────┐ │                              │
│          │  │  Nunam    │ │                              │
│          │  │    API    │ │                              │
│          │  └─────┬─────┘ │                              │
│          │        │       │                              │
│          │  ┌─────▼─────┐ │                              │
│          │  │  Ollama   │ │                              │
│          │  │(vision +  │ │                              │
│          │  │  embed)   │ │                              │
│          │  └───────────┘ │                              │
│          └────────────────┘                              │
│                  ▲                                       │
│                  │                                       │
│          ┌───────┴───────┐                              │
│          │  Browser UI   │  (any device on LAN)         │
│          │  React + Vite │                               │
│          └───────────────┘                              │
└─────────────────────────────────────────────────────────┘
```

---

## 3. Tech Stack

| Layer | Technology | Notes |
|---|---|---|
| Indexer | .NET 10 Worker Service | Runs on each machine |
| Document Parsing | PdfPig, PDFium, Open XML SDK, ExcelDataReader, OpenMcdf; Tesseract (CLI) for OCR | PDF, DOC/DOCX/RTF, XLS/XLSX, PPTX, OCR. Syncfusion until Phase 10 |
| Image metadata | MetadataExtractor (NuGet) | EXIF, IPTC, XMP |
| Audio metadata | TagLibSharp (NuGet) | MP3, FLAC, AAC, etc. |
| Video metadata | FFMpegCore (NuGet) | Duration, codec, resolution |
| File type detection | MimeDetective (NuGet) | Detects true file type by magic bytes, not extension |
| File watching | FileSystemWatcher (.NET built-in) | Incremental re-indexing |
| LLM runtime | Ollama **or** LM Studio (self-hosted) | Pluggable via config; production: Ollama (`ollama/ollama:rocm`, AMD GPU) in homelab container `192.168.7.113:11434` |
| Vision model | `gemma4:12b-it-qat` (reasoning switched off) | Describes images, scanned pages, video frames — chosen over mistral-small3.2:24b and qwen3.8:27b: as accurate on the samples, 2–3× faster, 7 GB |
| Embeddings | `bge-m3` | 1024-dim, multilingual (Tamil query ↔ English document). Replaced nomic-embed-text (768) after scoring MRR 0.95 vs 0.76 on the sample set |
| Database | PostgreSQL 16 + pgvector | Single DB for all search modes |
| Backend API | ASP.NET Core 10 Minimal API | Runs on homelab |
| AI abstraction | `Microsoft.Extensions.AI` (`IChatClient` + `IEmbeddingGenerator`) | Provider-neutral seam; backend chosen at startup |
| AI backends | OllamaSharp (native) / `Microsoft.Extensions.AI.OpenAI` (OpenAI-compatible `/v1`, for LM Studio) | Selected by `AI:Provider` config |
| ORM | Entity Framework Core 10 | Migrations, LINQ queries, pgvector support |
| DB provider | Npgsql.EntityFrameworkCore.PostgreSQL | EF Core driver for PostgreSQL |
| Vector support | Pgvector.EntityFrameworkCore | Adds `vector` type + distance ops to EF Core |
| Frontend | React 18 + Vite + TypeScript | |
| UI components | shadcn/ui + Tailwind CSS | |
| Deployment | Docker Compose | Homelab only; Indexer runs native |

---

## 4. Solution Structure

Projects live at the repository root (flat layout). The solution file is `Nunam.slnx` (the newer XML solution format used by VS 2022+).

```
Nunam.slnx
├── Nunam.Core/                      # Shared class library
│   ├── Entities/
│   │   ├── EntityBase.cs            # Id (Guid) + Created/LastModified (DateTimeOffset)
│   │   ├── Machine.cs
│   │   ├── IndexedFile.cs           # Main entity (pgvector + FTS columns)
│   │   ├── IndexJob.cs
│   │   ├── FileType.cs              # enum: Other, Document, Image, Audio, Video
│   │   ├── IndexJobKind.cs          # enum: Parse, Describe, Embed
│   │   ├── IndexJobStatus.cs        # enum: Pending, Processing, Done, Failed
│   │   └── IndexJobStage.cs         # enum: None, Detect, Parse, Describe, Embed, Upsert
│   ├── Data/
│   │   ├── NunamDbContext.cs        # EF Core DbContext (Files, Machines, IndexJobs)
│   │   └── Migrations/              # EF Core auto-generated migrations
│   ├── AI/
│   │   ├── AIOptions.cs             # Config (Provider, BaseUrl, models, dims, prompt)
│   │   ├── IAIInferenceService.cs   # Embed (single/batch) + DescribeImage
│   │   ├── AIInferenceService.cs    # Wraps M.E.AI IChatClient + IEmbeddingGenerator
│   │   └── AIServiceCollectionExtensions.cs  # AddNunamAI() — picks Ollama vs OpenAI-compatible backend
│   └── Nunam.Core.csproj
│
├── Nunam.Indexer/                   # .NET Worker Service — runs PARSE jobs on each indexed machine
│   ├── Workers/
│   │   ├── ParseWorker.cs           # Consumes Kind=Parse jobs (detect, parse, render preview, upsert)
│   │   └── WatcherWorker.cs         # FileSystemWatcher wrapper
│   ├── Parsers/
│   │   ├── IFileParser.cs
│   │   ├── PdfParser.cs             # PdfPig + PDFium (Pdfium.cs)
│   │   ├── WordParser.cs            # Open XML SDK, Word97.cs, RtfText.cs
│   │   ├── ExcelParser.cs           # ExcelDataReader + ExcelNumberFormat
│   │   ├── PowerPointParser.cs      # Open XML SDK
│   │   ├── ImageParser.cs           # MetadataExtractor + Tesseract (OcrService)
│   │   ├── AudioParser.cs           # TagLibSharp
│   │   ├── VideoParser.cs           # FFMpegCore
│   │   └── FallbackParser.cs        # Render to image → LLaVA
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── Dockerfile
│   └── Nunam.Indexer.csproj
│
├── Nunam.AIWorker/                  # .NET Worker Service — runs DESCRIBE + EMBED jobs on the homelab
│   ├── Workers/
│   │   ├── DescribeWorker.cs        # Consumes Kind=Describe jobs (vision model via Ollama)
│   │   └── EmbedWorker.cs           # Consumes Kind=Embed jobs (bge-m3 via Ollama, batched)
│   ├── appsettings.json
│   ├── Dockerfile                   # Lives in docker-compose alongside API/UI
│   └── Nunam.AIWorker.csproj
│
├── Nunam.API/                       # ASP.NET Core Minimal API (note: PascalCase `.API`)
│   ├── Endpoints/
│   │   ├── SearchEndpoints.cs
│   │   ├── FileEndpoints.cs         # File preview/thumbnail
│   │   └── StatusEndpoints.cs       # Index stats, machine status
│   ├── Services/
│   │   └── SearchService.cs         # Hybrid search logic
│   ├── appsettings.json
│   ├── Dockerfile
│   └── Nunam.API.csproj
│
├── Nunam.WebUI/                     # React + Vite frontend (esproj for VS integration)
│   ├── src/
│   │   ├── components/
│   │   │   ├── SearchBar.tsx
│   │   │   ├── SearchResults.tsx
│   │   │   ├── ResultCard.tsx
│   │   │   ├── FilterPanel.tsx
│   │   │   └── PreviewModal.tsx
│   │   ├── hooks/
│   │   │   └── useSearch.ts
│   │   ├── api/
│   │   │   └── nunam.ts             # API client
│   │   └── App.tsx
│   ├── package.json
│   ├── vite.config.ts
│   └── nunam.webui.esproj
│
├── docker/
│   ├── docker-compose.yml           # PostgreSQL + Ollama + API + UI
│   └── postgres/
│       └── init.sql                 # Enables pgvector extension only (schema managed by EF Core)
└── ProjectPlan.md                   # This file
```

> **Naming notes (flat layout):**
> - The API project is `Nunam.API` (uppercase), not `Nunam.Api` as some references in this doc may suggest.
> - The UI project is `Nunam.WebUI` (uses an `.esproj` for Visual Studio integration), not `nunam-ui`.
> - Dockerfiles live inside each project folder (`Nunam.API/Dockerfile`, `Nunam.Indexer/Dockerfile`) and reference paths relative to the repo root, so `docker build` must be run from the repo root or `docker-compose.yml` must set `context: .` with `dockerfile: Nunam.API/Dockerfile`.

---

## 5. Database Schema

Schema is managed entirely via **EF Core migrations** (`dotnet ef migrations add`, `dotnet ef database update`). The only manual SQL needed is enabling the pgvector extension, which goes in `init.sql` for Docker:

```sql
-- docker/postgres/init.sql
CREATE EXTENSION IF NOT EXISTS vector;
```

### EF Core Entities (`Nunam.Core/Entities`)

All entities inherit from `EntityBase`, which provides:
- `Id` (`Guid`, defaults to `Guid.NewGuid()`)
- `Created` (`DateTimeOffset`, UTC)
- `LastModified` (`DateTimeOffset`, UTC)

This is why none of the entities below re-declare `Id`, and `IndexJob` does not need its own `CreatedAt`/`UpdatedAt` — `EntityBase.Created`/`LastModified` cover them. For `IndexedFile`, the file's *filesystem* timestamps are renamed to `FileCreatedAt`/`FileModifiedAt` so they don't collide with the row-audit `Created` from `EntityBase`. All timestamps are `DateTimeOffset` for consistency with `EntityBase`.

```csharp
// EntityBase.cs (already in repo)
public abstract class EntityBase
{
    public Guid Id { get; private init; } = Guid.NewGuid();
    public DateTimeOffset Created { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastModified { get; private set; } = DateTimeOffset.UtcNow;
}

// FileType.cs
public enum FileType { Other, Document, Image, Audio, Video }

// IndexJobKind.cs — which queue / task type this job represents.
// Each file produces 1 Parse job; Parse, when complete, enqueues 1 Embed job
// for the file and (conditionally) 1 Describe job.
public enum IndexJobKind { Parse, Describe, Embed }

// IndexJobStatus.cs
public enum IndexJobStatus { Pending, Processing, Done, Failed }

// IndexJobStage.cs — sub-step within the current Kind. Mostly useful for Parse
// (Detect → Parse → Upsert); Describe and Embed are single-stage internally.
public enum IndexJobStage { None, Detect, Parse, Describe, Embed, Upsert }

// Machine.cs
public class Machine : EntityBase
{
    public string Name { get; set; } = default!;       // "desktop", "laptop"
    public string Hostname { get; set; } = default!;
    public DateTimeOffset? LastSeen { get; set; }

    public ICollection<IndexedFile> Files { get; set; } = [];
    public ICollection<IndexJob> Jobs { get; set; } = [];
}

// IndexedFile.cs
public class IndexedFile : EntityBase
{
    public Guid MachineId { get; set; }
    public Machine Machine { get; set; } = default!;

    public string Path { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public string? Extension { get; set; }
    public long? SizeBytes { get; set; }
    public FileType FileType { get; set; }

    public DateTimeOffset? FileCreatedAt { get; set; }   // filesystem ctime
    public DateTimeOffset? FileModifiedAt { get; set; }  // filesystem mtime
    public DateTimeOffset IndexedAt { get; set; }        // when we processed it

    public string? Content { get; set; }
    public string? Description { get; set; }
    public string? Metadata { get; set; }                // JSON string (JSONB in DB)

    public byte[]? PreviewImage { get; set; }            // rendered PNG (binary/image files) — fed to LLaVA + reused as UI thumbnail
    public Vector? Embedding { get; set; }               // pgvector type
    public NpgsqlTsVector? Fts { get; set; }             // auto-updated by DB trigger
}

// IndexJob.cs — one row per (file, task). A single file produces multiple
// IndexJob rows over its lifetime: 1 Parse, then 1 Embed (always) and
// optionally 1 Describe.
public class IndexJob : EntityBase
{
    public Guid MachineId { get; set; }
    public Machine Machine { get; set; } = default!;

    public string Path { get; set; } = default!;
    public IndexJobKind Kind { get; set; } = IndexJobKind.Parse;
    public IndexJobStatus Status { get; set; } = IndexJobStatus.Pending;
    public IndexJobStage Stage { get; set; } = IndexJobStage.None;
    public string? Error { get; set; }
}
```

> **Note on `LastModified`:** `EntityBase.LastModified` has a `private set`, so updating it on `SaveChanges` requires either a `SaveChangesInterceptor` or a method on `EntityBase` to touch it. To be decided alongside `NunamDbContext`.

### NunamDbContext.cs (`Nunam.Core/Data/`)

Located at `Nunam.Core/Data/NunamDbContext.cs`. Decisions encoded here:

- **Snake-case column/table names** via `EFCore.NamingConventions` 10.0.1 (`Files`, `MachineId`, `FileType` → `files`, `machine_id`, `file_type`). Matches the raw-SQL examples in §7 so we don't have to quote identifiers.
- **Enums stored as strings** (`FileType`, `IndexJobKind`, `IndexJobStatus`, `IndexJobStage`) via `HasConversion<string>()`. Far more debuggable in psql than integers; trivial perf cost.
- **`LastModified` updated by a `SaveChangesInterceptor`** (`TouchEntityInterceptor`, sibling file). Resolves the `private set` problem from §5 — the interceptor writes through `entry.Property(...).CurrentValue`, which goes through EF Core's metadata-aware setter and bypasses the public surface.
- **Snake-case + interceptor are wired in `OnConfiguring`**, so any consumer using `services.AddDbContext<NunamDbContext>(opts => opts.UseNpgsql(...))` gets them automatically without having to remember.
- **Cascading deletes from `Machine`** — removing a Machine removes its Files and Jobs. (For LAN scale, simpler than tombstoning.)
- **Extra index on `IndexJob(Kind, Status)`** beyond the plan — the queue-polling query (`WHERE Kind=X AND Status=Pending FOR UPDATE SKIP LOCKED`) hits this hot path constantly.

```csharp
public class NunamDbContext(DbContextOptions<NunamDbContext> options) : DbContext(options)
{
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<IndexedFile> Files => Set<IndexedFile>();
    public DbSet<IndexJob> IndexJobs => Set<IndexJob>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(new TouchEntityInterceptor());
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.HasPostgresExtension("vector");

        model.Entity<Machine>(e =>
        {
            e.HasIndex(m => m.Hostname).IsUnique();
        });

        model.Entity<IndexedFile>(e =>
        {
            e.HasAlternateKey(f => new { f.MachineId, f.Path });

            e.HasIndex(f => f.MachineId);
            e.HasIndex(f => f.Extension);
            e.HasIndex(f => f.FileType);
            e.HasIndex(f => f.FileModifiedAt);
            e.HasIndex(f => f.Fts).HasMethod("gin");
            e.HasIndex(f => f.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");

            e.Property(f => f.FileType).HasConversion<string>();
            e.Property(f => f.Metadata).HasColumnType("jsonb");
            e.Property(f => f.Embedding).HasColumnType("vector(1024)");

            e.HasOne(f => f.Machine)
                .WithMany(m => m.Files)
                .HasForeignKey(f => f.MachineId)
                .OnDelete(DeleteBehavior.Cascade);
            // fts column auto-updated via DB trigger added in initial migration's Up()
        });

        model.Entity<IndexJob>(e =>
        {
            e.HasIndex(j => new { j.Kind, j.Status });
            e.HasIndex(j => j.MachineId);

            e.Property(j => j.Kind).HasConversion<string>();
            e.Property(j => j.Status).HasConversion<string>();
            e.Property(j => j.Stage).HasConversion<string>();

            e.HasOne(j => j.Machine)
                .WithMany(m => m.Jobs)
                .HasForeignKey(j => j.MachineId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

### TouchEntityInterceptor.cs (`Nunam.Core/Data/`)

```csharp
public sealed class TouchEntityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        TouchModifiedEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        TouchModifiedEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void TouchModifiedEntities(DbContext? context)
    {
        if (context is null) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(EntityBase.LastModified)).CurrentValue = now;
            }
        }
    }
}
```

> `Created` doesn't need an interceptor — it's `private init` and gets `DateTimeOffset.UtcNow` from the constructor on entity creation. Only `LastModified` needs touching on update.

### Migration Commands

```bash
# Run from the repo root (flat layout)
dotnet ef migrations add InitialSchema --project Nunam.Core --startup-project Nunam.API
dotnet ef database update            --project Nunam.Core --startup-project Nunam.API
```

> **Note:** The `fts` trigger (`update_fts`) cannot be expressed in EF Core fluent API — add it as raw SQL inside the migration's `Up()` method:
> ```csharp
> migrationBuilder.Sql("""
>     CREATE OR REPLACE FUNCTION update_fts() RETURNS trigger AS $$
>     BEGIN
>       NEW.fts := to_tsvector('english',
>         coalesce(NEW.filename,'') || ' ' ||
>         coalesce(NEW.content,'') || ' ' ||
>         coalesce(NEW.description,''));
>       RETURN NEW;
>     END;
>     $$ LANGUAGE plpgsql;
>
>     CREATE TRIGGER trg_update_fts
>     BEFORE INSERT OR UPDATE ON files
>     FOR EACH ROW EXECUTE FUNCTION update_fts();
> """);
> ```

---

## 6. Indexer Logic

### Job Model (Immich-inspired, Postgres-based)

Inspired by Immich's queue architecture: **one `IndexJob` row per (file, task)**, with a `Kind` column splitting the work into separate Postgres-backed queues. Each kind runs in its own worker process, on whatever hardware fits its workload.

| `Kind` | What it does | Where it runs | Hardware fit |
|---|---|---|---|
| `Parse` | Detect MIME, parse content, render preview PNG, upsert partial `IndexedFile` row | `Nunam.Indexer` on each indexed machine | CPU + local disk |
| `Describe` | Send preview to the vision model, store description, then queue Embed | `Nunam.AIWorker` on homelab | Network → Ollama container (GPU) |
| `Embed` | Embed `filename + description + content` with `bge-m3`, store vector | `Nunam.AIWorker` on homelab | Network → Ollama container (GPU) |

**Worker dispatch:** each worker polls `IndexJob WHERE Status='Pending' AND Kind=<its kind>`, claims rows with `FOR UPDATE SKIP LOCKED`, processes, marks `Done` or `Failed`. Per-kind concurrency is configured in each worker's `appsettings.json` (e.g. `Describe = 1`, `Embed = 4`).

### Lifecycle of a file

```
File discovered (initial scan or FileSystemWatcher)
    │
    ▼
INSERT IndexJob(Kind=Parse, Status=Pending)             ← producer enqueues only this one
    │
    ▼
ParseWorker (on the indexed machine)
    ├─ Detect MIME via MimeDetective (magic bytes → extension fallback)
    ├─ Route to parser by MIME:
    │      application/pdf                       → PdfParser
    │      application/vnd.openxmlformats.word   → WordParser
    │      application/vnd.openxmlformats.sheet  → ExcelParser
    │      application/vnd.openxmlformats.pres   → PowerPointParser
    │      image/*                               → ImageParser         (MetadataExtractor + OCR)
    │      audio/*                               → AudioParser         (TagLibSharp)
    │      video/*                               → VideoParser         (FFMpegCore)
    │      unmatched / binary                    → FallbackParser      (render only — no AI here)
    ├─ Extract content + metadata JSONB
    ├─ If content is too short / empty: render first page/frame to PNG → IndexedFile.PreviewImage
    ├─ UPSERT IndexedFile (content, metadata, preview) — file is now KEYWORD-SEARCHABLE
    ├─ INSERT IndexJob(Kind=Embed,    Status=Pending)                    ← always
    ├─ INSERT IndexJob(Kind=Describe, Status=Pending)                    ← only if PreviewImage was set
    └─ UPDATE this job to Status=Done
    │
    ▼
AIWorker (on homelab, batches)
    │
    ├─ DescribeWorker (Concurrency: 1)
    │      ├─ Reads IndexedFile.PreviewImage (does NOT need filesystem access to the original)
    │      ├─ POSTs to Ollama chat API with gemma4:12b-it-qat + image (think: false)
    │      ├─ UPDATE IndexedFile.Description
    │      └─ UPDATE this job to Status=Done
    │
    └─ EmbedWorker (Concurrency: 4, batched by N)
           ├─ Pulls up to N pending Embed jobs (preferring ones whose Describe is Done)
           ├─ For each: build embedding input = filename + description + first 512 tokens of content
           ├─ POSTs all N in one bge-m3 call
           ├─ UPDATE IndexedFile.Embedding for each — file is now SEMANTICALLY-SEARCHABLE
           └─ UPDATE all these jobs to Status=Done
```

### Why this model

- **Independent failure domains.** LLaVA timing out doesn't fail Embed jobs; Embed jobs failing for one file don't fail the batch's other 31. The user-visible result: search keeps improving over time as bits of the AI pipeline catch up, instead of a wall of `Failed` rows whenever Ollama hiccups.
- **Files become keyword-searchable immediately after Parse,** without waiting for AI. Semantic search lights up later as Embed completes.
- **Right batch size per workload.** LLaVA processes one big image per call; embeddings batch beautifully (16–32 inputs per HTTP request). Separate queues let each worker use the right strategy.
- **Different hardware profiles cleanly separated.** Parse is CPU + local IO on the user's machine; Describe / Embed are network calls from the AI worker to the Ollama GPU container, which may be busy or down. The homelab AI worker can keep retrying when Ollama is down without surfacing failures to users.
- **No file access needed on the AI side.** ParseWorker writes the preview PNG into `IndexedFile.PreviewImage`. AIWorker reads only from the DB; it never needs SMB/NFS access to the indexed machine.

### Producers, not jobs

The initial directory scan and `FileSystemWatcher` are *producers* — they don't have their own job rows. They just `INSERT IndexJob(Kind=Parse, Status=Pending)` for each file discovered.

### Failure semantics

Any worker that hits an error marks its own job `Failed` with `Stage` left at the failing step and `Error` populated. The job is **scoped** — failing a Describe doesn't fail Parse or Embed. Retry = re-mark `Status = Pending`.

### Indexer Configuration (appsettings.json)

```json
{
  "Nunam": {
    "MachineId": "<uuid>",
    "MachineName": "desktop",
    "WatchPaths": [
      "C:\\Users\\Dineshkumar\\Documents",
      "C:\\Users\\Dineshkumar\\Downloads",
      "D:\\Projects"
    ],
    "ExcludePatterns": [
      "node_modules", ".git", "bin", "obj", "__pycache__"
    ],
    "MaxFileSizeMb": 500,
    "PostgresConnectionString": "Host=homelab;Database=nunam;Username=nunam;Password=..."
  },
  "AI": {
    "Provider": "Ollama",                       // or "OpenAICompatible" for LM Studio
    "BaseUrl": "http://192.168.7.113:11434",     // LM Studio: "http://<host>:1234/v1"
    "ApiKey": null,                              // ignored by Ollama/LM Studio
    "ChatModel": "gemma4:12b-it-qat",
    "EmbeddingModel": "bge-m3",
    "EmbeddingDimensions": 1024,
    "DescribePrompt": "Describe this image in detail for a search index..."
  }
}
```

> The `AI` section is provider-neutral. `AddNunamAI(configuration)` reads `AI:Provider` and wires either OllamaSharp (native API) or the OpenAI SDK pointed at an OpenAI-compatible `/v1` endpoint (LM Studio). Application code only ever sees `IAIInferenceService`. Switching backends is config-only — no recompile.

---

## 7. API Endpoints

Minimal API in `Nunam.API` (`Endpoints/`, `Services/`, `Contracts/`). JSON is camelCase, enums are camelCase strings (`"document"`, `"hybrid"`). Errors are RFC 9110 problem details; bad query values return 400 with the valid choices listed.

### Search

```
GET /api/search
  ?q=<query>                          (empty → browse: newest first, filters still apply)
  &mode=hybrid|keyword|semantic       (default: hybrid; case-insensitive)
  &type=document|image|audio|video|other
  &machine=<machine name>             (case-insensitive)
  &ext=.pdf,docx                      (leading dot optional)
  &from=2024-01-01&to=2024-12-31      (file modified date, both inclusive, UTC days)
  &limit=20 (1–100) &offset=0
                                      (q: at most 500 characters and 50 distinct words)

Response:
{
  "total": 3,                         // fused hits (≤ 2 × Search:CandidatesPerSide)
  "mode": "hybrid",
  "semanticUsed": true,               // false when the AI backend was unreachable
  "warning": null,                    // set when hybrid fell back to keywords only
  "results": [
    {
      "id": "uuid", "fileName": "scanned-bill.pdf", "path": "/…/scanned-bill.pdf",
      "machine": "home-desktop", "fileType": "document", "extension": ".pdf",
      "sizeBytes": 22764, "modifiedAt": "…",
      "snippet": "\uE000Electricity\uE001 \uE000bill\uE001 due on 25 November …",
      "description": "The image displays an electricity bill notice…",
      "hasPreview": true,
      "score": 0.0328,                // fused rank score; only meaningful for ordering
      "similarity": 0.7972            // cosine similarity when the semantic side matched
    }
  ]
}
```

Snippets mark matched words with U+E000 … U+E001 (private-use characters that don't occur in indexed text), so the UI highlights by splitting the string and never renders file content as HTML.

### Search logic

One SQL statement (`SearchService`), built from CTEs:

1. **filtered** — ids passing type/machine/extension/date filters.
2. **keyword** — full-text matches plus filename `ILIKE` matches (the FTS parser keeps `deed.txt` as one token, so filename substrings need `ILIKE`). Filename hits rank first, then `ts_rank_cd`. Keyword mode uses `websearch_to_tsquery` (all words, `"phrases"`, `-exclusions`); **hybrid matches any word** because hybrid queries are sentences and requiring every word misses most relevant files.
3. **semantic** — nearest `Search:CandidatesPerSide` (200) embeddings, then only those within `Search:SemanticRelativeMargin` (0.05, calibrated for bge-m3; nomic-embed-text needed 0.08) of the best similarity. Cosine similarity has no absolute meaning across queries with nomic-embed-text (a right answer scored 0.54 for one query while noise scored 0.59 for another), so a fixed floor doesn't work; relative to the top hit it does.
4. **fused** — reciprocal rank fusion, `1/(60 + keyword rank) + 1/(60 + semantic rank)`. Chosen over the originally planned `ts_rank × 0.4 + similarity × 0.6`: `ts_rank` values are ~0.01–0.1 while similarity is ~0.5–0.8, so a weighted sum is decided almost entirely by the semantic side.
5. **page** — limit/offset, then `ts_headline` only for the returned rows.

Embedding prefixes are configurable (`AI:EmbeddingQueryPrefix` / `AI:EmbeddingDocumentPrefix`): empty for bge-m3, `search_query: ` / `search_document: ` for nomic-embed-text. **Changing the model or either prefix means re-embedding every file:**
```sql
INSERT INTO index_jobs (id, machine_id, path, kind, status, stage, created, last_modified)
SELECT gen_random_uuid(), machine_id, path, 'Embed', 'Pending', 'None', now(), now() FROM files;
```

If the query can't be embedded within `Search:QueryEmbeddingTimeoutSeconds` (10), hybrid answers from keywords alone with a `warning`; semantic mode returns 503.

### Other endpoints

```
GET /api/files/{id}            # Details: metadata JSON, description, first 20 000 chars of content, hasPreview/hasEmbedding
GET /api/files/{id}/preview    # Stored preview image (JPEG/PNG); ?size=256 returns a thumbnail (longer edge ≤ size, 32–1568)
                               #   Thumbnails are made on demand and kept in an in-memory cache (64 MB); ETag + 304 supported
GET /api/status                # Totals by type, described/embedded counts, job counts by kind/status, last 20 failures, machines
GET /api/machines              # Machines with file count, last indexed time and whether the user may share it
GET /healthz                   # 200 when the database is reachable (no sign-in needed)
```

Everything above covers only the machines the signed-in user can see (all of them for admins); a file on another machine answers 404.

### Accounts and sharing

```
GET    /api/auth/state                      # { setupRequired, user } — no sign-in needed
POST   /api/auth/setup                      # { code, userName, displayName, password } — first admin, only while no account exists
POST   /api/auth/sign-in                    # { userName, password } — sets the session cookie
POST   /api/auth/sign-out[?everywhere=true]
POST   /api/auth/password                   # { currentPassword, newPassword } — signs other devices out
GET    /api/auth/sessions                   # own sessions; DELETE /api/auth/sessions/{id}; POST /api/auth/sessions/end-others
GET    /api/users/directory                 # enabled people (id, user name, display name), for the share picker
GET    /api/users                           # admin: accounts
POST   /api/users                           # admin: { userName, displayName, role, temporaryPassword }
PATCH  /api/users/{id}                      # admin: { displayName?, role?, disabled? } — not on yourself; one enabled admin must remain
POST   /api/users/{id}/password             # admin: { temporaryPassword } — must be changed at next sign-in
DELETE /api/users/{id}                      # admin
GET    /api/machines/{id}/access            # owners and admins
PUT    /api/machines/{id}/access/{userId}   # { role: owner|member }
DELETE /api/machines/{id}/access/{userId}
```

Every endpoint except `/api/auth/state`, setup, sign-in and `/healthz` needs a session; until a temporary password is changed only sign-out and the password change work (403 otherwise). State-changing requests must send `X-Nunam-Request: 1` and a JSON body. Sign-in, setup and password changes share a per-address rate limit (`Auth:SignInAttemptsPerMinute`, 10). No CORS: the UI is always served from the API's origin (Vite proxy in development, nginx in Compose).

`POST /api/index/trigger` (from the original plan) is **not implemented**. Re-indexing a path today = insert a `Parse` job, or touch the file so the watcher picks it up.

---

## 8. Frontend (React UI)

### Key Features
- Unified search bar — hybrid mode by default
- Toggle: keyword / semantic / natural language
- Facet filters: file type, machine, extension, date range
- Results show: filename, machine badge, path, snippet, score
- Image results show thumbnail
- Click result → open file on its native machine (via OS file:// URI or UNC path)
- Dark mode support

### Search Modes (UI)
- **Quick search** (default): hybrid, debounced, results as you type
- **Natural language**: full semantic — user types "documents about property tax from 2022"
- **Advanced**: expose all filters explicitly

---

## 9. Ollama Models

Ollama runs in the homelab's GPU container (`ssh ollama`, `192.168.7.113:11434`, Docker image `ollama/ollama:rocm`, `OLLAMA_HOST=0.0.0.0:11434`), shared with Open WebUI. Both models below were already installed there.

| Model | Purpose | Size |
|---|---|---|
| `bge-m3` | Embeddings, 1024-dim, multilingual | 1.2 GB |
| `gemma4:12b-it-qat` | Describes images, scanned pages and video frames (reasoning off) | 7.2 GB |

Compared 2026-11-20 on the sample set (16 files, 10 queries incl. 2 Tamil):
- **Embeddings** — bge-m3 put the right file first for 9/10 queries (MRR 0.95), nomic-embed-text for 7/10 (MRR 0.76). nomic failed both Tamil queries (ranks 8 and 4) and barely separated the song for "music".
- **Vision** — gemma4:12b-it-qat, mistral-small3.2:24b and qwen3.8:27b all described the bill, the rotated photo and the video frame correctly; gemma took ~2 s per image warm vs 3.5–5 s and 5.6–7 s, and is the smallest. All three are thinking- or vision-capable; with reasoning left on, hidden reasoning tokens eat the output budget, hence `AI:DisableReasoning`.

---

## 10. Deployment

Step-by-step guides: **`docs/deployment.md`** (homelab stack) and **`docs/adding-a-machine.md`** (indexers).

**Hardware split:**
- **Homelab** — Docker Compose (`docker/docker-compose.yml`): `postgres` (pgvector, Postgres 18, not published), `migrations` (one-shot EF bundle, runs as the owner role), `nunam-api` (`nunam_api`, the only login on the tables; not published; serves the indexer packages from `/packages`), `nunam-ui` (nginx on `UI_PORT` and HTTPS on `UI_HTTPS_PORT` with a self-signed or supplied certificate from `docker/tls`, proxies `/api`; `/api/agent/` takes bodies up to 32 MB), `nunam-aiworker` (signs in to the API with `NUNAM_WORKER_TOKEN`). Settings and passwords in `docker/.env` (template `docker/.env.example`, git-ignored).
- **Ollama container** (`192.168.7.113`, its own Proxmox container with the AMD GPU) — outside Compose; `AI_BASE_URL` points at it. Jobs wait `Pending` while it's unreachable.
- **Indexed machines** — `Nunam.Indexer` natively: Windows service via the MSI (`deploy/indexer/windows/build-msi.sh`), Linux via the `.deb`/`.rpm` packages (`deploy/indexer/linux/build-packages.sh`). Per-machine settings and the machine token in `appsettings.Local.json` next to the executable (templates `deploy/indexer/{windows,linux}/appsettings.Local.example.json`; the real file is git-ignored; on Linux it lives in `/etc/nunam-indexer`); it overrides `appsettings.json`, environment variables override both. The content root is always the executable's folder.

`docker/postgres/10-roles.sh` creates the `nunam_api` login on first start of the volume; the migrations grant it the tables. Indexers and the AI worker have no database logins since 0.4 (Phase 12).

**Verified 2026-11-20** with a throwaway Compose project on this machine: migrations applied (owner password containing quotes), roles and grants as above, `nunam_api` refused a DELETE, UI + API served through nginx, a published Linux indexer logged in as `nunam_indexer` and indexed, deleted and reconciled files, and search fell back to keywords with Ollama unreachable while AI jobs waited.

---

## 11. Implementation Phases

### Phase 1 — Foundation
- [x] Create solution with Core, Indexer, Api, UI projects
- [x] Install EF Core / pgvector NuGet packages in `Nunam.Core` (see §13)
- [x] Implement EF Core entities (`Machine`, `IndexedFile`, `IndexJob` + `FileType`/`IndexJobKind`/`IndexJobStatus`/`IndexJobStage` enums) under `Nunam.Core/Entities`, inheriting from `EntityBase`
- [x] Scaffold `Nunam.AIWorker` project (homelab AI-side worker — Describe + Embed) — skeleton `DescribeWorker` + `EmbedWorker` in `Workers/`, Dockerfile, `appsettings.json` shape, added to `Nunam.slnx`
- [x] Set up PostgreSQL + pgvector via Docker — dev: container `nunam-dev-postgres` (§14); homelab: `docker/docker-compose.yml` (§10)
- [x] Implement `NunamDbContext` with EF Core in Core (snake_case naming, enum-as-string conversions, HNSW + GIN + queue indexes, `TouchEntityInterceptor` for `LastModified`)
- [x] Add initial EF Core migration (`Nunam.Core/Data/Migrations/20260605015843_InitialSchema.cs`) — `fts` trigger as raw SQL in `Up()`, `PreviewImage` column included, all snake_case
- [x] Add `IDesignTimeDbContextFactory<NunamDbContext>` in Core so migrations can be generated/applied without wiring `AddDbContext` into a startup project's `Program.cs`
- [x] Run `dotnet ef database update --project Nunam.Core --startup-project Nunam.API` — schema applied to local `nunam_dev` database; verified all tables, HNSW + GIN indexes, FTS trigger, and pgvector 0.8.2 extension are in place
- [x] Implement AI inference service (embed + describe) in Core — `Nunam.Core/AI/`. Renamed from `OllamaService` to `IAIInferenceService`/`AIInferenceService` (provider-neutral). Built on `Microsoft.Extensions.AI`; `AddNunamAI(config)` selects Ollama (OllamaSharp) or OpenAI-compatible / LM Studio (`Microsoft.Extensions.AI.OpenAI`) via `AI:Provider`. Returns pgvector `Vector`; supports single + batch embeddings and image description with per-call prompt override.
- [x] Verify AI backend reachable + wire into `Nunam.AIWorker` — LM Studio verified on `localhost:1234/v1` (embeddings 768-dim ✓, vision ✓). Chose `qwen/qwen3-vl-4b` over Gemma 3n for the Describe stage (14× faster, no reasoning-token waste). Added `AIOptions.MaxOutputTokens`; `AddNunamAI` registered in the worker; Ollama/Gaming-PC remains the production default in `appsettings.json`. *(Still TODO: confirm Ollama path on the Gaming PC when that machine is up.)*

### Phase 2 — Parse Worker (`Nunam.Indexer`, runs per machine)
- [x] Implement `ParseWorker` (polls `IndexJob WHERE Kind=Parse AND Status=Pending` for **this machine only**, claims via `FOR UPDATE SKIP LOCKED`)
- [x] Implement `ScanWorker` (startup crawl; skips files unchanged by size + mtime) and `WatcherWorker` (FileSystemWatcher, 2 s debounce → INSERT `IndexJob(Kind=Parse)`); `MachineInitializer` upserts this host's `Machine` row; `PathFilter` exclude patterns use `/` and match on Windows + Linux
- [x] Implement `PdfParser` using Syncfusion.PDF — text of every page + page count/title/author; under 50 chars of text means a scan, so page 1 is rendered to PNG (`Syncfusion.PdfToImageConverter.Net`, PDFium) and queued for Describe; password-protected PDFs get `{"encrypted": true}` metadata and no content *(Syncfusion replaced in Phase 10)*
- [x] Implement `WordParser` using Syncfusion.DocIO — `.doc/.docx/.docm/.dot/.dotx/.dotm/.rtf`, text + title/author/subject/word count *(Syncfusion replaced in Phase 10)*
- [x] Implement `ExcelParser` using Syncfusion.XlsIO — `.xls/.xlsx/.xlsm/.xltx/.xltm`, sheet names + displayed cell text, one row per line *(Syncfusion replaced in Phase 10)*
- [x] Implement `PowerPointParser` using Syncfusion.Presentation — `.pptx` family only (Syncfusion can't open binary `.ppt`; it falls back to filename-only), shape/group/table text + speaker notes *(Syncfusion replaced in Phase 10)*
- [x] Cap stored content at `MaxContentChars` (500 000) — the FTS trigger's `to_tsvector` fails above 1 MB — and strip NULs centrally in `ParseWorker`
- [x] Register the Syncfusion license at Indexer startup from `Syncfusion:LicenseKey` — removed with Syncfusion in Phase 10
- [x] Implement `ImageParser` (EXIF via MetadataExtractor + OCR via Syncfusion.OCR) — camera make/model, date taken, GPS, pixel size into metadata; OCR text (kept only when it has ≥ 3 real words, so photos don't fill `content` with noise) into content; preview scaled to `PreviewMaxEdgePx` (1568) with EXIF rotation applied, JPEG unless transparent. Over `MaxPreviewMb`: EXIF only *(Syncfusion replaced in Phase 10)*
- [x] OCR scanned PDFs — `PdfParser` adds a text layer to the first `OcrMaxPdfPages` (20) pages of a PDF with no text, then extracts it; the rendered first page still goes to Describe
- [x] Implement `TextParser` — plain text / source / markup read as UTF-8 content (NULs stripped)
- [x] Implement `AudioParser` using TagLibSharp — title/artists/album/genre/comment/lyrics as content (so songs are found by name), plus year, track, duration, bitrate, sample rate, channels
- [x] Implement `VideoParser` using FFMpegCore — duration, resolution, frame rate, codecs, container, title/creation tags via ffprobe; one frame at 10% (≤ 30 s) grabbed with ffmpeg as the preview → Describe. Needs ffmpeg on the machine (see §14); without it the video is indexed by name only
- [x] `FallbackParser` — indexes filename + filesystem metadata only. Rendering moved to the parsers that can render: `PdfParser` (page 1 of scanned PDFs), `VideoParser` (a frame), `ImageParser` (the image). Not rendered: Word/PowerPoint files without text (would need `Syncfusion.DocIORenderer`/`PresentationRenderer`) and legacy `.ppt`
- [x] Implement `FileTypeDetector` using MimeDetective — extension first (cheap, usually right), magic bytes only for unknown extensions
- [x] Wire ParseWorker to upsert `IndexedFile` and enqueue the follow-up: `Describe` when there's a preview (DescribeWorker then enqueues `Embed`, so the vector includes the description), otherwise `Embed` directly
- [x] Test parse-only indexing on a sample folder with mixed file types — verified on Linux against a pgvector container: excludes honoured, keyword (FTS) search finds parsed text before the AIWorker runs

### Phase 2b — AI Worker (`Nunam.AIWorker`, runs on homelab)
- [x] Implement `DescribeWorker` (polls `Kind=Describe`, calls the vision model, updates `IndexedFile.Description`, then enqueues `Embed`)
- [x] Implement `EmbedWorker` (polls `Kind=Embed` in batches, calls `nomic-embed-text`, updates `IndexedFile.Embedding`)
- [x] Per-queue throughput from `appsettings.json` — batch size per poll (`DescribeBatchSize: 1`, `EmbedBatchSize: 16`) rather than parallel consumers; run more AIWorker instances to scale out
- [x] Graceful handling of Ollama-unreachable: jobs return to `Pending`, exponential backoff (5 s → 120 s), no `Failed` rows from outages. Shared `PollingWorker` base in Core; jobs abandoned in `Processing` by a crash are requeued after `StaleJobMinutes` (30)
- [x] Test on sample data — verified with Ollama (`qwen2.5vl:7b` + `nomic-embed-text`): image described then embedded, and a pgvector cosine query for "documents about property and land" ranks the land-deed text first

### Phase 3 — API
- [x] Implement hybrid search endpoint (`GET /api/search`) — keyword / semantic / hybrid (RRF) with type, machine, extension and date filters, highlighted snippets, browse mode for an empty query; falls back to keywords when the AI backend is down (§7)
- [x] Implement file metadata endpoint (`GET /api/files/{id}`)
- [x] Implement preview endpoint (`GET /api/files/{id}/preview`) — the stored preview, or an on-demand cached thumbnail with `?size=`; text lives in `/api/files/{id}` and search snippets
- [x] Implement status endpoint (`GET /api/status`) — includes job counts and recent failures for the Phase 5 health view
- [x] Implement machines endpoint (`GET /api/machines`)
- [x] Add CORS for local network access — config-driven origin list
- [x] Test natural language queries end-to-end — on the dev sample set: "documents about land or property" → the sale agreement and land deed; "when is the electricity bill due" → the scanned bill and bill photo; "family celebration video" → the birthday video; "photo from a holiday" → the holiday photo. Weak spot: audio is only embedded from its tags, so "music" barely separates the song from unrelated files
- [x] Embed with nomic's `search_document:` / `search_query:` prefixes (needed re-embedding the dev set)
- [x] Switch to the homelab Ollama: `bge-m3` embeddings (`vector(1024)`, migration `UseBgeM3Embeddings` clears old vectors and queues every file for re-embedding) and `gemma4:12b-it-qat` descriptions with reasoning off; semantic margin recalibrated to 0.05 (§9)
- [ ] `POST /api/index/trigger` — not built; the API's DB user is read-only (see §7)

### Phase 4 — Frontend
- [x] Scaffold React + Vite + TypeScript + Tailwind + shadcn/ui — Tailwind v4 via `@tailwindcss/vite`, shadcn/ui (Radix, "Nova" preset) with the system UI font instead of Geist and a smaller radius; Vite dev server proxies `/api` to `NUNAM_API_URL` (default `http://localhost:5033`)
- [x] Build `SearchBar` with debounce and mode toggle — 300 ms debounce, Hybrid / Keywords / Meaning, `/` focuses the box, in-flight requests are aborted when the query changes
- [x] Build `FilterPanel` (type, machine, extension, date) — native date inputs; collapses behind a "Filters" button below the `lg` breakpoint
- [x] Build `SearchResults` and `ResultCard` — thumbnail (`preview?size=128`) or type icon, machine badge, folder path with copy button, highlighted snippet (or the AI description), size and date; "Show more" paging; empty query lists recently modified files
- [x] Build `PreviewModal` (text snippet / image thumbnail) — preview at 1024 px, AI description, metadata (GPS links to OpenStreetMap), first 20 000 chars of text
- [x] Wire up `useSearch` hook to API — plus `useUrlState`: query, mode and filters live in the address bar, so searches can be bookmarked
- [x] Polish UI, dark mode, loading/empty states — system/light/dark toggle, skeleton rows, error and no-result states, keyword-only fallback warning, index status line (files, machines, queued/failed jobs; refreshes every minute)
- Opening a file on its own machine: browsers block `file://` links from an `http://` page, so each result has **Copy path** instead (clipboard needs https or localhost; over plain LAN http it falls back to a prompt with the path selected)

### Phase 5 — Deployment & Hardening
- [x] Finalize `docker-compose.yml` for homelab — `docker/docker-compose.yml` + migrations bundle image + nginx UI image + per-service DB logins; Ollama left out (it lives on the GPU machine). Tested end to end on this machine (§10)
- [x] Run the Indexer as a Windows service — first `deploy/indexer/install-windows.ps1` (since replaced by the MSI, Phase 8), tested end to end on a Windows 10 22H2 VM (install, update, service settings, settings-file ACL, parsing every file type incl. OCR and video, live watcher with renames and deletes). Windows needs the Visual C++ 2015–2022 x64 runtime for OCR; the installer warns without it
- [x] Set up pgvector HNSW index tuning — build parameters left at pgvector's defaults (`m=16`, `ef_construction=64`; ample recall for a home index). The real problem was query-time: an HNSW scan returns at most `hnsw.ef_search` rows (default 40), so semantic search silently capped at 40 candidates once the index is used. Search now sets `hnsw.ef_search` to `Search:CandidatesPerSide` and `hnsw.iterative_scan = relaxed_order` per transaction (verified on 20 000 vectors: 40 → 200)
- [x] Add re-index on file delete/move — watcher handles deletes, renames and folders moved in or out (editor-style delete-then-rename saves keep the row); startup scan drops entries for files deleted while the indexer was off, but only under watch paths that currently exist, so an unplugged drive doesn't wipe its entries
- [x] Purge finished jobs — the AI worker deletes `Done` jobs older than `AIWorker:KeepDoneJobsDays` (7) every hour; `Failed` jobs are kept
- [x] Add index health dashboard to UI — clicking the status line opens per-machine counts and last activity, the job queue by stage and status, and the 20 most recent failures with their errors
- [x] Document setup steps for adding a new machine — `docs/adding-a-machine.md`, `docs/deployment.md`

### Phase 6 — Accounts, access control and hardening
Decided: per-user accounts, per-machine access, LAN only for now (§12 #4, #10).
- [x] Accounts — `users` (role `Admin`/`Member`, hashed password, disabled flag, lockout counters), `user_sessions` (only a SHA-256 of the session token is stored), `machine_access` (user × machine, role `Owner`/`Member`)
- [x] DB privileges — the API login may write only the three auth tables; indexer and AI-worker logins lose all access to them (an indexed desktop holds its indexer password, and must not be able to read hashes or forge a session). Checked on a fresh Compose stack: both get `permission denied` on `users`; the API still can't write `files`
- [x] First-run setup — while no user exists the API logs a one-time setup code; whoever enters it creates the first admin. Afterwards registration is closed and admins create accounts with a temporary password that must be changed at first sign-in
- [x] Sessions — opaque random token in an `HttpOnly`, `SameSite=Strict` cookie (`Secure` over HTTPS), idle and absolute expiry, sign out (this device / everywhere), list own sessions
- [x] Sign-in protection — rate limit per client address, lockout per account after repeated failures, same response and timing for unknown users and wrong passwords
- [x] CSRF — state-changing requests need a custom header (forces a CORS preflight that the API never grants cross-origin) on top of `SameSite=Strict`
- [x] Authorization everywhere — every `/api` endpoint except setup/sign-in requires a session; search, file details, previews, status and machine lists only cover machines the user can see. Admins see all machines
- [x] Sharing — admins assign a machine's owner; owners add or remove people and choose owner or member; members can search the machine but not share it (API: `/api/machines/{id}/access`; tested with 59 end-to-end checks covering all of the above)
- [x] Security headers — CSP, `X-Content-Type-Options`, `Referrer-Policy`, `frame-ancestors 'none'`, `Permissions-Policy` from nginx and the API. nginx overwrites `X-Forwarded-For`, so a spoofed one can't dodge the sign-in rate limit (checked)
- [x] UI — setup, sign-in and forced password change screens; account menu (change password, sessions, sign out); admin user management; machine sharing in the index status dialog; 401 returns to sign-in
- [x] Docs — first-run setup, adding people, sharing machines, recovering a lost admin password from the database

### Phase 7 — Security audit fixes
From the first full security audit (2026-11-20; report kept outside the repo). Severity in brackets.
- [x] Indexer reads only files under its watch paths [high ×2] — `IndexScope` requires a path to resolve under a configured root, pass the exclude patterns, and reach the file without crossing a symlink or junction. ParseWorker checks every job before opening it (queue rows come from a database every indexer can write), and the tree walk and watcher skip links
- [x] Per-machine database logins with row-level security [medium] — `nunam_indexer` is a group; each machine gets its own login (`docker/add-indexer-login.sh`). `machines.db_role` defaults to the registering login, and policies on `machines`, `files` and `index_jobs` limit an indexer login to that machine. Grants live in migrations instead of default privileges; the AI worker can update files and jobs but not add files or read machines; the API only reads; no service login can touch migration history
- [x] Bounded parser work [medium] — images read their header size first and get metadata only above `Indexer:MaxImageMegapixels` (100); JPEG/WebP decode at reduced scale; scanned-PDF pages render straight at preview size; Excel reads populated cells only; each parse has a deadline (`Indexer:ParseTimeoutSeconds`, 300); jobs that keep stopping a worker fail after `MaxAttempts` (3) claims; shutdown returns in-flight jobs without using an attempt. Checked: a 1 MB 16000×16000 PNG, a 305-byte 7200 pt PDF and a 1.5 KB A1:XFD1048576 workbook together peaked at 333 MB (was over 3 GB)
- [x] AI queue keeps moving [medium] — failures are classified by outcome: no answer or 502/503/504/429 is an outage (job back to Pending, attempt refunded, worker backs off); another server error or a client timeout counts against that job and fails it after `AIWorker:MaxAttempts` (3); a refusal fails it at once. A rejected embed batch is retried file by file. Checked against a local stand-in model: a poisoned image failed after 3 tries while the next machine's image was described; a poisoned embed input failed alone; with no backend, jobs stayed Pending with no attempts used
- [x] API work limits [low/medium] — search text is capped at 500 characters and 50 distinct words, and each search runs under `Search:StatementTimeoutSeconds` (15; 503 when hit). Thumbnail sizes round up to 128/256/512/1024/1568, previews over 16 MP are refused instead of decoded, and JPEGs decode at reduced scale. `to=9999-12-31` is a validation error instead of a 500
- [x] Sign-in lockout counter updated atomically [unverified by the audit] — one `UPDATE … RETURNING` counts the failure and sets the lock, so concurrent wrong guesses can't overwrite each other's count. Checked: 9 simultaneous wrong passwords locked the account (was: count stayed at 1)
- [x] Hardening notes from the same audit — Postgres requires TLS for every TCP login (self-signed certificate made at first init; indexers pin it with `SSL Mode=VerifyCA`, and `add-indexer-login.sh` prints it); the Windows service runs as `NT SERVICE\Nunam.Indexer` with read access granted to each watch path; the Linux unit is sandboxed (read-only filesystem, private /tmp, syscall filter; `systemd-analyze security` 1.6); base images are pinned by digest; ffmpeg/ffprobe accept only plain files read by video container demuxers; roles, search mode and type parse by name only; admin demotions and deletions are serialized so one enabled admin always remains; previews are revalidated on every load (`no-cache` + ETag) so revoked access ends at once; nginx trusts forwarded address and scheme only from `NUNAM_TRUSTED_PROXY`; the startup scan drops entries a watch path or exclude pattern no longer covers, keeping those under a watch path that is offline

### Phase 8 — Whole-drive indexing and installers
- [x] Whole drive as a watch path, Linux — `/` works as a root (it never matched before); `Indexer:SkipSystemFolders` (on by default) skips the OS and program folders (`/usr`, `/etc`, `/var`, `/opt`, `/proc`, …), hidden folders in home folders and package caches inside a root, but not a root chosen inside one of them. Exclude patterns now match only below the root. The watcher goes folder by folder down to each skipped system folder instead of one recursive watch, which on `/` put an inotify watch on all of `/proc`. Checked on this workstation: `/` went from 543 k files to 227 k (home folders, `/srv`, `/mnt`), nothing under a skipped folder was queued, 28 watchers, no errors
- [x] Whole drive as a watch path, Windows — `C:\` skips `Windows`, `Program Files`, `ProgramData`, `Recovery`, the recycle bin, update and setup folders, page and hibernation files, `Users\Default`, and in each profile `AppData`, hidden folders and the registry hives. Checked on the Windows 10 VM: 22 watchers (profiles and other top-level folders), a write in Documents was seen and one in `AppData` was not
- [x] Linux packages — `deploy/indexer/linux/build-packages.sh` publishes self-contained (no .NET runtime needed) and builds `.deb` and `.rpm` in Debian/Fedora containers: binaries in `/usr/lib/nunam-indexer`, settings in `/etc/nunam-indexer` (root:nunam 0640, kept on upgrade), sandboxed unit running as the `nunam` system user (sysusers.d), ffmpeg/leptonica/acl recommended. Version from `<Version>` in `Nunam.Indexer.csproj`. Checked in clean containers: Debian 13 and 12, Fedora 44 install and parse all 16 test files (OCR with either leptonica, video frames); upgrade keeps edited settings; remove and purge clean up; booted systemd ran the real unit as `nunam` and indexed a private home folder after the `setfacl` grants, including a file created later. `ProtectProc=invisible` was dropped from the unit: it hid PID 1, so .NET never told systemd it was ready and the start timed out (`systemd-analyze security` 1.8)
- [x] Windows installer — `deploy/indexer/windows/build-msi.sh` builds a self-contained `.msi` on Linux with wixl (msitools). It installs to `Program Files\Nunam\Indexer` and registers the service as `NT SERVICE\Nunam.Indexer`, stopped. A Start-menu *Configure Nunam Indexer* shortcut runs `Configure-Indexer.ps1` (replaces `install-windows.ps1`), which edits the settings, locks down the settings file, sets delayed start and restart-on-failure, grants the watch paths and starts the service. The MSI runs it unattended after each install and upgrade. A watch path holding profiles (`C:\`, `C:\Users`) is granted per profile folder, not on the drive. wixl records no file versions or hashes, so `REINSTALLMODE=amus` forces files to be copied; without it an upgrade deleted all but 20 files. Checked on the Windows 10 VM: silent install, configure, indexing `C:\` (444 files; OCR and video work under the virtual account; nothing from Windows, Program Files or AppData), upgrade 0.1.0 → 0.1.1 → 0.1.2 keeping the settings and restarting the service, and uninstall (service, files and shortcut removed, settings file kept). Not checked: the Start-menu shortcut's elevation prompt and Notepad step, which need the desktop
- [ ] macOS — deferred; built and tested on a Mac later

### Phase 9 — Managing indexers from the UI
- [x] Choose the Windows service account — *Configure Nunam Indexer* asks whether the service runs as its own virtual account (grants per watch path), the signed-in user (reads what they can, shares included, no grants) or another account; `-Account`/`-Credential` do the same from a script. For a user account it grants *Log on as a service* (`LsaAddAccountRights`; sc.exe and WMI don't) and checks the password with a service logon before saving it through WMI, so it never appears on a command line. The MSI no longer uses `ServiceInstall`, which deletes and recreates the service on every upgrade and so lost the password: `Configure-Indexer.ps1 -Register` creates the service after InstallFiles, and an uninstall that isn't part of an upgrade deletes it with sc.exe. Indexer 0.3.2. Checked on the Windows 10 VM: 0.3.1 → 0.3.2 upgrade (service recreated on the virtual account, delayed start, quoted path), LocalSystem kept across 0.3.2 → 0.3.3, the menu switching back with grants, uninstall removing the service and a fresh install. The signed-in user's account end to end (password typed on the VM desktop): service runs as `.\Admin`, both watch paths readable, and an upgrade keeps the account and password and restarts it. That upgrade found that Windows stores local accounts as `.\name`, which NTAccount can't translate; the script maps it to the computer name.
- [x] Choose the Linux service user — `sudo nunam-indexer-account [<user>|--default]` (in `/usr/sbin`, both packages) writes a systemd drop-in, `/etc/systemd/system/nunam-indexer.service.d/account.conf`, with `User=<user>`, `Group=nunam` (keeps the root:nunam 0640 settings file readable) and the user's primary group as a supplementary group, which systemd otherwise leaves out. No password and no ACLs; the unit's sandbox still applies. Upgrades keep it; a deb purge removes it; root is refused. Checked in a Fedora 44 container with systemd as PID 1: `nunam` couldn't open a 0700 home, the user could and its file was indexed, the menu, `--default`, refusals and a package reinstall keeping the user.
- [x] Status and logs — each indexer writes `indexer_status` every 30 s: version, OS, service account, state (running/scanning/paused), last scan, watch paths it can or can't open, and the settings in effect with their source (default/machine/set here) and no secrets. Its own log entries at Information and up, and everyone else's at Warning and up, go to `indexer_logs` every 5 s from a buffer that survives database outages (oldest dropped past 5000). The indexer deletes its own entries after 7 days
- [x] Remote management, off unless the machine's own settings set `Indexer:RemoteManagement` — commands in `indexer_commands` (restart and rescan, pause/resume parsing, retry failed files), picked up within 5 s and answered with a result. Commands sent to a machine that doesn't allow it are refused by the API (409), and rejected by the indexer if one gets through. Settings in `indexer_settings` (machine name, watch paths, excludes, skip system folders, size limits, OCR) override the machine's own values. Each change bumps a revision, and the indexer restarts to apply it. Nothing that names a program or data folder can be set remotely
- [x] Restart without stopping the service — the process now only keeps the Windows/systemd service, logging and an `IndexerSupervisor`; the workers run in an inner host that the supervisor rebuilds to restart. Remote settings are read before every start, so a scan never runs on the local watch paths alone and drops the remote ones. A failed start (database down) is retried with backoff instead of ending the process
- [x] Access — row security limits each indexer login to its own machine's status, logs, commands (read/complete) and settings (read only). The API reads all four and writes only commands and settings. Everyone who can see a machine sees its status and queue; logs, settings and actions are for admins and the machine's owners. Checked with real roles: no cross-machine status, log or command writes, and no API writes to status or logs
- [x] UI — *Machines* page (`/machines/<id>`): machine list with online/paused/offline, files, queued and failed; detail with Overview, Settings (override form plus the settings in effect), Logs (level filter, paging) and Activity (command history); Share from there. Checked in headless Firefox as admin and as an owner; pause, resume and a settings change worked end to end, and the Windows VM (0.2.0 MSI) restarted in place from the page
- Indexer version 0.2.0: older indexers don't report and show as *never reported*

### Phase 10 — Open-source document parsing
- [x] Replace Syncfusion — PDF text and metadata with PdfPig; scanned-PDF preview and OCR pages rendered with PDFium; .docx/.dotx/.docm/.dotm and .pptx family streamed with the Open XML SDK (`OpenXmlReader`, no DOM, parts capped at 200 M characters); .xls/.xlsx with ExcelDataReader, numbers and dates shown through ExcelNumberFormat, stopping after 20 M cells visited; binary .doc from its piece table; .rtf with a tokenizer; OCR through the `tesseract` program. Encrypted PDFs, .docx and workbooks get `{"encrypted": true}`. Indexer version 0.3.0. Checked against a throwaway database with 18 files (Word in four formats with Tamil text, a table and a link; .xls/.xlsx; .pptx; text, scanned and encrypted PDFs; photos; the 16000×16000 PNG, 7200 pt PDF and A1:XFD1048576 workbook bombs, each under a second, 260 MB peak): from the dev build, the Debian 13/12 and Fedora 44 packages with the distro Tesseract, and the 0.3.0 MSI upgraded over 0.2.0 on the Windows VM (without Tesseract: everything but OCR, with a warning). OCR text of scans is now plain lines instead of one word per line. Packages shrank: MSI 147 → 80 MB. `scanned-receipt.pdf` still reads as nothing, as it did with Syncfusion
- [x] Pluggable providers — every parser, OCR engine and PDF renderer has a name, behind contracts in the new `Nunam.Indexer.Abstractions` (`IFileParser`, `IOcrEngine`, `IPdfRenderer`, `ParseRequest`, `ParseOutcome`; assembly version pinned at 1.0.0.0). `Indexer:Parsers` maps an extension to the parsers to try in order: a parser that declines or throws passes the file on; if one threw and only the name-only fallback is left, the job fails with that error. Unlisted extensions get every parser whose `CanParse` accepts the file, plugins and commands first. `Indexer:OcrEngine`/`PdfRenderer` choose `tesseract`/`pdfium`/`none`, a command or a plugin. `Indexer:Commands` runs any program (file path or stdin in, text on stdout, timeout, output capped at the content limit) as a parser or OCR engine; `Indexer:PluginFolder` (default `plugins/` beside the indexer) loads one assembly per subfolder into its own load context sharing the contracts. Parsers/OCR engine/PDF renderer can be set from the Machines page, built-in names only (validated in Core, so the API answers 400). Word split into `word97`, `openxml-word`, `rtf`. Sample plugin `samples/Nunam.Indexer.Plugins.OpenDocument` (.odt/.ods/.odp). Indexer 0.3.1. Checked: plugin + `strings` command for .ppt + Tesseract as a command OCR engine + `.rtf → fallback` (all 21 test files went to the expected parser); fail-then-succeed and fail-only chains; remote `.rtf → fallback` and OCR `none` applied as revision 1 and reported as *set here*; API rejected a command name, a bad extension and an unknown OCR engine; Debian 13/12 and Fedora 44 packages with the plugin installed (ODF read, OCR through the distro Tesseract); 0.3.1 MSI over 0.3.0 on the Windows VM with the plugin in `Program Files\Nunam\Indexer\plugins`
- [ ] OCR on Windows checked with Tesseract installed

### Phase 11 — Android indexer (proposed)
- [x] Feasibility spike — a throwaway .NET 10 Android app (`net10.0-android`, API 26+) linking `Nunam.Indexer/Parsing/*.cs` unchanged except `VideoParser` and `Pdfium`, with Android's `PdfRenderer` as an `IPdfRenderer` and Google ML Kit text recognition (bundled Latin model, `Xamarin.Google.MLKit.TextRecognition` 116.0.1.9) as an `IOcrEngine`. On the Android 16 emulator with *All files access* (`MANAGE_EXTERNAL_STORAGE`) it read all 27 files in shared storage, including another app's photo, every parser gave the same result as on the desktop, and EF Core + Npgsql wrote the rows straight to Postgres (3–4.5 s for the set). ML Kit read the bill photo and scanned PDF exactly and found text in a receipt Tesseract missed. Trimmed release APK, x86_64 only, with ML Kit: 31.6 MB; trimming didn't break EF Core. Not covered: background scheduling, a real phone, arm64, Tamil OCR (ML Kit has no Tamil; Tesseract4Android does).
- [x] Decide: direct Postgres with a per-machine login on home Wi-Fi, or an API upload endpoint with revocable device tokens — the API, for every indexer (Phase 12); a phone enrolls like any machine
- [ ] Split the parsers and parse pipeline out of `Nunam.Indexer` into a library both heads share
- [ ] `Nunam.Indexer.Android`: setup screen (server, *All files access*), WorkManager job on charging + unmetered, incremental scan by modified time and MediaStore generation, Android video parser (MediaMetadataRetriever), PDF renderer and OCR providers selectable by name
- [ ] Distribution: GitHub Releases (sideload). Play allows *All files access* for on-device search as the core purpose, after a declaration

### Phase 12 — Indexers and the AI worker through the API
- [x] Agent API — `/api/agent/indexer/*` (hello, settings, poll, status, command results, logs, scan batches, indexed-path pages, removals, parse claim/result/recover/retry-failed) and `/api/agent/worker/*` (claim with the file's preview or text, results, recover), behind a bearer scheme that never reads cookies and is exempt from the CSRF header. Machine tokens (SHA-256 stored on `machines`) scope every indexer call to their machine; the worker token is `Agents:WorkerToken`. The server now decides the AI follow-up of a parse and drops results for jobs stale recovery took back (409). Hourly clean-up of done jobs, logs older than 7 days and spent codes moved into the API. Migration `MoveAgentsToApi`: `db_role` and row-level security gone, `token_hash`, `machine_enrollments`, hostname no longer unique, every right of the indexer and AI-worker logins revoked, `nunam_api` read-write on the index
- [x] Indexer 0.4.0 over the API — no database access or EF; results the server can't take wait in memory and are sent before the next claim, changes seen while it's down are retried every 30 s, a refused result (400/413) fails its job. Not enrolled: logs how to connect and exits. `Nunam.Indexer enroll` trades a code for the token and writes `appsettings.Local.json` (in place, so it keeps its permissions; a new file on Windows starts with Administrators and SYSTEM only); `Nunam.Indexer watch-paths` prints the watch paths in effect, UI ones included, so *Configure Nunam Indexer* grants those too. Optional `Server:CertificateSha256` pins a self-signed certificate
- [x] AI worker over the API — same Describe/Embed logic and backoff, results kept until the server takes them
- [x] Add machine from the web UI — admins create a one-time code (an hour); the dialog shows a Windows and a Linux command and follows the code until the machine connects. `install.ps1`/`install.sh` (served by the API) download the newest package from `Agents:PackageFolder`, install it, enroll, ask for the service account and start it. *Reconnect* (admins and owners) rotates a machine's token and keeps its files; *Remove* (admins) deletes it. The dialog warns when the page is on plain http
- [x] Server stack — Postgres unpublished, `NUNAM_AIWORKER_PASSWORD` replaced by `NUNAM_WORKER_TOKEN`, `add-indexer-login.sh` gone, packages mounted into the API. Checked: a throwaway Compose project built from source on a fresh volume (only `nunam_api` created) took a Fedora 44 container (systemd) enrolled through nginx with the one-line command, a watch path set from the UI, and parse → describe → embed of 7 files with the real Ollama; nginx took a 31 MB agent body and still refused 100 kB elsewhere. Dev: 28-file set parsed, described and embedded; watcher add/delete, pause, a settings restart, the API stopped and restarted under a running indexer (the change made meanwhile landed, no restart), code reuse refused, old token 401 after reconnect, members refused codes and removal. Windows VM: 0.3.4 → 0.4.0 with the one-line command as a reconnect, its 41 files and `.\Admin` account kept. Found and fixed on the way: `curl | sh` handing the rest of the script to dnf as stdin (now `main "$@"` and `</dev/null`), and Windows' default execution policy blocking `Configure-Indexer.ps1` from the pasted command
- [x] HTTPS without an outside proxy — `nunam-ui` serves 443 (`UI_HTTPS_PORT`, 3443) with `tls/cert.pem` + `key.pem`, created self-signed (P-256, 10 years) on first start when absent; `NUNAM_TLS_CERT`/`NUNAM_TLS_KEY` point at other files. It publishes the certificate's SHA-256 and public-key hash at `/certificate.json` (https port only, not through a trusted proxy), and when the certificate doesn't verify against public CAs (`NUNAM_TLS_PIN=auto|yes|no`) the *Add machine* commands pin it: `curl -k --pinnedpubkey` on Linux, `curl.exe` on Windows (Windows PowerShell's certificate callbacks run without a runspace, so `irm` can't pin), and `--certificate-sha256` for the indexer
- [ ] Update the homelab server: new `.env` keys, pull, drop the old logins (`docs/deployment.md`, *Moving from 0.3*)

### Rollout — after features, testing and review are done

- [ ] Images on GitHub Packages — `.github/workflows/images.yml` builds `nunam-{api,ui,aiworker,migrations}` for linux/amd64 on every push to `master` and pushes `ghcr.io/devil7dk/nunam-*:latest` and `:<yyyymmdd>-<sha7>` with the workflow's `GITHUB_TOKEN` (layer cache in GitHub Actions; actions pinned by commit; nothing runs for pull requests). `deploy/server` pulls from there (`NUNAM_REGISTRY=ghcr.io/devil7dk`); the Harbor registry is no longer used. Not run yet: needs the first push; check the packages are public afterwards
- [ ] Versioned releases — publishing a GitHub release tagged `vX.Y.Z` runs `.github/workflows/release.yml`: the tag must match `Version` in `Nunam.Indexer.csproj` (plain X.Y.Z, since the MSI and rpm take nothing else); images pushed as `X.Y.Z` and `X.Y` and attached as `docker save` archives (`nunam-*-X.Y.Z-amd64.tar.gz`); the indexer's `.msi`, `.deb` and `.rpm` built by the existing scripts (Ubuntu 24.04's wixl 0.103 builds the MSI) and attached; `nunam-server-X.Y.Z.tar.gz` with `deploy/server`, the Postgres init scripts and `NUNAM_TAG=X.Y.Z`. Not run yet: needs the first release
- [x] Deploy the server — its own LXC (`nunam`, 192.168.7.118, Debian 13, 4 vCPU, 8 GB, 32 GB) rather than the shared docker-host, so the index and its LAN-facing Postgres sit apart from the other apps. Laid out like the other app LXCs: `deploy/server/` (`compose.yaml` on registry images, `.env.example`, runbook) copied to `/srv/docker` with `.env` at 0600 and the folder in a local git repo. Images `registry.dineshkumar.dev/nunam/nunam-{migrations,api,ui,aiworker}` tagged `latest` and `<date>-<commit>`; the registry has no `nunam` project yet, so the first build was loaded onto the LXC over SSH. Checked: migrations applied, UI and API answer from the LAN, setup code issued, Postgres refuses non-TLS logins
- [ ] Install the Indexer on the desktop and laptop (`docs/adding-a-machine.md`)
- [ ] Website, nunam.dineshkumar.dev — `website/` (Vite + React, the same theme, font and components as the AES Camera site): landing page and `/docs/<name>` rendered from `docs/*.md` at build time. Builds, lints and fits a 390 px phone without sideways scrolling. Not deployed yet: it needs a Cloudflare Pages project (root `website`, build `yarn build`, output `dist`) and the custom domain. The download button points at GitHub Releases

## 12. Open Questions / Decisions

| # | Question | Default Decision |
|---|---|---|
| 1 | Should the Indexer also run on homelab for NFS/SMB shares? | Yes — run a separate Indexer instance on homelab pointed at mounted shares |
| 2 | How to handle files >500MB (large videos)? | Extract metadata only, skip content extraction |
| 3 | Should embedding be regenerated on every file change? | Only when the AI input changed: a new preview image re-describes; a changed name, content or description re-embeds. The parse result is compared with the stored row, so there is no hash column |
| 4 | Authentication for the web UI? | Per-user accounts stored in Postgres with server-side sessions (Phase 6). No external identity provider: the homelab has none. LAN only for now; remote access would add two-factor sign-in first |
| 5 | Should thumbnails be stored in DB or generated on demand? | Generated on demand and cached in memory |
| 6 | What to do with encrypted PDFs? | Skip content, set `metadata = {"encrypted": true}`; the file stays searchable by name. `Description` is left to the AI worker |
| 7 | Should the Indexer write directly to DB or via API? | **Via the API** (since 0.4). Each indexer signs in with its own machine token, and every agent endpoint is scoped to that machine; the AI worker uses a shared worker token. Postgres is no longer on the LAN and `nunam_api` is the only login on the tables. Up to 0.3 indexers wrote directly with per-machine logins under row-level security; the move made adding a machine a copied command instead of a login, certificate and connection string, and gives phones (Phase 11) a revocable way in. Scans send batches of 500 paths with size and time, so bulk indexing costs one request per batch. |
| 8 | One `IndexJob` table or many? | **One table, `Kind` column** discriminates Parse / Describe / Embed. Same shape, single set of migrations, per-kind queues fall out of `WHERE Kind=...`. Inspired by Immich's BullMQ queue separation but adapted to Postgres-as-queue. |
| 9 | Where does Ollama live? | **Homelab GPU container** (`192.168.7.113:11434`, `ollama/ollama:rocm`). Originally planned for the Gaming PC. AIWorker still tolerates it being unreachable: jobs stay `Pending`. |
| 10 | Who sees which machine's files? | Users see only machines they've been given. An admin assigns each machine an owner; owners share it with others as owner or member. Admins see every machine. New machines are visible to admins only until someone is given access |

---

## 13. NuGet Packages Summary

### Nunam.Core
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.8" />            <!-- explicit floor to align consumers -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.8" /> <!-- floor: stops consumers pulling Relational 10.0.4 via Npgsql -->
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.2" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />
<PackageReference Include="EFCore.NamingConventions" Version="10.0.1" />        <!-- snake_case columns/tables -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.8">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
<PackageReference Include="Microsoft.Extensions.AI" Version="10.6.0" />                  <!-- IChatClient + IEmbeddingGenerator abstractions + DI -->
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.6.0" />           <!-- OpenAI-compatible backend (LM Studio) -->
<PackageReference Include="OllamaSharp" Version="5.4.25" />                              <!-- Ollama native backend (implements M.E.AI interfaces) -->
<PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.8" />  <!-- bind AIOptions from config -->
```

> **Tooling:** install the EF Core CLI once per machine: `dotnet tool install --global dotnet-ef` (10.0.8 at time of writing).
>
> **Why the explicit `Microsoft.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore.Relational 10.0.8` pins:** `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2` declares EF Core `>= 10.0.4`, while `Microsoft.EntityFrameworkCore.Design 10.0.8` (PrivateAssets=all) brings EF Core 10.0.8 into Core's own compile but **doesn't propagate** to consumers like `Nunam.AIWorker`. Without the pins, the consumer resolves 10.0.4 (for both `EntityFrameworkCore` and `EntityFrameworkCore.Relational`) while `Nunam.Core.dll` was compiled against 10.0.8 — MSBuild warns (MSB3277) and you risk MissingMethodException at runtime if Core ever calls a 10.0.8-only API. Pinning both packages in Core (non-private) makes 10.0.8 the floor for everyone. The `Relational` pin was added when `Nunam.AIWorker` referenced Core and surfaced the same MSB3277 conflict on `Microsoft.EntityFrameworkCore.Relational`.
>
> **Known version skew:** `Pgvector.EntityFrameworkCore 0.3.0` declares a dependency on `Npgsql 8.0.5`, but `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2` brings `Npgsql 10.0.3`. NuGet resolves to the higher version (10.0.3), which works in practice; flag this as the first suspect if pgvector serialization misbehaves at runtime.

### Nunam.Indexer
```xml
<!-- All AGPL-compatible: Apache-2.0, MIT, BSD, MPL-2.0 -->
<PackageReference Include="PdfPig" Version="0.1.16" />
<PackageReference Include="bblanchon.PDFium.Linux" Version="160.0.8165" />
<PackageReference Include="bblanchon.PDFium.Win32" Version="160.0.8165" />
<PackageReference Include="DocumentFormat.OpenXml" Version="3.5.1" />
<PackageReference Include="ExcelDataReader" Version="3.9.0" />
<PackageReference Include="ExcelNumberFormat" Version="1.2.0" />
<PackageReference Include="OpenMcdf" Version="3.4.0" />
<PackageReference Include="SkiaSharp" Version="4.150.1" />
<PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" Version="4.150.1" />
<PackageReference Include="Mime-Detective" Version="25.8.1" />
<PackageReference Include="MetadataExtractor" Version="2.9.3" />
<PackageReference Include="TagLibSharp" Version="2.3.0" />
<PackageReference Include="FFMpegCore" Version="6.0.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="*" />
```

### Nunam.AIWorker
```xml
<PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.6" />
<ProjectReference Include="..\Nunam.Core\Nunam.Core.csproj" />   <!-- pulls in IAIInferenceService + AddNunamAI -->
```

> **Note:** `Program.cs` calls `builder.Services.AddNunamAI(builder.Configuration)`. The active backend comes from the `AI` config section — both `appsettings.json` and `appsettings.Development.json` point at **Ollama in the homelab GPU container** (`192.168.7.113:11434`, `gemma4:12b-it-qat` + `bge-m3`). LM Studio still works via `AI:Provider=OpenAICompatible`.

### Nunam.Api
```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="*" />
```

> **Note:** `Nunam.API` references `Nunam.Core` and inherits EF Core transitively. Set `Nunam.API` as the startup project when running EF migrations.

---

*2026-06-07 — Verified the LM Studio backend end-to-end and wired it into `Nunam.AIWorker`. Reachability: LM Studio server on `localhost:1234/v1` serves `text-embedding-nomic-embed-text-v1.5` (confirmed **768 dims**, matches the `vector(768)` column) and vision chat. **Model bake-off** for the Describe stage on an RX 6600 (8 GB): `qwen/qwen3-vl-4b` (Q4_K_M, 3.33 GB) vs `google/gemma-4-e4b` (Gemma 3n) on the same image — Qwen finished an accurate description in **8.3 s / 132 tokens / 0 reasoning**, while Gemma took **114 s**, burned **517 reasoning tokens**, and was **truncated before finishing**. Picked **Qwen3 VL 4B** as the LM Studio `ChatModel`. Added `AIOptions.MaxOutputTokens` (default 1024) and passed it via `ChatOptions.MaxOutputTokens` in `DescribeImageAsync` — guards against reasoning-style models exhausting the budget on "thinking" and returning empty/truncated text. Wired `Nunam.AIWorker`: added a `ProjectReference` to Core, called `AddNunamAI(builder.Configuration)` in `Program.cs`, and added the `AI` config section (`appsettings.json` → Ollama/Gaming-PC production default; `appsettings.Development.json` → LM Studio/localhost). Pinned `Microsoft.EntityFrameworkCore.Relational 10.0.8` in Core to clear a new MSB3277 conflict the worker reference surfaced. Worker builds clean (0 warnings) and boots cleanly in Development.*

*2026-06-07 — Implemented the AI inference service in `Nunam.Core/AI/`, replacing the planned `OllamaService`. Renamed to provider-neutral `IAIInferenceService`/`AIInferenceService` built on `Microsoft.Extensions.AI` (`IChatClient` + `IEmbeddingGenerator`). `AddNunamAI(IConfiguration)` reads `AI:Provider` and registers either OllamaSharp (native API) or the OpenAI SDK pointed at an OpenAI-compatible `/v1` endpoint (LM Studio) — switching backends is config-only. Service exposes single + batch `EmbedAsync` (returns pgvector `Vector`) and `DescribeImageAsync` (vision, per-call prompt override). Added packages `Microsoft.Extensions.AI 10.6.0`, `Microsoft.Extensions.AI.OpenAI 10.6.0`, `OllamaSharp 5.4.25`, `Microsoft.Extensions.Options.ConfigurationExtensions 10.0.8` to Core. Core builds clean. Added the provider-neutral `AI` config section to §6. Still pending: register `AddNunamAI` in `Nunam.AIWorker`/API hosts and verify the GPU machine's backend is reachable.*

*Last updated: 2026-06-05 — Applied `InitialSchema` migration to local `nunam_dev` database. Reused the user's existing Postgres docker-compose at `C:\Users\Dineshkumar\Desktop\Files\docker\postgresql\docker-compose.yml`, swapped image from `postgres:16` → `pgvector/pgvector:pg16` (volume-compatible drop-in). Confirmed pgvector 0.8.2 available; all tables, HNSW (`vector_cosine_ops`) + GIN + B-tree indexes, alternate key `(machine_id, path)`, FK cascades, and `trg_update_fts` trigger present. Updated `NunamDbContextFactory` connection string to point at `nunam_dev`. Noted a benign collation version mismatch warning (database created under old image, fixable with `ALTER DATABASE nunam_dev REFRESH COLLATION VERSION` if it becomes noisy).*

*2026-06-05 — Generated initial EF Core migration (`InitialSchema`) under `Nunam.Core/Data/Migrations/`. Added `PreviewImage` (bytea) column to `IndexedFile` entity (was in plan, not yet in code). Added `NunamDbContextFactory : IDesignTimeDbContextFactory<NunamDbContext>` so `dotnet ef` works without requiring the startup project to register the DbContext via DI yet. Added `ProjectReference` from `Nunam.API` → `Nunam.Core` and added `Microsoft.EntityFrameworkCore.Design 10.0.8` to API (required by EF tools since Core's copy has `PrivateAssets=all`). FTS trigger appended to migration's `Up()` as raw SQL (concatenates `file_name + content + description` via `to_tsvector('english', ...)`), drop in `Down()`. Verified via `dotnet ef migrations script --idempotent`. Schema apply still blocked on Postgres being set up.*

*2026-06-05 — Implemented `NunamDbContext` + `TouchEntityInterceptor` under `Nunam.Core/Data/`. Added `EFCore.NamingConventions 10.0.1` for snake_case columns/tables (matches §7 raw-SQL examples). Enums (`FileType`, `IndexJobKind`, `IndexJobStatus`, `IndexJobStage`) stored as strings via `HasConversion<string>()` for psql debuggability. HNSW (`vector_cosine_ops`) + GIN (`fts`) + `(MachineId, Path)` alt key + new `(Kind, Status)` index on `IndexJob` for queue polling. Cascade delete from Machine. Resolves §5 open question on `LastModified` update — interceptor writes via `entry.Property(...).CurrentValue` so the `private set` stays sealed off from C# callers. Snake-case + interceptor wired in `OnConfiguring` so consumers don't have to remember.*

*2026-06-05 — Scaffolded `Nunam.AIWorker` project: skeleton `Workers/DescribeWorker.cs` + `Workers/EmbedWorker.cs` (currently log-only stubs), `Program.cs` registers both, `appsettings.json` carries the configured shape (Postgres conn, OllamaUrl, model names, per-queue concurrency, batch size, poll interval), Dockerfile mirrors `Nunam.Indexer`'s pattern (multi-stage build with Core copied alongside), VS container debug profile added, project referenced from `Nunam.slnx`. Pinned `Microsoft.EntityFrameworkCore 10.0.8` explicitly in `Nunam.Core` to resolve an MSB3277 version-mismatch warning surfaced when AIWorker referenced Core (Npgsql.EF brought 10.0.4 transitively, but Core was compiled against 10.0.8 from Design's transitive — explicit pin floors everyone to 10.0.8).*

*2026-06-04 — Reversed earlier "one job per file, full pipeline inside" decision after the hardware split clarified (Ollama on Gaming PC, everything else on homelab). Adopted Immich-inspired multi-queue model: one `IndexJob` row per (file, task), single table with `Kind` column (`Parse | Describe | Embed`). Parse runs on indexed machines; Describe + Embed run on a new `Nunam.AIWorker` project on the homelab, talking to Ollama over LAN. Added `IndexJobKind` enum + `IndexJob.Kind` property; added `IndexedFile.PreviewImage` (bytea) so AIWorker doesn't need filesystem access. Rewrote §6 around the new pipeline; added §10 hardware-split notes; added §12 Q8/Q9; split Phase 2 into Parse / AI sub-phases.*

*2026-06-04 — Decided job granularity: one `IndexJob` per file, full pipeline inside it (no per-stage job splitting). Added `IndexJobStage` enum (`None`/`Detect`/`Parse`/`Describe`/`Embed`/`Upsert`) and `IndexJob.Stage` property for pipeline-progress observability. Documented job-granularity reasoning in §6. **(Superseded by 2026-06-04 entry above.)**

*2026-06-04 — Added Phase 1 entities (`Machine`, `IndexedFile`, `IndexJob`, `FileType`, `IndexJobStatus`) under `Nunam.Core/Entities`. Renamed folder `Models` → `Entities`. All entities inherit from existing `EntityBase` (provides `Id`/`Created`/`LastModified`). `IndexedFile.CreatedAt`/`ModifiedAt` renamed to `FileCreatedAt`/`FileModifiedAt` to disambiguate from `EntityBase.Created`. All timestamps now `DateTimeOffset`. Status fields are enums (`IndexJobStatus`) instead of free strings. Removed template-generated `Class1.cs`.*

*2026-06-04 — Installed EF Core 10 stack into `Nunam.Core`: `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2`, `Pgvector.EntityFrameworkCore 0.3.0`, `Microsoft.EntityFrameworkCore.Design 10.0.8`. Pinned versions in §13 (previously `9.*` / `*`). Noted Npgsql 8 vs 10 transitive skew.*

*2026-06-04 — Adopted flat repo layout (projects at root, no `src/` folder); fixed project name casing (`Nunam.API`, `Nunam.WebUI`); updated EF migration and docker-compose paths to match.*

*2026-06-04 — Added EF Core ORM; added MimeDetective for magic-byte file type detection; decided Indexer writes directly to DB using a dedicated low-privilege user.*

---

## 14. Development Environment & Third-Party Setup

How the pieces outside the repo were set up, so they can be rebuilt. No secret values here.

### Dev database (Linux workstation)
- Docker container `nunam-dev-postgres` (image `pgvector/pgvector:pg18`) on `127.0.0.1:5442`, database `nunam_dev`, user `postgres` / password `postgres`. Port 5432 on this machine belongs to another project.
- Created with:
  ```bash
  docker run -d --name nunam-dev-postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=nunam_dev \
    -p 127.0.0.1:5442:5432 pgvector/pgvector:pg18
  docker exec nunam-dev-postgres psql -U postgres -d nunam_dev -c 'CREATE EXTENSION IF NOT EXISTS vector;'
  dotnet tool install --global dotnet-ef --version 10.0.8
  dotnet ef database update --project Nunam.Core --startup-project Nunam.Core
  ```
- `appsettings.Development.json` of the API and `NunamDbContextFactory` point at this container. The dev indexer and AI worker talk to the API on `localhost:5033`: the AI worker with the dev worker token in both Development configs, the indexer with a token from `Nunam.Indexer enroll --server http://localhost:5033 --code <code>` (a code from *Machines* → *Add machine*), which writes `appsettings.Local.json` beside the built executable.

### Dev AI backend
- Development uses the homelab Ollama directly (`192.168.7.113:11434`, `gemma4:12b-it-qat` + `bge-m3`) — the Development configs of the API and AIWorker point at it, no overrides needed. A local Ollama on this workstation also has `bge-m3`.

### Document parsing libraries
- **Syncfusion was dropped (2026-11-21, Phase 10)** so the code can be AGPL-3.0: its Community License covers only its own licensees and its source isn't available, so nobody else could redistribute an AGPL indexer linked to it. The old key on syncfusion.com (Community license, Document SDK, v35) is no longer used; any `Syncfusion:LicenseKey` left in a machine's settings or user-secrets is ignored.
- **NPOI was not used:** from 2.8 its NuGet binaries come with an "Open Source Maintenance Fee" agreement (a fee for users with revenue over US$10k). 2.7.x is still plain Apache-2.0, but ExcelDataReader + ExcelNumberFormat cover .xls/.xlsx with streaming reads.
- **PDFium** comes from `bblanchon.PDFium.Linux`/`.Win32` (natives only); `Parsing/Pdfium.cs` is the P/Invoke wrapper. PDFium isn't thread-safe, so every call takes one lock (parses run one at a time anyway).
- **OCR is the `tesseract` program**, run per page with the image on stdin and text on stdout (`OMP_THREAD_LIMIT=1`, 2-minute timeout). Found via `Indexer:OcrTesseractPath`, then PATH, then `Program Files\Tesseract-OCR` on Windows. Linux packages recommend `tesseract-ocr` + `tesseract-ocr-eng` (Fedora: `tesseract` + `tesseract-langpack-eng`); Windows: `winget install UB-Mannheim.TesseractOCR`, not bundled. Other languages are the distro's language packs.
- **Dev without a system Tesseract:** a Debian container image `nunam-dev-tesseract` (tesseract 5.5 with eng and tam) behind a two-line wrapper script set as `Indexer__OcrTesseractPath`; the wrapper runs `docker run -i --rm --network none nunam-dev-tesseract tesseract "$@"`.
- **Configuration binder gotcha:** binding config to an array property that has a default *appends* to the default. `CommandOptions.Arguments` therefore defaults to empty and means `{file}` when empty; a `["{file}"]` default made Tesseract get the path before `stdin`.
- **Plugin contracts version:** the installers publish with `-p:Version=<indexer version>`, which applies to referenced projects too, so the contracts would ship as 0.3.x while plugins ask for 1.0.0.0 and fail with FileNotFoundException. `AssemblyVersion` is pinned in the project, and the plugin load context hands every plugin the indexer's copy by name.
- **Binary .doc** text is read from the Word 97 piece table (`Word97.cs`, [MS-DOC] 2.4.1) on top of OpenMcdf; title/author/subject/word count from the SummaryInformation stream (`OleProperties.cs`). Word 6/95 files get name only. **RTF** is a small tokenizer (`RtfText.cs`).

### Per-machine prerequisites for the Indexer
- **ffmpeg + ffprobe** for video metadata and frames. Linux: distro package (`dnf install ffmpeg`). Windows: e.g. `winget install Gyan.FFmpeg`, then either have them on PATH or set `Indexer:FFmpegBinaryFolder` to the folder holding `ffmpeg.exe`/`ffprobe.exe`. Missing ffmpeg isn't fatal: videos are indexed by name only, with a warning in the log.
- **Tesseract** for OCR (see above). Without it, images and scanned PDFs are indexed without their text and the log says so once.
- PDFium and SkiaSharp natives come from NuGet.
- **Verified 2026-11-19 on Linux** with generated samples: an image with printed text and the same image as a scanned PDF (OCR read both exactly), a 3000×4000 JPEG with EXIF rotation/camera/GPS (stored upright as 4000×3000, metadata extracted), a tagged MP3, an MP4 with a title tag (frame grabbed and described). Semantic queries "electricity bill payment", "family celebration video", "devotional music" each ranked the matching file first.
