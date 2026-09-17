#!/usr/bin/env bash
# Two rendered local processes; public-event and effect evidence on both peers.
set -euo pipefail
FOUNDRY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FOUNDRY_APP="$FOUNDRY_ROOT/igruha/Builds/MemoryFoundry/sleepover.app/Contents/MacOS/sleepover"
FOUNDRY_LOGS="$FOUNDRY_ROOT/docs/art/memory-run/network-check/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$FOUNDRY_LOGS"
if [[ ! -x "$FOUNDRY_APP" ]]; then
    echo "Build the development macOS player into Builds/MemoryFoundry/sleepover.app first." >&2
    exit 1
fi
echo "Evidence: $FOUNDRY_LOGS"
"$FOUNDRY_APP" --autostart MemoryRun --wait-players 2 --bot \
    --memory-foundry-check "$FOUNDRY_LOGS" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 \
    -logFile "$FOUNDRY_LOGS/host.log" >/dev/null 2>&1 &
FOUNDRY_HOST_PID=$!
FOUNDRY_CLIENT_PID=""
cleanup() {
    kill "$FOUNDRY_HOST_PID" 2>/dev/null || true
    if [[ -n "$FOUNDRY_CLIENT_PID" ]]; then kill "$FOUNDRY_CLIENT_PID" 2>/dev/null || true; fi
    wait "$FOUNDRY_HOST_PID" 2>/dev/null || true
    if [[ -n "$FOUNDRY_CLIENT_PID" ]]; then wait "$FOUNDRY_CLIENT_PID" 2>/dev/null || true; fi
}
trap cleanup EXIT INT TERM
sleep 6
"$FOUNDRY_APP" --client --host 127.0.0.1 --bot \
    --memory-foundry-check "$FOUNDRY_LOGS" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 \
    -logFile "$FOUNDRY_LOGS/client.log" >/dev/null 2>&1 &
FOUNDRY_CLIENT_PID=$!
echo "Foundry test host=$FOUNDRY_HOST_PID client=$FOUNDRY_CLIENT_PID"
for ((tick=0; tick<360; tick++)); do
    if ! kill -0 "$FOUNDRY_HOST_PID" 2>/dev/null || ! kill -0 "$FOUNDRY_CLIENT_PID" 2>/dev/null; then
        echo "A player exited early; inspect $FOUNDRY_LOGS" >&2; exit 1
    fi
    if [[ -f "$FOUNDRY_LOGS/host-probe.txt" && -f "$FOUNDRY_LOGS/client-probe.txt" ]] \
        && rg -q 'END scene unloaded' "$FOUNDRY_LOGS/host-probe.txt" \
        && rg -q 'END scene unloaded' "$FOUNDRY_LOGS/client-probe.txt"; then
        echo "Both peers completed MemoryRun and unloaded the scene."
        exit 0
    fi
    if ((tick % 15 == 0)); then
        echo "Waiting: $((tick*2)) seconds"
        tail -2 "$FOUNDRY_LOGS/host-probe.txt" 2>/dev/null || true
    fi
    sleep 2
done
echo "Timed out; inspect $FOUNDRY_LOGS" >&2
exit 1
