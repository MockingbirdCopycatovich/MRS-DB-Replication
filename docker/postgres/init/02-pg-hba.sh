#!/bin/sh
# Runs once during initdb bootstrap (primary only) — the official Postgres image's default
# pg_hba.conf does not reliably admit the special "replication" pseudo-database, so we add an
# explicit rule. Safe for a local demo: the whole stack is only reachable on the compose network.
set -e

echo "host replication all 0.0.0.0/0 scram-sha-256" >> "$PGDATA/pg_hba.conf"
echo "host all all 0.0.0.0/0 scram-sha-256" >> "$PGDATA/pg_hba.conf"
