#!/usr/bin/env python3
"""Run a real localhost SumoRing development build: punches, jump, collapse, results, hub.
Usage: python3 tools/check_sumo.py --players 8 [--scenario disconnect]
Build first with Igruha.EditorTools.AutotestBuild.BuildMac (Unity MCP).
This is an automated transport/physics check, not a human balance playtest.
"""
import argparse
import pathlib
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--players', type=int, choices=range(3, 9), default=3)
    parser.add_argument('--scenario', choices=['basic', 'disconnect'], default='basic')
    parser.add_argument('--app', type=pathlib.Path)
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parents[1]
    app = args.app or root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    if not app.is_file():
        parser.error(f'Development player not found: {app}')
    logs = root / 'igruha/Builds/Autotest/logs' / f'sumo-{args.scenario}-{args.players}-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    processes = []
    try:
        for i in range(args.players):
            log = logs / ('host.log' if i == 0 else f'client-{i}.log')
            command = [str(app), '--sumo-check', args.scenario, '--bot', '-logFile', str(log)]
            if i == 0:
                command += ['--autostart', 'SumoRing', '--wait-players', str(args.players), '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720']
            else:
                command += ['--client', '--host', '127.0.0.1', '-batchmode', '-nographics']
            processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            time.sleep(5 if i == 0 else 1)
        (logs / 'pids.txt').write_text('\n'.join(str(p.pid) for p in processes))
        deadline = time.monotonic() + 220
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        ok = all(p.poll() == 0 for p in processes)
        results = []
        for log in sorted(logs.glob('*.log')):
            text = log.read_text(errors='replace')
            intentional = args.scenario == 'disconnect' and log.name == f'client-{args.players - 1}.log'
            passed = 'SUMO_CHECK DISCONNECT' in text if intentional else 'SUMO_CHECK RETURN restored=True' in text
            passed &= 'SUMO_CHECK FAIL' not in text and 'Exception:' not in text
            ok &= passed
            lines = [line for line in text.splitlines() if line.startswith('SUMO_CHECK') or 'итоги раунда SumoRing' in line]
            print(log.name, 'PASS' if passed else 'FAIL', '\n' + '\n'.join(lines[-6:]), flush=True)
            results += [line for line in lines if 'итоги раунда SumoRing' in line]
        # Every remaining peer must agree on the complete ordered results record.
        ok &= len(set(results)) == 1 and len(results) == args.players - (args.scenario == 'disconnect')
        print('SUMO LOCALHOST CHECK:', 'PASS' if ok else 'FAIL', flush=True)
        return 0 if ok else 1
    finally:
        for process in processes:
            if process.poll() is None:
                process.terminate()
        for process in processes:
            try:
                process.wait(timeout=8)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


if __name__ == '__main__':
    sys.exit(main())
