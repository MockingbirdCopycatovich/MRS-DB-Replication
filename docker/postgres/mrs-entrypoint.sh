#!/bin/sh
# Wraps the official postgres entrypoint:
#  - MRS_ROLE=primary: starts a normal writable instance with replication enabled
#    (wal_level, max_wal_senders, hot_standby) and runs /docker-entrypoint-initdb.d on first boot.
#  - MRS_ROLE=replica: on an empty PGDATA, bootstraps via pg_basebackup -R against
#    PRIMARY_HOST:PRIMARY_PORT (writing standby.signal + primary_conninfo), then hands off to
#    the same official entrypoint, which just starts postgres in standby mode.
set -e

ROLE="${MRS_ROLE:-primary}"
PGDATA="${PGDATA:-/var/lib/postgresql/data}"

if [ "$ROLE" = "replica" ]; then
    if [ -z "$(ls -A "$PGDATA" 2>/dev/null)" ]; then
        echo "[mrs-entrypoint] node=$MRS_NODE_ID bootstrapping replica from $PRIMARY_HOST:$PRIMARY_PORT"

        until pg_basebackup \
            -D "$PGDATA" -Fp -Xs -P -R \
            -d "host=$PRIMARY_HOST port=$PRIMARY_PORT user=$POSTGRES_USER password=$POSTGRES_PASSWORD application_name=$MRS_NODE_ID"
        do
            echo "[mrs-entrypoint] node=$MRS_NODE_ID primary not ready yet, retrying pg_basebackup in 2s..."
            rm -rf "${PGDATA:?}"/*
            sleep 2
        done

        chmod 700 "$PGDATA"
        echo "[mrs-entrypoint] node=$MRS_NODE_ID basebackup complete"
    fi

    exec docker-entrypoint.sh postgres
else
    exec docker-entrypoint.sh postgres \
        -c wal_level=replica \
        -c max_wal_senders=10 \
        -c max_replication_slots=10 \
        -c hot_standby=on
fi
