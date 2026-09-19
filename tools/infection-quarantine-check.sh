#!/usr/bin/env bash
# Four real processes; the opt-in probe drives ordinary local movement through props.
set -eo pipefail
INFECTION_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INFECTION_APP="$INFECTION_ROOT/igruha/Builds/InfectionQuarantine/sleepover.app/Contents/MacOS/sleepover"
INFECTION_LOGS="$INFECTION_ROOT/docs/art/infection/network-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$INFECTION_LOGS"
pids=()
cleanup(){ for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done; wait 2>/dev/null || true; }
trap cleanup EXIT INT TERM
"$INFECTION_APP" --autostart Infection --wait-players 4 --bot --infection-art-check "$INFECTION_LOGS" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "$INFECTION_LOGS/host.log" >/dev/null 2>&1 &
pids+=("$!")
sleep 5
for ((i=1;i<4;i++));do
 extra=();if ((i>1));then extra=(-batchmode -nographics);fi
 "$INFECTION_APP" --client --host 127.0.0.1 --bot --infection-art-check "$INFECTION_LOGS" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "$INFECTION_LOGS/client-$i.log" "${extra[@]}" >/dev/null 2>&1 &
 pids+=("$!");sleep 2
done
echo "Evidence: $INFECTION_LOGS"
for ((tick=0;tick<150;tick++));do
 done_count=$(python3 - "$INFECTION_LOGS" <<'PY'
from pathlib import Path
import sys
print(sum('END scene unloaded' in p.read_text() and 'RESULTS=' in p.read_text() for p in Path(sys.argv[1]).glob('*-probe.txt')))
PY
)
 if [[ "$done_count" == 4 ]];then
 python3 - "$INFECTION_LOGS" <<'PY'
from pathlib import Path
import sys,re,json
root=Path(sys.argv[1]);results=[];reports=[];errors=[]
for p in sorted(root.glob('*-probe.txt')):
 t=p.read_text();result=re.search(r'^RESULTS=(.*)',t,re.M).group(1);results.append(result)
 perf=re.search(r'^PERF.*',t,re.M).group(0);reports.append({'peer':p.stem,'results':result,'performance':perf})
 if 'ROSTER=4' not in t:errors.append(p.name+': roster not four')
 if 'splashFrames=0' in perf:errors.append(p.name+': splash never observed')
 if 'tubeFrames=0' in perf:errors.append(p.name+': no tube entry')
if len(set(results))!=1:errors.append('Results differ between peers')
for p in root.glob('*.log'):
 bad=re.findall(r'^.*(?:Exception|Assertion failed|Error:).*$',p.read_text(errors='replace'),re.M)
 if bad:errors.append(p.name+': '+str(bad[:6]))
(root/'summary.json').write_text(json.dumps({'passed':not errors,'errors':errors,'peers':reports},indent=2))
print(json.dumps({'passed':not errors,'errors':errors,'peers':reports},indent=2))
if errors:sys.exit(1)
PY
 exit 0
 fi
 if ((tick%15==0));then echo "Waiting $((tick*2))s; finished=$done_count";tail -n 2 "$INFECTION_LOGS/host-probe.txt" 2>/dev/null || true;fi
 sleep 2
done
echo 'Timed out' >&2;exit 1
