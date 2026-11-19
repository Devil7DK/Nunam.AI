#!/bin/bash
# Runs once, when the data volume is first initialised. Creates the pgvector extension and one login
# per service: the indexers and the AI worker write, the API only reads. The owner role
# ($POSTGRES_USER) runs migrations, so default privileges cover the tables it creates later.
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v owner="$POSTGRES_USER" -v db="$POSTGRES_DB" \
  -v indexer_pw="$NUNAM_INDEXER_PASSWORD" \
  -v aiworker_pw="$NUNAM_AIWORKER_PASSWORD" \
  -v api_pw="$NUNAM_API_PASSWORD" <<'EOSQL'
CREATE EXTENSION IF NOT EXISTS vector;

CREATE ROLE nunam_indexer LOGIN PASSWORD :'indexer_pw';
CREATE ROLE nunam_aiworker LOGIN PASSWORD :'aiworker_pw';
CREATE ROLE nunam_api LOGIN PASSWORD :'api_pw';

GRANT CONNECT ON DATABASE :"db" TO nunam_indexer, nunam_aiworker, nunam_api;
GRANT USAGE ON SCHEMA public TO nunam_indexer, nunam_aiworker, nunam_api;

ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO nunam_indexer, nunam_aiworker;
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA public
  GRANT SELECT ON TABLES TO nunam_api;
EOSQL
