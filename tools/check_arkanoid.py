#!/usr/bin/env python3
"""Exercise Hub Arkanoid using real NGO peers and compare replicated screen snapshots.

Build with Igruha.EditorTools.AutotestBuild.BuildMac first. Delayed UDP is shared
with the existing Sumo runner; no artificial hit acceptance is added to gameplay.
"""
import argparse
import pathlib
import subprocess
import sys
import time

from check_sumo import DelayedLink


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--scenario', choices=['flow', 'disconnect', 'host-exit'], default='flow')
    parser.add_argument('--players', type=int, choices=[2, 3], default=2)
    parser.add_argument('--latency-ms', type=int, default=0)
    parser.add_argument('--jitter-ms', type=int, default=0)
    parser.add_argument('--late-observer', action='store_true')
    args = parser.parse_args()
    if args.players == 3 and args.scenario != 'flow':
        parser.error('The observer scenario uses --scenario flow')
    root = pathlib.Path(__file__).resolve().parents[1]
    app = root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    if not app.is_file():
        parser.error('Build the development player first')
    logs = root / 'igruha/Builds/Autotest/logs' / f'arkanoid-{args.scenario}-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    (logs / 'network.txt').write_text(f'one-way latency={args.latency_ms} ms; jitter={args.jitter_ms} ms\n')
    processes, links = [], []
    try:
        for i in range(args.players):
            if i == 2 and args.late_observer:
                time.sleep(17)
            log = logs / ('host.log' if i == 0 else f'client-{i}.log')
            command = [str(app), '--arkanoid-check', args.scenario, '--bot', '-logFile', str(log),
                       '-batchmode', '-nographics', '-native-leak-detection', 'EnabledWithStackTrace']
            if i:
                command += ['--client', '--host', '127.0.0.1']
                if args.latency_ms or args.jitter_ms:
                    link = DelayedLink(args.latency_ms, args.jitter_ms, i)
                    links.append(link)
                    command += ['--port', str(link.port)]
            processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            time.sleep(4 if i == 0 else 1)
        deadline = time.monotonic() + 280
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        ok = all(p.poll() == 0 for p in processes)
        records = []
        for log in sorted(logs.glob('*.log')):
            content = log.read_text(errors='replace')
            lines = [line for line in content.splitlines() if line.startswith('ARKANOID_')]
            passed = 'ARKANOID_CHECK COMPLETE passed=True' in content and 'ARKANOID_CHECK FAIL' not in content
            passed &= not any(marker in content for marker in ('Exception:', 'ErrorException', 'NetworkConfig mismatch'))
            print(log.name, 'PASS' if passed else 'FAIL', '\n' + '\n'.join(lines[-10:]), flush=True)
            ok &= passed
            snapshots = {}
            for line in lines:
                if line.startswith('ARKANOID_STATE '):
                    parts = line.split()
                    if parts[1] != '0':
                        snapshots[(parts[1], parts[2], parts[3])] = line
            records.append(snapshots)
        common = set.intersection(*(set(r) for r in records)) if records else set()
        equal = len(common) >= 10 and all(len({record[key] for record in records}) == 1 for key in common)
        print('Replicated screen snapshots:', len(common), 'identical=' + str(equal), flush=True)
        ok &= equal
        print('ARKANOID LOCALHOST CHECK:', 'PASS' if ok else 'FAIL', flush=True)
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
        for link in links:
            link.close()


if __name__ == '__main__':
    sys.exit(main())
