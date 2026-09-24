#!/usr/bin/env python3
"""Run the opt-in development integration probe against an existing Mac build.
Usage: python3 tools/one_bullet_check.py flow|disconnect|timeout
"""
import argparse
import subprocess
import time
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('scenario', choices=['flow', 'disconnect', 'timeout'])
parser.add_argument('--port', type=int, default=17777)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1] / 'igruha/Builds/OneBulletChecks'
exe = root / 'sleepover.app/Contents/MacOS/sleepover'
if not exe.is_file():
    raise SystemExit('Build the development player into Builds/OneBulletChecks first.')
folder = root / (args.scenario + '-' + time.strftime('%Y%m%d-%H%M%S'))
folder.mkdir(parents=True)
players = 3 if args.scenario == 'disconnect' else 2
processes = []
try:
    for index in range(players):
        role = ['--autostart', 'OneBullet', '--wait-players', str(players)] if index == 0 else ['--client', '--host', '127.0.0.1']
        log = folder / ('host.log' if index == 0 else f'client{index}.log')
        command = [str(exe), *role, '--port', str(args.port), '--onebullet-check', args.scenario,
                   '--bot', '-batchmode', '-nographics', '-logFile', str(log)]
        processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
        if index == 0:
            time.sleep(2)
    deadline = time.monotonic() + 160
    while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
        time.sleep(.5)
    failed = False
    disconnected = 0
    rankings = []
    for index, proc in enumerate(processes):
        log = folder / ('host.log' if index == 0 else f'client{index}.log')
        data = log.read_text(errors='replace') if log.exists() else ''
        disconnected += 'ONE_BULLET DISCONNECT holding weapon' in data
        rankings.extend(line for line in data.splitlines() if '📊 итоги раунда OneBullet' in line)
        expected = 'ONE_BULLET DISCONNECT holding weapon' if args.scenario == 'disconnect' and 'ONE_BULLET DISCONNECT holding weapon' in data else 'ONE_BULLET RETURN lock=False'
        valid = proc.poll() == 0 and expected in data and 'ONE_BULLET FAIL' not in data and 'Exception:' not in data
        failed |= not valid
        print(log.name, 'PASS' if valid else 'FAIL', flush=True)
        for line in data.splitlines():
            if any(marker in line for marker in ['ONE_BULLET FINAL', 'ONE_BULLET RETURN', 'ONE_BULLET FAIL', 'ONE_BULLET DISCONNECT', 'итоги раунда']):
                print(line, flush=True)
    if len(set(rankings)) != 1 or len(rankings) != players - disconnected:
        print("FAIL: result tables do not match")
        failed = True
    if disconnected != (1 if args.scenario == "disconnect" else 0):
        print("FAIL: unexpected disconnect count")
        failed = True
    print(folder, flush=True)
    raise SystemExit(1 if failed else 0)
finally:
    for proc in processes:
        if proc.poll() is None:
            proc.terminate()
    for proc in processes:
        if proc.poll() is None:
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
