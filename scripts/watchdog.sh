#!/bin/bash
# ─────────────────────────────────────────────
#  watchdog.sh
#  Monitors the primary and auto-promotes the
#  most up-to-date standby when it goes down.
#
#  Usage:
#    bash scripts/watchdog.sh
# ─────────────────────────────────────────────

PRIMARY="mrs-primary"
STANDBYS=("mrs-standby-1" "mrs-standby-2")
PG_USER="mrs_user"
PG_DB="mrs_db"

CHECK_INTERVAL=5      # seconds between health checks
FAILURE_THRESHOLD=3   # consecutive failures before triggering failover

# ── Helpers ───────────────────────────────────

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" >&2
}

check_primary() {
    docker exec "$PRIMARY" pg_isready -U "$PG_USER" -d "$PG_DB" -q 2>/dev/null
}

# Returns the standby container name with the highest replay LSN.
# LSN format is HI/LO (hex), converted to a single integer for comparison.
# Uses bash arithmetic (16#hex) instead of awk strtonum — works on macOS BSD awk.
lsn_to_int() {
    local hi lo
    IFS='/' read -r hi lo <<< "$1"
    echo $(( 16#${hi} * 4294967296 + 16#${lo} ))
}

pick_best_standby() {
    local best_name=""
    local best_lsn_int=0

    for standby in "${STANDBYS[@]}"; do
        # Skip if container is not reachable
        if ! docker exec "$standby" pg_isready -U "$PG_USER" -d "$PG_DB" -q 2>/dev/null; then
            log "  ↳ $standby — unreachable, skipping"
            continue
        fi

        # Skip if it is somehow already a primary
        is_replica=$(docker exec "$standby" psql -U "$PG_USER" -d "$PG_DB" \
            -tAc "SELECT pg_is_in_recovery();" 2>/dev/null)
        if [ "$is_replica" != "t" ]; then
            log "  ↳ $standby — not in recovery mode, skipping"
            continue
        fi

        lsn=$(docker exec "$standby" psql -U "$PG_USER" -d "$PG_DB" \
            -tAc "SELECT pg_last_wal_replay_lsn();" 2>/dev/null | tr -d '[:space:]')

        lsn_int=$(lsn_to_int "$lsn")
        log "  ↳ $standby — replay LSN: $lsn ($lsn_int)"

        if [ "$lsn_int" -gt "$best_lsn_int" ]; then
            best_lsn_int=$lsn_int
            best_name=$standby
        fi
    done

    echo "$best_name"
}

# ── Failover ──────────────────────────────────

do_failover() {
    echo ""
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    log "  PRIMARY IS DOWN — INITIATING FAILOVER"
    log "  OLD PRIMARY : $PRIMARY"
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""

    log "Scanning standbys for the most up-to-date replica..."
    local target
    target=$(pick_best_standby)

    if [ -z "$target" ]; then
        log "ERROR: No available standby found. Cannot failover. Will retry..."
        return 1
    fi

    echo ""
    log "Selected standby for promotion: $target"
    log "Promoting $target → PRIMARY ..."
    echo ""

    docker exec -u postgres "$target" pg_ctl promote -D /var/lib/postgresql/data

    if [ $? -ne 0 ]; then
        log "ERROR: pg_ctl promote failed on $target. Will retry..."
        return 1
    fi

    local old_primary="$PRIMARY"

    # Update state: new primary is the promoted standby,
    # old primary becomes a (dead) standby slot
    PRIMARY="$target"
    local new_standbys=()
    for s in "${STANDBYS[@]}"; do
        [ "$s" != "$target" ] && new_standbys+=("$s")
    done
    new_standbys+=("$old_primary")
    STANDBYS=("${new_standbys[@]}")

    echo ""
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    log "  FAILOVER COMPLETE"
    log "  NEW PRIMARY : $PRIMARY"
    log "  STANDBYS    : ${STANDBYS[*]}"
    log "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    log "Action required:"
    log "  1. Point MRS backend DB_HOST to: $PRIMARY"
    log "  2. Do NOT restart $old_primary until reconfigured as a standby."
    log "  3. Run: bash scripts/check_replication.sh to verify."
    echo ""
    log "Watchdog continuing — now monitoring: $PRIMARY"
    echo ""
}

# ── Main loop ─────────────────────────────────

failure_count=0

log "Watchdog started."
log "Monitoring : $PRIMARY"
log "Standbys   : ${STANDBYS[*]}"
log "Interval   : ${CHECK_INTERVAL}s | Threshold: ${FAILURE_THRESHOLD} failures"
echo ""

while true; do
    if check_primary; then
        if [ "$failure_count" -gt 0 ]; then
            log "Primary recovered after $failure_count failed check(s). Resetting counter."
        fi
        failure_count=0
    else
        failure_count=$((failure_count + 1))
        log "Primary health check FAILED ($failure_count/$FAILURE_THRESHOLD)"

        if [ "$failure_count" -ge "$FAILURE_THRESHOLD" ]; then
            do_failover
            failure_count=0
        fi
    fi

    sleep "$CHECK_INTERVAL"
done
