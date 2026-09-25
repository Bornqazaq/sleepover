#!/usr/bin/env bash
# IGR-592: five deliveries per team, using real prefabs and replicated state.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover"
PLAYERS="${1:-4}"
PORT="${CARRY_TEST_PORT:-17592}"
[[ "$PLAYERS" =~ ^[4-8]$ ]] || { echo "Expected 4-8 players" >&2; exit 1; }
[[ -x "$APP" ]] || { echo "Missing development build: $APP" >&2; exit 1; }
LOGS="$ROOT/igruha/Builds/Autotest/logs/carry-delivery-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$LOGS"
pids=()
cleanup() {
    for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
    for pid in "${pids[@]}"; do wait "$pid" 2>/dev/null || true; done
}
trap cleanup EXIT
trap 'exit 130' INT TERM

"$APP" --autostart CarryItem --wait-players "$PLAYERS" --port "$PORT" --bot --carry-delivery-check true \
    -batchmode -nographics -logFile "$LOGS/host.log" >/dev/null 2>&1 &
pids+=("$!")
sleep 6
for ((i = 1; i < PLAYERS; i++)); do
    "$APP" --client --host 127.0.0.1 --port "$PORT" --bot --carry-delivery-check true \
        -batchmode -nographics -logFile "$LOGS/client-$i.log" >/dev/null 2>&1 &
    pids+=("$!")
    sleep 1
done
echo "Logs: $LOGS"

deadline=$((SECONDS + 180))
while ((SECONDS < deadline)); do
    running=0
    for pid in "${pids[@]}"; do
        if kill -0 "$pid" 2>/dev/null; then running=$((running + 1)); fi
    done
    ((running == 0)) && break
    sleep 1
done

result=0
for log in "$LOGS"/*.log; do
    if ! grep -q 'CARRY_DELIVERY_CHECK PASS' "$log" ||
        grep -Eq 'CARRY_DELIVERY_CHECK FAIL|Exception:|NetworkConfig mismatch' "$log"; then
        echo "FAIL: $log"
        result=1
    fi
    grep 'CARRY_DELIVERY_CHECK' "$log" || true
done
exit "$result"
