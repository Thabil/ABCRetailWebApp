#!/bin/bash
# Sync clock with NTP on startup — retry until drift is under 5 seconds.
# Prevents Azure Storage 403 AuthenticationFailed from container clock drift.
sync_clock() {
    ntpdate -u pool.ntp.org 2>/dev/null || ntpdate -u time.windows.com 2>/dev/null || true
}

echo "[entrypoint] Syncing clock before startup..."
for i in 1 2 3 4 5; do
    sync_clock
    OFFSET=$(ntpdate -q pool.ntp.org 2>/dev/null | awk '{print $4}' | tr -d '+')
    OFFSET_ABS=$(awk "BEGIN{x=${OFFSET:-999}; print (x<0)?-x:x}")
    echo "[entrypoint] NTP offset after sync attempt $i: ${OFFSET}s"
    if awk "BEGIN{exit !(${OFFSET_ABS} < 5)}"; then
        echo "[entrypoint] Clock synced. Starting functions host."
        break
    fi
    echo "[entrypoint] Offset too large (${OFFSET_ABS}s), retrying in 3s..."
    sleep 3
done

# Background loop: re-sync every 2 minutes
( while true; do sleep 120; sync_clock; done ) &

# Retry func start on transient failures.
until func start --port 80 --no-build; do
    echo "[entrypoint] func start exited with code $?. Syncing clock and retrying in 5s..."
    sync_clock
    sleep 5
done
