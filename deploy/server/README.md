# Nunam server

The stack is `compose.yaml` beside this file. Its settings are in `.env`, which is `chmod 600` and holds the passwords; `.env.example` says what each one is for.

| Service | What it is | Reachable at |
| --- | --- | --- |
| `nunam-ui` | the web UI, and the proxy to the API | `UI_HTTPS_PORT` (3443) and `UI_PORT` (3000, http) on the LAN |
| `postgres` | the index: files, text, previews, vectors, accounts | inside the stack only, TLS only |
| `nunam-api` | search, previews, sign-in, and what indexers and the AI worker call | inside the stack only, through `nunam-ui` |
| `nunam-aiworker` | describes images and embeds files, with the Ollama server | nowhere; it takes jobs from the API |
| `migrations` | applies the schema, then exits | nowhere |

Indexers on other machines reach the API through the UI's port, each with its own machine token; nothing else is published.

## Bringing it up the first time

```bash
cd /srv/docker
cp .env.example .env && chmod 600 .env
# Fill in the two passwords, NUNAM_WORKER_TOKEN, AI_BASE_URL and TZ.
docker compose up -d
docker compose logs nunam-api | grep "setup code"
```

The images come from GitHub Packages (`ghcr.io/devil7dk/nunam-*`). Each [release](https://github.com/Devil7DK/Nunam.AI/releases) is tagged with its version (`0.4.0`, and `0.4` for the newest patch); `latest` and dated tags (`20261124-abc1234`) follow every push to `master`. Each release also attaches `nunam-server-<version>.tar.gz`, this folder with `NUNAM_TAG` set to that version, and the images as `docker load` archives for a server that can't reach ghcr.io.

Open `https://<this machine>:3443`, accept the self-signed certificate, and enter the setup code with a user name and password; that account is the first admin. The code works only while there are no accounts.

The `postgres/` scripts run once, when the data volume is created: `10-roles.sh` makes the API's login from its password in `.env`, and `20-tls.sh` makes the self-signed certificate Postgres requires for TCP logins. Changing those passwords in `.env` later does not change them in the database.

`nunam-ui` creates its own self-signed certificate in `tls/` on first start; the *Add machine* command pins it. To serve your own instead, put `cert.pem` (with intermediates) and `key.pem` there and restart `nunam-ui`. *HTTPS* in the deployment docs covers names, renewals and pinning.

## Adding a machine

Copy the indexer packages (`nunam-indexer-<version>-x64.msi`, `nunam-indexer_<version>_amd64.deb`, `nunam-indexer-<version>-1.x86_64.rpm`, attached to each [release](https://github.com/Devil7DK/Nunam.AI/releases)) into `packages/` beside this file; the newest of each is offered. Then, in the web UI, *Machines* → *Add machine* shows the command to run on the machine. *Adding a machine to the index* in the Nunam docs has the rest.

## Updating

```bash
docker compose pull
docker compose up -d
```

`migrations` runs on every `up` and skips what is already applied; the API and AI worker start after it. To stay on one release, set `NUNAM_TAG` to its version (`0.4.0`), or to `0.4` for its patches; a dated tag pins one build of `master`. Copy the same release's indexer packages into `packages/`.

## Looking at it

```bash
docker compose ps
docker compose logs -f nunam-api nunam-aiworker
docker compose exec postgres psql -U nunam -d nunam
```

## Backing it up

Everything is in the `nunam_pgdata` volume. It can all be rebuilt by indexing again, but descriptions and embeddings take hours of GPU time, so dump it nightly:

```bash
docker compose exec -T postgres pg_dump -U nunam -Fc nunam > backups/nunam-$(date +%F).dump
```
