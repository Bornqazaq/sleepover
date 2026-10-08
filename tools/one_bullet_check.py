#!/usr/bin/env python3
"""Run the opt-in development integration probe against an existing Mac build.
Usage: python3 tools/one_bullet_check.py flow|disconnect|timeout|upgrade
"""
import argparse
import subprocess
import time
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('scenario', choices=['flow', 'disconnect', 'timeout', 'upgrade'])
parser.add_argument('--port', type=int, default=17777)
parser.add_argument('--app', type=Path, help='Use an existing development .app instead of Builds/OneBulletChecks.')
parser.add_argument('--players', type=int, choices=[4, 8], default=4, help='Roster for the upgrade scenario.')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1] / 'igruha/Builds/OneBulletChecks'
exe = (args.app.resolve() if args.app else root / 'sleepover.app') / 'Contents/MacOS/sleepover'
if not exe.is_file():
    raise SystemExit('Build the development player into Builds/OneBulletChecks first.')
folder = root / (args.scenario + '-' + time.strftime('%Y%m%d-%H%M%S'))
folder.mkdir(parents=True)
players = args.players if args.scenario == 'upgrade' else 3 if args.scenario == 'disconnect' else 2
processes = []
try:
    for index in range(players):
        role = ['--autostart', 'OneBullet', '--wait-players', str(players)] if index == 0 else ['--client', '--host', '127.0.0.1']
        log = folder / ('host.log' if index == 0 else f'client{index}.log')
        probe = ['--onebullet-upgrade', '--upgrade-roster', str(players)] if args.scenario == 'upgrade' else ['--onebullet-check', args.scenario]
        command = [str(exe), *role, '--port', str(args.port), *probe,
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
        crouch_checked = 'ONE_BULLET CROUCH valid=True' in data or (args.scenario == 'disconnect' and index > 0 and 'ONE_BULLET DISCONNECT holding weapon' not in data)
        if args.scenario == 'upgrade':
            expected = 'OB_UPGRADE RETURN valid=True'
            crouch_checked = all(marker in data for marker in ['OB_UPGRADE CHECK valid=True', 'OB_UPGRADE ESCAPE valid=True', 'OB_UPGRADE FINAL valid=True'])
        valid = proc.poll() == 0 and expected in data and crouch_checked and 'ONE_BULLET FAIL' not in data and 'OB_UPGRADE FAIL' not in data and 'Exception:' not in data
        failed |= not valid
        print(log.name, 'PASS' if valid else 'FAIL', flush=True)
        for line in data.splitlines():
            if any(marker in line for marker in ['OB_UPGRADE CHECK', 'OB_UPGRADE ESCAPE', 'OB_UPGRADE FINAL', 'OB_UPGRADE RETURN', 'OB_UPGRADE FAIL', 'ONE_BULLET CROUCH', 'ONE_BULLET ADS', 'ONE_BULLET FINAL', 'ONE_BULLET RETURN', 'ONE_BULLET FAIL', 'ONE_BULLET DISCONNECT', 'итоги раунда']):
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
