#!/bin/bash
# Runs once, when the data volume is first initialised. Creates the pgvector extension and the API's
# login. Table privileges are set by the migrations, so tables start closed. Indexers and the AI
# worker have no logins: they go through the API.
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v db="$POSTGRES_DB" \
  -v api_pw="$NUNAM_API_PASSWORD" <<'EOSQL'
CREATE EXTENSION IF NOT EXISTS vector;

CREATE ROLE nunam_api LOGIN PASSWORD :'api_pw';

GRANT CONNECT ON DATABASE :"db" TO nunam_api;
GRANT USAGE ON SCHEMA public TO nunam_api;
EOSQL
