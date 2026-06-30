#!/bin/bash
# Runs automatically by postgres entrypoint on first boot (because it's in initdb.d)
# Creates a dedicated user that standbys will use to stream WAL

set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    -- Create the replication user
    CREATE USER ${REPLICATION_USER} WITH REPLICATION ENCRYPTED PASSWORD '${REPLICATION_PASSWORD}';

    -- Grant it login rights
    GRANT pg_monitor TO ${REPLICATION_USER};
EOSQL

echo "Replication user '${REPLICATION_USER}' created."