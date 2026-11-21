# Nunam server

The stack is `compose.yaml` beside this file. Its settings are in `.env`, which is `chmod 600` and holds the passwords; `.env.example` says what each one is for.

| Service | What it is | Reachable at |
| --- | --- | --- |
| `nunam-ui` | the web UI, and the proxy to the API | `UI_PORT` (3000) on the LAN |
| `postgres` | the index: files, text, previews, vectors, accounts | `POSTGRES_PORT` (5432) on the LAN, TLS only |
| `nunam-api` | search, previews, sign-in | inside the stack only, through `nunam-ui` |
| `nunam-aiworker` | describes images and embeds files, with the Ollama server | nowhere; it takes jobs |
| `migrations` | applies the schema, then exits | nowhere |

Postgres is on the LAN because each indexer writes to it directly. Every login made with `add-indexer-login.sh` can read and write only its own machine's rows, and must verify the server's certificate.

## Bringing it up the first time

```bash
cd /srv/docker
cp .env.example .env && chmod 600 .env
# Fill in the three passwords, AI_BASE_URL and TZ.
docker compose up -d
docker compose logs nunam-api | grep "setup code"
```

Open `http://<this machine>:3000` and enter the setup code with a user name and password; that account is the first admin. The code works only while there are no accounts.

The `postgres/` scripts run once, when the data volume is created: `10-roles.sh` makes the service logins from the passwords in `.env`, and `20-tls.sh` makes the self-signed certificate that indexers pin. Changing those passwords in `.env` later does not change them in the database.

## Adding a machine

```bash
./add-indexer-login.sh <machine-name> <this machine's address>
```

It prints the certificate to save on that machine and the connection string for its `appsettings.Local.json`. Installing the indexer itself is in *Adding a machine to the index* in the Nunam docs.

## Updating

```bash
docker compose pull
docker compose up -d
```

`migrations` runs on every `up` and skips what is already applied; the API and AI worker start after it. To stay on one build, set `NUNAM_TAG` to its dated tag (`20261121-63eec70`) instead of `latest`.

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
