#!/bin/bash
# ─────────────────────────────────────────────
#  Standby entrypoint
#
#  On first boot:
#    1. Waits for the primary to be ready
#    2. Clones the primary with pg_basebackup
#    3. Writes the streaming connection config
#    4. Starts PostgreSQL in standby mode
#
#  On subsequent boots:
#    Data directory already exists → skip the clone, just start.
# ─────────────────────────────────────────────

set -e

DATA_DIR="/var/lib/postgresql/data"
PGPASSFILE="/tmp/.pgpass"

# Write a .pgpass so pg_basebackup doesn't prompt for password
echo "${PRIMARY_HOST}:${PRIMARY_PORT}:replication:${REPLICATION_USER}:${REPLICATION_PASSWORD}" > "$PGPASSFILE"
chmod 600 "$PGPASSFILE"
export PGPASSFILE

# ── First-boot: clone the primary ────────────
if [ ! -s "$DATA_DIR/PG_VERSION" ]; then
    echo "📦 No data directory found — cloning primary at ${PRIMARY_HOST}:${PRIMARY_PORT} ..."

    # Wait until primary accepts connections
    until pg_isready -h "$PRIMARY_HOST" -p "$PRIMARY_PORT" -U "$REPLICATION_USER"; do
        echo "⏳ Waiting for primary..."
        sleep 2
    done

    # pg_basebackup makes an exact binary copy of the primary data dir
    pg_basebackup \
        --host="$PRIMARY_HOST" \
        --port="$PRIMARY_PORT" \
        --username="$REPLICATION_USER" \
        --pgdata="$DATA_DIR" \
        --wal-method=stream \
        --checkpoint=fast \
        --progress \
        --verbose

    # standby.signal tells Postgres "I am a replica, not a primary"
    touch "$DATA_DIR/standby.signal"

    # Write primary_conninfo into postgresql.auto.conf
    # This tells the standby WHERE to stream WAL from
    cat >> "$DATA_DIR/postgresql.auto.conf" <<EOF

# Streaming replication connection to primary
primary_conninfo = 'host=${PRIMARY_HOST} port=${PRIMARY_PORT} user=${REPLICATION_USER} password=${REPLICATION_PASSWORD} application_name=$(hostname)'
EOF

    chown -R postgres:postgres "$DATA_DIR"
    chmod 700 "$DATA_DIR"
    echo "✅ Base backup complete. Starting in standby mode..."
else
    echo "📂 Data directory exists. Starting PostgreSQL normally..."
fi

# Start PostgreSQL as the postgres user (root is not permitted)
exec gosu postgres postgres -c config_file=/etc/postgresql/postgresql.conf