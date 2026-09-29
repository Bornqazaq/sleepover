#!/usr/bin/env python3
"""Check Stopwatch spectator retargeting on the host and a client with five real NGO players.
Deaths are staged on the server; ordinary cage/impact messages and real disconnects cross NGO.
Build first via Unity MCP: Igruha.EditorTools.AutotestBuild.BuildMac.
This uses real NGO disconnects, not direct calls to HandlePlayerLeft.
"""
import argparse
import pathlib
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--scenario', choices=['eliminate', 'disconnect'], default='eliminate')
    args = parser.parse_args()
    players = 5
    root = pathlib.Path(__file__).resolve().parents[1]
    app = root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    if not app.is_file():
        parser.error('Build the development player first')
    logs = root / 'igruha/Builds/Autotest/logs' / f'stopwatch-spectator-{args.scenario}-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    processes = []
    try:
        for i in range(players):
            log = logs / ('host.log' if i == 0 else f'client-{i}.log')
            command = [str(app), '--stopwatch-spectator-check', args.scenario, '--spectator-screenshots', str(logs), '--bot', '-logFile', str(log)]
            if i == 0:
                command += ['--autostart', 'Stopwatch', '--wait-players', str(players),
                            '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720']
            else:
                command += ['--client', '--host', '127.0.0.1', '-batchmode', '-nographics']
            processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            time.sleep(5 if i == 0 else 1)
        deadline = time.monotonic() + 145
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        ok = all(p.poll() == 0 for p in processes)
        snapshots, disconnects = [], 0
        for log in sorted(logs.glob('*.log')):
            contents = log.read_text(errors='replace')
            lines = [line for line in contents.splitlines() if line.startswith('STOPWATCH_CHECK')]
            disconnected = any(' DISCONNECT ' in line for line in lines)
            disconnects += disconnected
            passed = 'STOPWATCH_CHECK FAIL' not in contents and 'Exception:' not in contents
            if not disconnected:
                passed &= any(' RETURN passed=True' in line for line in lines)
                if log.name in ('host.log', 'client-1.log'):
                    passed &= any(' RETARGET ' in line and 'passed=True' in line for line in lines)
                snapshots += [line for line in lines if ' STATE ' in line]
            ok &= passed
            print(log.name, 'PASS' if passed else 'FAIL', '\n' + '\n'.join(lines[-8:]), flush=True)
        expected_disconnects = 0 if args.scenario == 'eliminate' else 1
        ok &= disconnects == expected_disconnects
        ok &= len(snapshots) == players - expected_disconnects and len(set(snapshots)) == 1
        print('STOPWATCH SPECTATOR CHECK:', 'PASS' if ok else 'FAIL', flush=True)
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
