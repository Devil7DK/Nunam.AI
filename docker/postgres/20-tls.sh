#!/bin/bash
# Runs once, when the data volume is first initialised (sourced by the image's entrypoint, so no cd).
# TCP logins must use TLS, with a self-signed certificate; the services connect with SSL Mode=Require.
# Socket logins inside the container are unchanged.
set -euo pipefail

openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -nodes -days 3650 \
  -subj "/CN=nunam-postgres" -keyout "$PGDATA/server.key" -out "$PGDATA/server.crt" 2>/dev/null
chmod 600 "$PGDATA/server.key"
echo "ssl = on" >> "$PGDATA/postgresql.conf"

# Drop every TCP rule initdb and the image wrote; without a matching line a plain TCP login is refused.
sed -i -E '/^host(ssl|nossl)?[[:space:]]/d' "$PGDATA/pg_hba.conf"
echo "hostssl all all all scram-sha-256" >> "$PGDATA/pg_hba.conf"
