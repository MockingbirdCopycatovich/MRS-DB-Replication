#!/bin/bash
# ─────────────────────────────────────────────
#  check_replication.sh
#  Run this from your host machine to see the
#  replication status of the cluster.
# ─────────────────────────────────────────────

echo ""
echo "══════════════════════════════════════════"
echo "  MRS Cluster — Replication Status"
echo "══════════════════════════════════════════"

echo ""
echo "── Connected standbys (on primary) ──────"
docker exec mrs-primary psql -U mrs_user -d mrs_db -x -c "
SELECT
    application_name,
    client_addr,
    state,
    sent_lsn,
    write_lsn,
    flush_lsn,
    replay_lsn,
    sync_state
FROM pg_stat_replication;
"

echo ""
echo "── Replication lag (bytes behind primary) ─"
docker exec mrs-primary psql -U mrs_user -d mrs_db -x -c "
SELECT
    application_name,
    pg_wal_lsn_diff(sent_lsn, replay_lsn) AS lag_bytes
FROM pg_stat_replication;
"

echo ""
echo "── Is standby-1 in recovery mode? ────────"
docker exec mrs-standby-1 psql -U mrs_user -d mrs_db -c "SELECT pg_is_in_recovery();"

echo ""
echo "── Is standby-2 in recovery mode? ────────"
docker exec mrs-standby-2 psql -U mrs_user -d mrs_db -c "SELECT pg_is_in_recovery();"

echo ""
echo "══════════════════════════════════════════"
echo "  ✅ pg_is_in_recovery = true  → replica"
echo "  ❌ pg_is_in_recovery = false → primary"
echo "══════════════════════════════════════════"
echo ""