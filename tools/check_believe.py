#!/usr/bin/env python3
"""Check BelieveOrNot UI decisions and cancellation in real localhost development players.
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
    parser.add_argument('--scenario', choices=['normal', 'cancel-seating', 'cancel-peek', 'cancel-persuasion', 'cancel-oath', 'cancel-final', 'predictions', 'oaths', 'oath-timeout'], default='normal')
    parser.add_argument('--players', type=int, choices=range(2, 9))
    parser.add_argument('--port', type=int, default=7777, help='Separate transport port for concurrent checks')
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error('--port must be between 1 and 65535')
    players = args.players or (2 if args.scenario == 'normal' else 4)
    root = pathlib.Path(__file__).resolve().parents[1]
    app = root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    if not app.is_file():
        parser.error('Build the development player first')
    logs = root / 'igruha/Builds/Autotest/logs' / f'believe-{args.scenario}-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    processes = []
    try:
        for i in range(players):
            log = logs / ('host.log' if i == 0 else f'client-{i}.log')
            command = [str(app), '--believe-check', args.scenario, '--bot', '--port', str(args.port), '-logFile', str(log)]
            if i == 0:
                command += ['--autostart', 'BelieveOrNot', '--wait-players', str(players),
                            '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720']
            else:
                command += ['--client', '--host', '127.0.0.1', '-batchmode', '-nographics']
            processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            time.sleep(5 if i == 0 else 1)
        deadline = time.monotonic() + 930
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        ok = all(p.poll() == 0 for p in processes)
        snapshots, disconnects, predictions, pairs, oaths, verdicts = [], 0, {}, {}, {}, {}
        for log in sorted(logs.glob('*.log')):
            contents = log.read_text(errors='replace')
            lines = [line for line in contents.splitlines() if line.startswith('BELIEVE_CHECK')]
            disconnected = any(' DISCONNECT ' in line for line in lines)
            disconnects += disconnected
            passed = 'BELIEVE_CHECK FAIL' not in contents and 'Exception:' not in contents
            if not disconnected:
                passed &= any(' RETURN passed=True' in line for line in lines)
                passed &= any(' CHAMPION ' in line for line in lines)
                if args.scenario.startswith('cancel-'):
                    passed &= any(' CANCEL_PASS ' in line for line in lines)
                elif args.scenario != 'predictions':
                    passed &= any(' PAUSE_PASS ' in line for line in lines)
                snapshots += [line for line in lines if ' FINAL ' in line]
                for line in lines:
                    if ' OATH ' in line or ' OATH_RESULT ' in line:
                        target = verdicts if ' OATH_RESULT ' in line else oaths
                        round_id = line.split('round=')[1].split()[0]
                        target.setdefault(round_id, []).append(line)
                    if ' PAIR ' in line:
                        round_id = line.split('round=')[1].split()[0]
                        pairs.setdefault(round_id, []).append(line)
                    if ' PREDICTIONS ' in line:
                        round_id = line.split('round=')[1].split()[0]
                        predictions.setdefault(round_id, []).append(line)
                if args.scenario == 'predictions':
                    passed &= any(' PREDICTIONS ' in line for line in lines)
            ok &= passed
            print(log.name, 'PASS' if passed else 'FAIL', '\n' + '\n'.join(lines[-8:]), flush=True)
        expected_disconnects = 1 if args.scenario.startswith('cancel-') else 0
        ok &= disconnects == expected_disconnects
        ok &= len(snapshots) == players - expected_disconnects and len(set(snapshots)) == 1
        if not expected_disconnects:
            qualification_rounds = 2 * players
            expected_rounds = len(pairs)
            ok &= qualification_rounds + 2 <= expected_rounds <= qualification_rounds + players - 2 + 3
            ok &= len(pairs) == expected_rounds and all(len(v) == players and len(set(v)) == 1 for v in pairs.values())
        if args.scenario in ['predictions', 'oaths', 'oath-timeout']:
            ok &= bool(predictions) and all(len(v) == players and len(set(v)) == 1 for v in predictions.values())
        if not expected_disconnects:
            ok &= len(oaths) == expected_rounds and all(len(v) == players and len(set(v)) == 1 for v in oaths.values())
            ok &= len(verdicts) == expected_rounds and all(len(v) == players and len(set(v)) == 1 for v in verdicts.values())
        print('BELIEVE LOCALHOST CHECK:', 'PASS' if ok else 'FAIL', flush=True)
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
