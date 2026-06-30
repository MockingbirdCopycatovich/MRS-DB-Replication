#!/bin/bash
# ─────────────────────────────────────────────
#  failover.sh
#  Manual failover: promotes standby-1 to primary
#
#  Usage:
#    bash scripts/failover.sh
# ─────────────────────────────────────────────

TARGET="mrs-standby-1"

echo ""
echo "⚠️  FAILOVER PROCEDURE"
echo "    Promoting ${TARGET} to primary..."
echo ""

# Step 1: Verify the target is actually a replica right now
IS_REPLICA=$(docker exec "$TARGET" psql -U mrs_user -d mrs_db -tAc "SELECT pg_is_in_recovery();")
if [ "$IS_REPLICA" != "t" ]; then
    echo "❌ ${TARGET} does not appear to be a replica. Aborting."
    exit 1
fi

# Step 2: Promote
docker exec -u postgres "$TARGET" pg_ctl promote -D /var/lib/postgresql/data

if [ $? -ne 0 ]; then
    echo "❌ Promotion failed."
    exit 1
fi

echo ""
echo "✅ ${TARGET} has been promoted."
echo ""
echo "Next steps:"
echo "  1. Update your MRS backend DB_HOST to: ${TARGET} (port 5433 from host)"
echo "  2. The old primary (mrs-primary) should NOT be started again until"
echo "     you reconfigure it as a new standby."
echo "  3. Run: bash scripts/check_replication.sh to verify the new state."
echo ""