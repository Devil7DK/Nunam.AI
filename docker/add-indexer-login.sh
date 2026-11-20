#!/bin/bash
# Creates the database login for one indexed machine and prints its connection string.
# Usage, from the repository root on the homelab: docker/add-indexer-login.sh <machine-name> [db-host]
# The login can only read and write that machine's rows (row-level security); give each machine its own.
set -euo pipefail
cd "$(dirname "$0")"

name="${1:?usage: $0 <machine-name> [db-host]}"
host="${2:-homelab}"
role="nunam_idx_$(echo "$name" | tr '[:upper:]' '[:lower:]' | tr -c 'a-z0-9\n' '_')"
[ ${#role} -le 63 ] || { echo "Machine name too long." >&2; exit 1; }

# No ';' in the password: Npgsql connection strings split on it.
password="$(head -c 32 /dev/urandom | base64 | tr -d '/+=;\n' | cut -c1-32)"

# Settings come from the running container, not by sourcing .env: that file is Compose syntax, and a
# password with a quote in it is valid there but breaks the shell.
pg() { docker compose -f docker-compose.yml exec -T postgres "$@"; }
db="$(pg printenv POSTGRES_DB)"
port="$(docker compose -f docker-compose.yml port postgres 5432 | sed 's/.*://')"

pg sh -c 'psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v role="$1" -v pw="$2"' sh "$role" "$password" <<'EOSQL'
CREATE ROLE :"role" LOGIN PASSWORD :'pw' IN ROLE nunam_indexer;
EOSQL

echo "Created login $role. Put this in the machine's appsettings.Local.json (ConnectionStrings:Postgres):"
echo "Host=$host;Port=$port;Database=$db;Username=$role;Password=$password"
