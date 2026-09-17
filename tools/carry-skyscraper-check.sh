#!/usr/bin/env bash
# Full local match: two rendered peers, remaining peers headless. No rule overrides.
set -eo pipefail
CARRY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CARRY_COUNT="${1:-4}"
CARRY_RENDERED="${2:-2}"
CARRY_WIDTH="${3:-1280}"
CARRY_HEIGHT=$((CARRY_WIDTH*9/16))
CARRY_APP="$CARRY_ROOT/igruha/Builds/CarrySkyscraperFinal/sleepover.app/Contents/MacOS/sleepover"
CARRY_LOGS="$CARRY_ROOT/docs/art/carry-item/network-$CARRY_COUNT-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$CARRY_LOGS"
pids=()
cleanup(){ for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done; wait 2>/dev/null || true; }
trap cleanup EXIT INT TERM
"$CARRY_APP" --autostart CarryItem --wait-players "$CARRY_COUNT" --bot --carry-art-check "$CARRY_LOGS" -screen-fullscreen 0 -screen-width "$CARRY_WIDTH" -screen-height "$CARRY_HEIGHT" -logFile "$CARRY_LOGS/host.log" >/dev/null 2>&1 &
pids+=("$!")
sleep 6
for ((i=1;i<CARRY_COUNT;i++));do
    extra=(); if ((i>=CARRY_RENDERED));then extra=(-batchmode -nographics);fi
    "$CARRY_APP" --client --host 127.0.0.1 --bot --carry-art-check "$CARRY_LOGS" -screen-fullscreen 0 -screen-width "$CARRY_WIDTH" -screen-height "$CARRY_HEIGHT" -logFile "$CARRY_LOGS/client-$i.log" "${extra[@]}" >/dev/null 2>&1 &
    pids+=("$!");sleep 2
done
echo "Evidence: $CARRY_LOGS; participants: $CARRY_COUNT"
for ((tick=0;tick<270;tick++));do
    for pid in "${pids[@]}";do if ! kill -0 "$pid" 2>/dev/null;then echo "Peer exited early" >&2;exit 1;fi;done
    CARRY_FINISHED=$(python3 - "$CARRY_LOGS" <<'PY'
from pathlib import Path
import sys
print(sum('END scene unloaded' in p.read_text() and 'RESULTS' in p.read_text() for p in Path(sys.argv[1]).glob('*-probe.txt')))
PY
)
    if [[ "$CARRY_FINISHED" -eq "$CARRY_COUNT" ]];then
        python3 - "$CARRY_LOGS" "$CARRY_COUNT" <<'VERIFY'
from pathlib import Path
import sys,re,json
root=Path(sys.argv[1]);count=int(sys.argv[2]);scores=[];reports=[];problems=[]
for p in sorted(root.glob('*-probe.txt')):
    t=p.read_text();m=re.search(r'RESULTS A=(\d+) B=(\d+) deliveries=(\d+)/(\d+)',t)
    assert m,p
    score=tuple(map(int,m.groups()));scores.append(score)
    if f'ROSTER={count} duration=200' not in t: problems.append(f'{p.stem}: roster/duration mismatch')
    if min(score)<=0: problems.append(f'{p.stem}: team failed to deliver water: {score}')
    heights=[float(y) for y in re.findall(r'local=\([^,]+, ([^,]+),',t)]
    if min(heights)<=-40: problems.append(f'{p.stem}: missed respawn, minY={min(heights)}')
    reports.append({'peer':p.stem,'result':score,'minY':min(heights),'perf':re.search(r'PERF .+',t).group(0)})
if len(scores)!=count or len(set(scores))!=1: problems.append(f'replicated results mismatch: {scores}')
for p in root.glob('*.log'):
    bad=re.findall(r'^.*(?:Exception|Assertion failed|Error:).*$' ,p.read_text(),re.M)
    if bad: problems.append(f'{p.name}: runtime errors: {bad[:8]}')
(root/'summary.json').write_text(json.dumps({'accepted':not problems,'problems':problems,'peers':reports},indent=2)+'\n')
if problems:
    print('FAIL:\n'+'\n'.join(problems));sys.exit(1)
print('PASS: both teams delivered, all peers agree, roster/timer intact, no runtime exceptions.')
VERIFY
        echo "All peers reached results and returned to hub.";exit 0
    fi
    if ((tick%15==0));then echo "Waiting $((tick*2))s; finished=$CARRY_FINISHED";tail -n 2 "$CARRY_LOGS/host-probe.txt" 2>/dev/null || true;fi
    sleep 2
done
echo "Timed out" >&2;exit 1
