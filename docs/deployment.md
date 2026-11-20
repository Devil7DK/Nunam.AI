# Deploying the homelab stack

The homelab runs Postgres (with pgvector), the API, the web UI and the AI worker in Docker. Indexers run natively on each machine with files ([adding a machine](adding-a-machine.md)); Ollama runs on the machine with the GPU.

## 1. Ollama

Ollama runs in its own GPU container on the homelab (`192.168.7.113`, `ollama/ollama:rocm`). It needs:

```bash
ollama pull gemma4:12b-it-qat   # describes images, scanned pages and video frames
ollama pull bge-m3              # 1024-dim multilingual embeddings; must match the vector(1024) column
```

and `OLLAMA_HOST=0.0.0.0:11434` so other machines can reach it. Set `AI_BASE_URL` in `docker/.env` to its address.

The AI worker tolerates Ollama being unreachable: Describe and Embed jobs wait as `Pending` and resume when it's back. Search falls back to keywords meanwhile.

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
| `nunam-api` | Search API and sign-in, as `nunam_api`: read-only on the index, read-write on the account tables. Not published; the UI proxies to it, and it trusts nginx's `X-Forwarded-For` for the sign-in rate limit. |
| `nunam-ui` | nginx serving the UI on `UI_PORT` (3000) with security headers, and forwarding `/api` to the API. |
| `nunam-aiworker` | Describe + Embed queues, as `nunam_aiworker`; purges finished jobs older than 7 days. |

Open `http://homelab:3000`.

## 3. First sign-in

The first visit asks for a **setup code**, which the API prints to its log once no account exists:

```bash
docker compose -f docker/docker-compose.yml logs nunam-api | grep "setup code"
```

Enter it with a user name and password to create the first admin. From then on setup is closed. A new code is printed each time the API starts, but it only works while there are no accounts.

**Adding people:** account menu → *People* → *Add a person*. Nunam generates a temporary password to hand over; the person picks their own at first sign-in. Admins can also reset passwords (which signs the person out everywhere), disable accounts, and make others admin.

**Who sees what:** admins see every machine. Everyone else sees only machines shared with them. A newly indexed machine is visible to admins only until someone shares it: *Index status* → *Share* on the machine's row. Owners of a machine can share it further; members can only search it.

**Sessions** last 14 days without use and 90 days at most (`Auth:SessionIdleDays`, `Auth:SessionMaxDays`). Each person can see their signed-in devices and sign them out from the account menu. Five wrong passwords lock an account for 15 minutes, and each address gets 10 attempts a minute.

**Lost the only admin password?** If another account exists, promote it and reset the admin from there:

```sql
UPDATE users SET role = 'Admin' WHERE user_name = 'someone';
```

With no other account, delete the accounts and restart the API to get a new setup code. Machine sharing is lost with them; indexed files are not.

```sql
DELETE FROM users;
```

Nunam is built for the LAN. The session cookie is marked `Secure` only when the page was loaded over HTTPS, so plain-HTTP access works; anyone who can watch LAN traffic could read a session cookie there. Put an HTTPS reverse proxy in front before exposing it beyond the LAN.

**Database logins** are created by `docker/postgres/10-roles.sh` the first time the `pgdata` volume is initialised. Changing a password in `.env` later does not change it in the database; use `ALTER ROLE nunam_indexer PASSWORD '…'` instead. Keep passwords free of `;` — Npgsql connection strings split on it.

## 4. Updating

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

Stored vectors only match queries embedded the same way. After changing `AI_EMBEDDING_MODEL` to one with the same dimensions or the `AI__EmbeddingDocumentPrefix` / `AI__EmbeddingQueryPrefix` settings, re-embed everything:

```sql
INSERT INTO index_jobs (id, machine_id, path, kind, status, stage, created, last_modified)
SELECT gen_random_uuid(), machine_id, path, 'Embed', 'Pending', 'None', now(), now() FROM files;
```

A model with a different dimension needs a migration changing `vector(1024)` (see `UseBgeM3Embeddings` for the pattern: clear vectors, alter the column, queue Embed jobs).
