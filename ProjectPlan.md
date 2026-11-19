# Nunam — Project Plan

> **Nunam** (नूनम्, Sanskrit) — *"certainly / surely / indeed"*
> A self-hosted, AI-powered file search engine for your local network.

---

## 1. Overview

Nunam indexes all files across multiple machines on the same local network (desktop, laptop, homelab server) and provides a fast, intelligent web-based search interface. It supports keyword search, full-text content search, metadata search, and natural language queries powered by a locally hosted LLM.

### Goals
- Index files and metadata across multiple machines (documents, images, audio, video)
- Use OCR and a local multimodal LLM (LLaVA) to understand unrecognized files
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
│          │  │(LLaVA +   │ │                              │
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
| Document Parsing | Syncfusion Essential Studio | PDF, DOCX, XLSX, PPTX, OCR |
| Image metadata | MetadataExtractor (NuGet) | EXIF, IPTC, XMP |
| Audio metadata | TagLibSharp (NuGet) | MP3, FLAC, AAC, etc. |
| Video metadata | FFMpegCore (NuGet) | Duration, codec, resolution |
| File type detection | MimeDetective (NuGet) | Detects true file type by magic bytes, not extension |
| File watching | FileSystemWatcher (.NET built-in) | Incremental re-indexing |
| LLM runtime | Ollama **or** LM Studio (self-hosted) | Pluggable via config; runs on the GPU machine |
| Vision model | llava:13b (or any vision model on LM Studio) | Describes unknown/image files |
| Embeddings | nomic-embed-text | 768-dim vectors |
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
│   │   ├── PdfParser.cs             # Syncfusion.PDF
│   │   ├── WordParser.cs            # Syncfusion.DocIO
│   │   ├── ExcelParser.cs           # Syncfusion.XlsIO
│   │   ├── PowerPointParser.cs      # Syncfusion.Presentation
│   │   ├── ImageParser.cs           # MetadataExtractor + Syncfusion OCR
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
│   │   ├── DescribeWorker.cs        # Consumes Kind=Describe jobs (LLaVA via Ollama)
│   │   └── EmbedWorker.cs           # Consumes Kind=Embed jobs (nomic-embed-text via Ollama, batched)
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
            e.Property(f => f.Embedding).HasColumnType("vector(768)");

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
| `Describe` | Send rendered preview to LLaVA, store description | `Nunam.AIWorker` on homelab | Network → Ollama on Gaming PC (GPU) |
| `Embed` | Embed `filename + description + content` with `nomic-embed-text`, store vector | `Nunam.AIWorker` on homelab | Network → Ollama on Gaming PC (GPU) |

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
    │      application/pdf                       → PdfParser           (Syncfusion)
    │      application/vnd.openxmlformats.word   → WordParser          (Syncfusion)
    │      application/vnd.openxmlformats.sheet  → ExcelParser         (Syncfusion)
    │      application/vnd.openxmlformats.pres   → PowerPointParser    (Syncfusion)
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
    │      ├─ POSTs to Ollama generate API with llava:13b + image
    │      ├─ UPDATE IndexedFile.Description
    │      └─ UPDATE this job to Status=Done
    │
    └─ EmbedWorker (Concurrency: 4, batched by N)
           ├─ Pulls up to N pending Embed jobs (preferring ones whose Describe is Done)
           ├─ For each: build embedding input = filename + description + first 512 tokens of content
           ├─ POSTs all N in one nomic-embed-text call
           ├─ UPDATE IndexedFile.Embedding for each — file is now SEMANTICALLY-SEARCHABLE
           └─ UPDATE all these jobs to Status=Done
```

### Why this model

- **Independent failure domains.** LLaVA timing out doesn't fail Embed jobs; Embed jobs failing for one file don't fail the batch's other 31. The user-visible result: search keeps improving over time as bits of the AI pipeline catch up, instead of a wall of `Failed` rows whenever Ollama hiccups.
- **Files become keyword-searchable immediately after Parse,** without waiting for AI. Semantic search lights up later as Embed completes.
- **Right batch size per workload.** LLaVA processes one big image per call; embeddings batch beautifully (16–32 inputs per HTTP request). Separate queues let each worker use the right strategy.
- **Different hardware profiles cleanly separated.** Parse is CPU + local IO on the user's machine; Describe / Embed are network calls from the always-on homelab to the intermittently-on Gaming PC. The homelab AI worker can keep retrying when Ollama is down without surfacing failures to users.
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
    "BaseUrl": "http://gaming-pc.lan:11434",     // LM Studio: "http://gaming-pc.lan:1234/v1"
    "ApiKey": null,                              // ignored by Ollama/LM Studio
    "ChatModel": "llava:13b",
    "EmbeddingModel": "nomic-embed-text",
    "EmbeddingDimensions": 768,
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
3. **semantic** — nearest `Search:CandidatesPerSide` (200) embeddings, then only those within `Search:SemanticRelativeMargin` (0.08) of the best similarity. Cosine similarity has no absolute meaning across queries with nomic-embed-text (a right answer scored 0.54 for one query while noise scored 0.59 for another), so a fixed floor doesn't work; relative to the top hit it does.
4. **fused** — reciprocal rank fusion, `1/(60 + keyword rank) + 1/(60 + semantic rank)`. Chosen over the originally planned `ts_rank × 0.4 + similarity × 0.6`: `ts_rank` values are ~0.01–0.1 while similarity is ~0.5–0.8, so a weighted sum is decided almost entirely by the semantic side.
5. **page** — limit/offset, then `ts_headline` only for the returned rows.

Query embedding uses the `search_query: ` prefix and documents use `search_document: ` (`AI:EmbeddingQueryPrefix` / `AI:EmbeddingDocumentPrefix`), as nomic-embed-text is trained. **Changing either prefix or the model means re-embedding every file:**
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
GET /api/machines              # Machines with file count and last indexed time
GET /healthz                   # 200 when the database is reachable
```

`POST /api/index/trigger` (from the original plan) is **not implemented**: the API runs as the read-only `nunam_api` DB user (§12 #7). Re-indexing a path today = insert a `Parse` job, or touch the file so the watcher picks it up.

CORS: `Cors:AllowedOrigins` (GET only); `*` allows any origin. Development allows `http://localhost:5173` (Vite).

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

| Model | Purpose | Size |
|---|---|---|
| `nomic-embed-text` | Generate embeddings | ~270MB |
| `llava:13b` | Describe unknown/image files | ~8GB |
| `llama3.2:3b` *(optional)* | Intent parsing for complex NL queries | ~2GB |

Pull on homelab:
```bash
ollama pull nomic-embed-text
ollama pull llava:13b
```

---

## 10. Deployment

Step-by-step guides: **`docs/deployment.md`** (homelab stack) and **`docs/adding-a-machine.md`** (indexers).

**Hardware split:**
- **Homelab** — Docker Compose (`docker/docker-compose.yml`): `postgres` (pgvector, Postgres 18, port published for indexers), `migrations` (one-shot EF bundle, runs as the owner role), `nunam-api` (read-only `nunam_api` login, not published), `nunam-ui` (nginx on `UI_PORT`, proxies `/api`), `nunam-aiworker` (`nunam_aiworker` login). Settings and passwords in `docker/.env` (template `docker/.env.example`, git-ignored).
- **Gaming PC** — Ollama only, outside Compose (`OLLAMA_HOST=0.0.0.0:11434`). Jobs wait `Pending` while it's off.
- **Indexed machines** — `Nunam.Indexer` natively: Windows service via `deploy/indexer/install-windows.ps1`, Linux via `deploy/indexer/nunam-indexer.service`. Per-machine settings and secrets in `appsettings.Local.json` next to the executable (template `deploy/indexer/appsettings.Local.example.json`, git-ignored); it overrides `appsettings.json`, environment variables override both. The content root is always the executable's folder.

Database logins are created by `docker/postgres/10-roles.sh` on first start of the volume: `nunam_indexer` and `nunam_aiworker` get SELECT/INSERT/UPDATE/DELETE, `nunam_api` SELECT only, all through default privileges on tables the owner creates.

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
- [x] Implement `PdfParser` using Syncfusion.PDF — text of every page + page count/title/author; under 50 chars of text means a scan, so page 1 is rendered to PNG (`Syncfusion.PdfToImageConverter.Net`, PDFium) and queued for Describe; password-protected PDFs get `{"encrypted": true}` metadata and no content
- [x] Implement `WordParser` using Syncfusion.DocIO — `.doc/.docx/.docm/.dot/.dotx/.dotm/.rtf`, text + title/author/subject/word count
- [x] Implement `ExcelParser` using Syncfusion.XlsIO — `.xls/.xlsx/.xlsm/.xltx/.xltm`, sheet names + displayed cell text, one row per line
- [x] Implement `PowerPointParser` using Syncfusion.Presentation — `.pptx` family only (Syncfusion can't open binary `.ppt`; it falls back to filename-only), shape/group/table text + speaker notes
- [x] Cap stored content at `MaxContentChars` (500 000) — the FTS trigger's `to_tsvector` fails above 1 MB — and strip NULs centrally in `ParseWorker`
- [x] Register the Syncfusion license at Indexer startup from `Syncfusion:LicenseKey` (see §14)
- [x] Implement `ImageParser` (EXIF via MetadataExtractor + OCR via Syncfusion.OCR) — camera make/model, date taken, GPS, pixel size into metadata; OCR text (kept only when it has ≥ 3 real words, so photos don't fill `content` with noise) into content; preview scaled to `PreviewMaxEdgePx` (1568) with EXIF rotation applied, JPEG unless transparent. Over `MaxPreviewMb`: EXIF only
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
- [ ] Configure Indexer as Windows service on desktop and laptop — *ready, not run*: hosting supports Windows services and systemd, `deploy/indexer/install-windows.ps1` publishes, installs and secures the settings file. Needs running on each Windows machine (untested on Windows so far)
- [x] Set up pgvector HNSW index tuning — build parameters left at pgvector's defaults (`m=16`, `ef_construction=64`; ample recall for a home index). The real problem was query-time: an HNSW scan returns at most `hnsw.ef_search` rows (default 40), so semantic search silently capped at 40 candidates once the index is used. Search now sets `hnsw.ef_search` to `Search:CandidatesPerSide` and `hnsw.iterative_scan = relaxed_order` per transaction (verified on 20 000 vectors: 40 → 200)
- [x] Add re-index on file delete/move — watcher handles deletes, renames and folders moved in or out (editor-style delete-then-rename saves keep the row); startup scan drops entries for files deleted while the indexer was off, but only under watch paths that currently exist, so an unplugged drive doesn't wipe its entries
- [x] Purge finished jobs — the AI worker deletes `Done` jobs older than `AIWorker:KeepDoneJobsDays` (7) every hour; `Failed` jobs are kept
- [x] Add index health dashboard to UI — clicking the status line opens per-machine counts and last activity, the job queue by stage and status, and the 20 most recent failures with their errors
- [x] Document setup steps for adding a new machine — `docs/adding-a-machine.md`, `docs/deployment.md`

## 12. Open Questions / Decisions

| # | Question | Default Decision |
|---|---|---|
| 1 | Should the Indexer also run on homelab for NFS/SMB shares? | Yes — run a separate Indexer instance on homelab pointed at mounted shares |
| 2 | How to handle files >500MB (large videos)? | Extract metadata only, skip content extraction |
| 3 | Should embedding be regenerated on every file change? | Yes, if content hash changes |
| 4 | Authentication for the web UI? | None initially (LAN-only), add basic auth in Phase 5 if needed |
| 5 | Should thumbnails be stored in DB or generated on demand? | Generated on demand and cached in memory |
| 6 | What to do with encrypted PDFs? | Skip content, set `metadata = {"encrypted": true}`; the file stays searchable by name. `Description` is left to the AI worker |
| 7 | Should the Indexer write directly to DB or via API? | **Direct DB write** — avoids HTTP overhead during bulk indexing, indexer uses a dedicated low-privilege `nunam_indexer` DB user (INSERT/UPDATE/SELECT only). API uses a separate `nunam_api` read-only user. AIWorker uses `nunam_aiworker` with INSERT/UPDATE/SELECT on `index_jobs` + `files` only. |
| 8 | One `IndexJob` table or many? | **One table, `Kind` column** discriminates Parse / Describe / Embed. Same shape, single set of migrations, per-kind queues fall out of `WHERE Kind=...`. Inspired by Immich's BullMQ queue separation but adapted to Postgres-as-queue. |
| 9 | Where does Ollama live? | **Gaming PC** (the only machine with a GPU). AIWorker calls it over LAN. Tolerates intermittent availability via Postgres-backed retry (jobs stay `Pending` while Ollama is down). |

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
<!-- Installed (all Syncfusion packages must share one version; the license key is per major version) -->
<PackageReference Include="Syncfusion.Licensing" Version="35.2.12" />
<PackageReference Include="Syncfusion.Pdf.Net.Core" Version="35.2.12" />
<PackageReference Include="Syncfusion.PdfToImageConverter.Net.Core" Version="35.2.12" />
<PackageReference Include="Syncfusion.PDF.OCR.Net.Core" Version="35.2.12" />
<PackageReference Include="Syncfusion.DocIO.Net.Core" Version="35.2.12" />
<PackageReference Include="Syncfusion.XlsIO.Net.Core" Version="35.2.12" />
<PackageReference Include="Syncfusion.Presentation.Net.Core" Version="35.2.12" />
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

> **Note:** `Program.cs` calls `builder.Services.AddNunamAI(builder.Configuration)`. The active backend comes from the `AI` config section — `appsettings.json` defaults to **Ollama on the Gaming PC** (production), and `appsettings.Development.json` overrides to **LM Studio on `localhost:1234/v1`** with `qwen/qwen3-vl-4b` + `text-embedding-nomic-embed-text-v1.5` (verified working on this machine).

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
  dotnet ef database update --project Nunam.Core --startup-project Nunam.Indexer
  ```
- `appsettings.Development.json` (Indexer, AIWorker) and `NunamDbContextFactory` point at this container.

### Dev AI backend
- Ollama on `localhost:11434` with `qwen2.5vl:7b` (vision) and `nomic-embed-text` (pulled with `ollama pull nomic-embed-text`). The Development config still targets LM Studio; override per run with `AI__Provider=Ollama AI__BaseUrl=http://localhost:11434 AI__ChatModel=qwen2.5vl:7b AI__EmbeddingModel=nomic-embed-text`.

### Syncfusion (document parsing)
- **License:** Essential Studio Enterprise Edition — **Community license** on the syncfusion.com account.
- **Key generated (2026-11-19):** syncfusion.com → Account → *License & Downloads* → *Downloads & Keys* → **Get License Key**, with *Version* `35.x.x`, *Platform* **Document SDK** (covers PDF, DocIO, XlsIO, Presentation, OCR), *Project* `Nunam`. Since v31.1.17 keys are issued per product (UI Edition, Document SDK, DOCX/PDF/Spreadsheet Editor SDK), not per platform.
- **The key only works for major version 35.** Upgrading the packages to 36+ needs a new key generated the same way.
- **Where the key lives:** never in git.
  - Dev: `dotnet user-secrets set "Syncfusion:LicenseKey" "<key>" --project Nunam.Indexer` (stored under `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json` on Linux, `%APPDATA%\Microsoft\UserSecrets\` on Windows; set it again on each machine).
  - Deployed indexers: environment variable `Syncfusion__LicenseKey`.
  - Without a key the Indexer logs a warning and Syncfusion runs in evaluation mode.
- **Packages:** come from nuget.org; no installer download is needed. The Linux "Download for Linux" installer on the account page isn't used.
- **Use `Syncfusion.PdfToImageConverter.Net.Core`, not `.Net`:** `Syncfusion.PDF.OCR.Net.Core` depends on the `.Net.Core` one, and having both defines `PdfToImageConverter` twice (CS0433).
- **Linux native bits:** `Syncfusion.PdfToImageConverter.Net.Core` ships PDFium for linux/win/osx itself, but its SkiaSharp dependency needs `SkiaSharp.NativeAssets.Linux.NoDependencies` pinned to the same SkiaSharp version Syncfusion resolves (4.150.1 for 35.2.12). Check `obj/project.assets.json` for the SkiaSharp version when bumping Syncfusion.
- **Verified** with generated samples (text PDF, image-only PDF, password-protected PDF, .docx, .xlsx, .pptx): content and metadata extracted with no evaluation watermark text; the image-only PDF was rendered, described by the vision model, then embedded.
- **OCR:** `Syncfusion.PDF.OCR.Net.Core` bundles Tesseract (4.0 and 5.0 builds for win/linux/osx, copied under `runtimes/` at build) and `eng.traineddata` only. Other languages: download `<lang>.traineddata` (e.g. `tam` from github.com/tesseract-ocr/tessdata) into a folder, set `Indexer:OcrTessDataPath` to it (copy `eng.traineddata` there too) and `Indexer:OcrLanguage` to e.g. `eng+tam`.
- **OCR on Linux needs a `libdl` shim:** Syncfusion's OCR imports `libdl` by its unversioned name, which glibc 2.34+ only installs with `glibc-devel`. `OcrService` maps it to `libdl.so.2` with a `NativeLibrary.SetDllImportResolver` on the OCR assembly, so no system change is needed. The symptom without it: `DllNotFoundException: Unable to load shared library 'libdl'`.

### Per-machine prerequisites for the Indexer
- **ffmpeg + ffprobe** for video metadata and frames. Linux: distro package (`dnf install ffmpeg`). Windows: e.g. `winget install Gyan.FFmpeg`, then either have them on PATH or set `Indexer:FFmpegBinaryFolder` to the folder holding `ffmpeg.exe`/`ffprobe.exe`. Missing ffmpeg isn't fatal: videos are indexed by name only, with a warning in the log.
- **Syncfusion license key** set on the machine (user-secrets or `Syncfusion__LicenseKey`).
- Nothing else: Tesseract, PDFium and SkiaSharp natives come from NuGet.
- **Verified 2026-11-19 on Linux** with generated samples: an image with printed text and the same image as a scanned PDF (OCR read both exactly), a 3000×4000 JPEG with EXIF rotation/camera/GPS (stored upright as 4000×3000, metadata extracted), a tagged MP3, an MP4 with a title tag (frame grabbed and described). Semantic queries "electricity bill payment", "family celebration video", "devotional music" each ranked the matching file first.
