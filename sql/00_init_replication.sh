#!/bin/bash
# Creates the replication user on primary first boot.
# Runs via /docker-entrypoint-initdb.d before the SQL schema files.

set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE USER ${REPLICATION_USER} WITH REPLICATION ENCRYPTED PASSWORD '${REPLICATION_PASSWORD}';
    GRANT pg_monitor TO ${REPLICATION_USER};
EOSQL

echo "Replication user '${REPLICATION_USER}' created."
