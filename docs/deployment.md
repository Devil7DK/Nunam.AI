# Deploying the homelab stack

The homelab runs Postgres (with pgvector), the API, the web UI and the AI worker in Docker. Indexers run natively on each machine with files ([adding a machine](adding-a-machine.md)); Ollama runs on the machine with the GPU.

## 1. Ollama

Ollama runs on a machine with a GPU, for example in the `ollama/ollama` container (`ollama/ollama:rocm` for AMD cards). It needs:

```bash
ollama pull gemma4:12b-it-qat   # describes images, scanned pages and video frames
ollama pull bge-m3              # 1024-dim multilingual embeddings; must match the vector(1024) column
```

and `OLLAMA_HOST=0.0.0.0:11434` so other machines can reach it. Set `AI_BASE_URL` in `docker/.env` to its address.

The AI worker tolerates Ollama being unreachable: Describe and Embed jobs wait as `Pending` and resume when it's back. Search falls back to keywords meanwhile.

Describing can also run on a cloud model instead of, or next to, Ollama; see [Describing with other models](#describing-with-other-models).

## 2. Start the stack

```bash
cp docker/.env.example docker/.env
# edit docker/.env: the two passwords, the worker token, AI_BASE_URL, ports
docker compose -f docker/docker-compose.yml up -d --build
```

| Service | What it does |
|---|---|
| `postgres` | pgvector on Postgres 18. Not published: only the stack reaches it. |
| `migrations` | Applies EF Core migrations as the owner role, then exits. The API waits for it. |
| `nunam-api` | Search, sign-in, and the agent API that indexers and the AI worker use, as `nunam_api`, the only login on the tables. Purges finished jobs and indexer logs older than 7 days. Not published; the UI proxies to it, and it trusts nginx's `X-Forwarded-For` for the sign-in rate limit. |
| `nunam-ui` | nginx serving the UI on `UI_PORT` (3000, http) and `UI_HTTPS_PORT` (3443) with security headers, and forwarding `/api` to the API. |
| `nunam-aiworker` | Describe + Embed queues, signed in to the API with `NUNAM_WORKER_TOKEN`. |

Open `https://homelab:3443` and accept the certificate warning, or `http://homelab:3000` ([HTTPS](#https) has the difference).

## 3. First sign-in

The first visit asks for a **setup code**, which the API prints to its log once no account exists:

```bash
docker compose -f docker/docker-compose.yml logs nunam-api | grep "setup code"
```

Enter it with a user name and password to create the first admin. From then on setup is closed. A new code is printed each time the API starts, but it only works while there are no accounts.

**Adding people:** account menu → *People* → *Add a person*. Nunam generates a temporary password to hand over; the person picks their own at first sign-in. Admins can also reset passwords (which signs the person out everywhere), disable accounts, and make others admin.

**Machines page:** *Machines* in the header shows each indexer's status, and to admins and owners its logs and settings ([managing it from the web UI](adding-a-machine.md#managing-it-from-the-web-ui)).

**Who sees what:** admins see every machine. Everyone else sees only machines shared with them. A newly added machine is visible to admins only until someone shares it: *Machines* → the machine → *Share*. Owners of a machine can share it further; members can only search it.

**Sessions** last 14 days without use and 90 days at most (`Auth:SessionIdleDays`, `Auth:SessionMaxDays`). Each person can see their signed-in devices and sign them out from the account menu. Five wrong passwords lock an account for 15 minutes, and each address gets 10 attempts a minute.

**Lost the only admin password?** If another account exists, promote it and reset the admin from there:

```sql
UPDATE users SET role = 'Admin' WHERE user_name = 'someone';
```

With no other account, delete the accounts and restart the API to get a new setup code. Machine sharing is lost with them; indexed files are not.

```sql
DELETE FROM users;
```

Nunam is built for the LAN. The session cookie is marked `Secure` only when the page was loaded over HTTPS, so plain-HTTP access works; anyone who can watch LAN traffic could read a session cookie there. Put an HTTPS reverse proxy in front before exposing it beyond the LAN, and set `NUNAM_TRUSTED_PROXY` in `.env` to that proxy's address (as nginx in `nunam-ui` sees it, e.g. `172.18.0.1/32`). Only then does nginx pass on the client address and `https` scheme the proxy reports; without it, every request looks like it came from the proxy, so all users share one sign-in limit, and the cookie isn't marked `Secure`.

**Database login.** `docker/postgres/10-roles.sh` creates the API's login the first time the `pgdata` volume is initialised; changing its password in `.env` later does not change it in the database (`ALTER ROLE nunam_api PASSWORD '…'`). Keep it free of `;`, since Npgsql connection strings split on it. `docker/postgres/20-tls.sh` gives Postgres a self-signed certificate, and every TCP login must use TLS.

**Indexer packages.** *Machines* → *Add machine* shows a command that downloads the indexer from this server. The API hands out the newest `.msi`, `.deb` and `.rpm` from `artifacts/packages` (mounted at `/packages`; on a server running registry images, `packages/` beside `compose.yaml`). Build them with `deploy/indexer/windows/build-msi.sh` and `deploy/indexer/linux/build-packages.sh`. Without a package, the dialog shows a command for a machine where the indexer is already installed.

**Machine tokens.** Each indexer signs in with its own token, which it gets once with a single-use code from *Add machine* (valid an hour). The token reads and writes only that machine's files and jobs. *Reconnect* on a machine gives it a new token and keeps its files; *Remove* deletes the machine, its files and its token.

## HTTPS

Indexers send their token with every request, and browsers their session cookie. Over plain http both can be read by anyone watching LAN traffic, so open the UI on its https port before adding machines: the *Add machine* command uses the address the page was opened with.

**Self-signed (the default).** On first start `nunam-ui` creates a certificate and key in `docker/tls/` (`tls/` beside `compose.yaml` on a server running registry images) and keeps using them. Browsers warn about it once per device. The *Add machine* command pins it: curl gets `--pinnedpubkey`, and the indexer stores the certificate's SHA-256 in `Server:CertificateSha256` and accepts that certificate and no other. The fingerprint is in the container's log and under the command:

```bash
docker compose -f docker/docker-compose.yml logs nunam-ui | grep certificate
```

`NUNAM_TLS_NAMES` (comma-separated, e.g. `nunam.lan,192.168.1.20`) adds names and addresses to the certificate when it is created, for anyone who wants to install it as trusted. Deleting the two files makes a new certificate on the next start; machines that pinned the old one stop connecting until they are reconnected.

**Your own certificate.** Put `cert.pem` (the certificate followed by any intermediates) and `key.pem` in that folder before the first start, or replace the generated pair and restart `nunam-ui`. To use files from elsewhere, mount them and point `NUNAM_TLS_CERT` and `NUNAM_TLS_KEY` at them, e.g. in a `docker-compose.override.yml`:

```yaml
services:
  nunam-ui:
    volumes:
      - /etc/letsencrypt:/etc/letsencrypt:ro
    environment:
      NUNAM_TLS_CERT: /etc/letsencrypt/live/nunam.example.org/fullchain.pem
      NUNAM_TLS_KEY: /etc/letsencrypt/live/nunam.example.org/privkey.pem
```

After a renewal, `docker compose restart nunam-ui`. The command pins a certificate only when a public authority didn't issue it (`NUNAM_TLS_PIN=auto`). A certificate from your own CA is pinned too, since machines without that CA can't check it; if the machines trust your CA, set `NUNAM_TLS_PIN=no` so a renewed certificate doesn't need every machine reconnected. `yes` always pins.

**Behind your own HTTPS proxy** (see `NUNAM_TRUSTED_PROXY` above), forward to the http port. The command then pins nothing, and machines must trust the proxy's certificate.

Over plain http the *Add machine* dialog says the token travels unencrypted. An indexer added that way can move to https with *Reconnect*.

## 4. Updating

```bash
git pull
docker compose -f docker/docker-compose.yml up -d --build
```

Migrations run on every `up`; already-applied ones are skipped.

Base images are pinned by digest (`FROM nginx:1.29-alpine@sha256:…`), so a rebuild uses exactly the image that was reviewed and never picks up a tag that was changed upstream; it also never picks up security fixes on its own. Refresh them every month or so: `docker buildx imagetools inspect <image:tag>` prints the current digest for each `FROM` line and the `postgres` image in the Compose file.

## Backups

All state is in the `nunam_pgdata` volume. Everything in it can be rebuilt by re-indexing, but descriptions and embeddings take GPU time to regenerate, so a nightly dump is worthwhile:

```bash
docker compose -f docker/docker-compose.yml exec -T postgres \
  pg_dump -U nunam -Fc nunam > nunam-$(date +%F).dump
```

## Describing with other models

Admins manage the models that describe images on the web UI's **AI** page. On the first start of this version, the server creates one provider, *Local Ollama* with `gemma4:12b-it-qat` at `AI_BASE_URL`, and credits it with the descriptions already written; `AI_CHAT_MODEL` is no longer read and can be removed from `.env`. More can be added: another Ollama server, or a cloud model through an OpenAI-compatible API (presets for Anthropic, OpenRouter and Gemini fill in the address and list prices).

- **Priority and concurrency.** The AI worker uses the providers with the lowest priority number; equal numbers share the queue. *At a time* sets how many images a provider describes in parallel: 1 suits a single local GPU, 4 to 8 a cloud API.
- **Backup or overflow.** A provider with a higher number is a backup: it describes only while every provider ahead of it is disabled, failing or over budget. With *Take overflow* it also describes while they are all busy, so a local GPU can work alongside a cloud model.
- **Budgets.** Each provider's input and output prices (US$ per million tokens) turn the tokens each request was billed for into spend. A budget stops the provider once it has spent that much, either in total or in each calendar month; the next provider takes over. Spend is counted per day, so a budget can overshoot by the few requests in flight.
- **Test** describes one random indexed image with the provider as saved and shows the text, tokens and cost; it counts toward the budget.
- Every description records the model and provider that wrote it, shown under *What it shows* on the file and counted on the AI page. The search page's *AI description* filter lists files with or without one.

Cloud APIs need a key, which the server stores encrypted with `NUNAM_SECRETS_KEY` from `.env`:

```bash
openssl rand -base64 32   # put it in .env as NUNAM_SECRETS_KEY, then: docker compose up -d
```

Keep that value: without it the stored keys can't be decrypted, and each provider's key has to be entered again. Only the AI worker receives the keys; the web UI shows whether one is stored, never the key. A cloud provider receives the preview of every file it describes, photos and scanned documents included.

## Changing the embedding model or prefixes

Stored vectors only match queries embedded the same way. After changing `AI_EMBEDDING_MODEL` to one with the same dimensions or the `AI__EmbeddingDocumentPrefix` / `AI__EmbeddingQueryPrefix` settings, re-embed everything:

```sql
INSERT INTO index_jobs (id, machine_id, path, kind, status, stage, created, last_modified)
SELECT gen_random_uuid(), machine_id, path, 'Embed', 'Pending', 'None', now(), now() FROM files;
```

A model with a different dimension needs a migration changing `vector(1024)` that clears the old vectors, alters the column and queues an Embed job for every file.
