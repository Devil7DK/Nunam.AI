#!/bin/bash
# Runs once, when the data volume is first initialised. Creates the pgvector extension and the service
# roles. Table privileges and row-level security are set by the migrations, so tables start closed.
# nunam_indexer is a group without login: each indexed machine gets its own login in it
# (docker/add-indexer-login.sh), and row-level security limits that login to its own machine.
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v db="$POSTGRES_DB" \
  -v aiworker_pw="$NUNAM_AIWORKER_PASSWORD" \
  -v api_pw="$NUNAM_API_PASSWORD" <<'EOSQL'
CREATE EXTENSION IF NOT EXISTS vector;

CREATE ROLE nunam_indexer NOLOGIN;
CREATE ROLE nunam_aiworker LOGIN PASSWORD :'aiworker_pw';
CREATE ROLE nunam_api LOGIN PASSWORD :'api_pw';

GRANT CONNECT ON DATABASE :"db" TO nunam_indexer, nunam_aiworker, nunam_api;
GRANT USAGE ON SCHEMA public TO nunam_indexer, nunam_aiworker, nunam_api;
EOSQL
