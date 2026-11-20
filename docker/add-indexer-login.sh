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

set -a; . ./.env; set +a
# No ';' in the password: Npgsql connection strings split on it.
password="$(head -c 32 /dev/urandom | base64 | tr -d '/+=;\n' | cut -c1-32)"

docker compose -f docker-compose.yml exec -T postgres \
  psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v role="$role" -v pw="$password" <<'EOSQL'
CREATE ROLE :"role" LOGIN PASSWORD :'pw' IN ROLE nunam_indexer;
EOSQL

echo "Created login $role. Put this in the machine's appsettings.Local.json (ConnectionStrings:Postgres):"
echo "Host=$host;Port=${POSTGRES_PORT:-5432};Database=$POSTGRES_DB;Username=$role;Password=$password"
