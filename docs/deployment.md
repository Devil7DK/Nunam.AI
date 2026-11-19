# Deploying the homelab stack

The homelab runs Postgres (with pgvector), the API, the web UI and the AI worker in Docker. Indexers run natively on each machine with files ([adding a machine](adding-a-machine.md)); Ollama runs on the machine with the GPU.

## 1. Ollama on the GPU machine

```bash
ollama pull qwen2.5vl:7b        # describes images, scanned pages and video frames
ollama pull nomic-embed-text    # 768-dim embeddings; must match the vector(768) column
```

Ollama listens on `127.0.0.1` by default. Set `OLLAMA_HOST=0.0.0.0:11434` (Windows: a system environment variable, then restart Ollama) so the homelab can reach it, and allow port 11434 through the firewall for the LAN only.

The AI worker tolerates this machine being off: Describe and Embed jobs wait as `Pending` and resume when it's back. Search falls back to keywords while it's unreachable.

## 2. Start the stack

```bash
cp docker/.env.example docker/.env
# edit docker/.env: four passwords, AI_BASE_URL, ports
docker compose -f docker/docker-compose.yml up -d --build
```

| Service | What it does |
|---|---|
| `postgres` | pgvector on Postgres 18. Port `POSTGRES_PORT` (5432) is published because indexers on other machines write to it directly. |
| `migrations` | Applies EF Core migrations as the owner role, then exits. The API and AI worker wait for it. |
| `nunam-api` | Search API, reads as `nunam_api` (read-only). Not published; the UI proxies to it. |
| `nunam-ui` | nginx serving the UI on `UI_PORT` (3000) and forwarding `/api` to the API. |
| `nunam-aiworker` | Describe + Embed queues, as `nunam_aiworker`; purges finished jobs older than 7 days. |

Open `http://homelab:3000`.

**Database logins** are created by `docker/postgres/10-roles.sh` the first time the `pgdata` volume is initialised. Changing a password in `.env` later does not change it in the database; use `ALTER ROLE nunam_indexer PASSWORD '…'` instead. Keep passwords free of `;` — Npgsql connection strings split on it.

## 3. Updating

```bash
git pull
docker compose -f docker/docker-compose.yml up -d --build
```

Migrations run on every `up`; already-applied ones are skipped.

## Backups

All state is in the `nunam_pgdata` volume. Everything in it can be rebuilt by re-indexing, but descriptions and embeddings take GPU time to regenerate, so a nightly dump is worthwhile:

```bash
docker compose -f docker/docker-compose.yml exec -T postgres \
  pg_dump -U nunam -Fc nunam > nunam-$(date +%F).dump
```

## Changing the embedding model or prefixes

Stored vectors only match queries embedded the same way. After changing `AI_EMBEDDING_MODEL` (same dimensions) or the `AI__EmbeddingDocumentPrefix` / `AI__EmbeddingQueryPrefix` settings, re-embed everything:

```sql
INSERT INTO index_jobs (id, machine_id, path, kind, status, stage, created, last_modified)
SELECT gen_random_uuid(), machine_id, path, 'Embed', 'Pending', 'None', now(), now() FROM files;
```

A model with a different dimension needs a migration changing `vector(768)`.
