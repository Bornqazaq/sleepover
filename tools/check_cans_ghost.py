#!/usr/bin/env python3
"""Real NGO checks: one or many solver ghosts, ordered swaps, confirmation races and cleanup."""
import argparse
import pathlib
import subprocess
import time
from check_sumo import DelayedLink


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--scenario', choices=['host', 'client', 'disconnect', 'target-disconnect', 'multiple', 'multiple-disconnect'], default='client')
    parser.add_argument('--players', type=int, choices=range(3, 9), default=4)
    parser.add_argument('--graphics-client', type=int, default=-1,
                        help='Render this client to inspect the ghost or the victim shelf.')
    parser.add_argument('--latency-ms', type=int, default=0)
    parser.add_argument('--jitter-ms', type=int, default=0)
    args = parser.parse_args()
    if args.latency_ms < 0 or args.jitter_ms < 0:
        parser.error('Delay and jitter must be nonnegative')
    root = pathlib.Path(__file__).resolve().parents[1]
    app = root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    logs = root / 'igruha/Builds/Autotest/logs' / f'ghost-{args.scenario}-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    (logs / 'network.txt').write_text(f'one-way delay={args.latency_ms} ms; jitter={args.jitter_ms} ms\n')
    processes, links = [], []
    group = args.scenario.startswith('multiple')
    count = args.players if group else 3
    prefix = 'GHOST_GROUP' if group else 'GHOST_CHECK'
    try:
        for i in range(count):
            command = [str(app), '--cans-ghost-group-check' if group else '--cans-ghost-check', args.scenario, '--bot',
                       '-logFile', str(logs / f'{i}.log')]
            if i == 0:
                command += ['--autostart', 'CansOrder', '--wait-players', str(count),
                            '--ghost-screenshots', str(logs / 'host'),
                            '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720']
            else:
                port = 7777
                if args.latency_ms or args.jitter_ms:
                    link = DelayedLink(args.latency_ms, args.jitter_ms, i)
                    links.append(link)
                    port = link.port
                command += ['--client', '--host', '127.0.0.1', '--port', str(port)]
                if i == args.graphics_client:
                    command += ['--ghost-screenshots', str(logs / f'client-{i}'),
                                '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720']
                else:
                    command += ['-batchmode', '-nographics']
            processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            if i == 0:
                time.sleep(4)
            else:
                # A graphical client can boot slower than the next headless one.
                # Wait for its connection so process indices really match the role IDs.
                connected_by = time.monotonic() + 20
                client_log = logs / f'{i}.log'
                while time.monotonic() < connected_by:
                    if client_log.exists() and 'CLIENT: подключился к хосту' in client_log.read_text(errors='replace'):
                        break
                    if processes[-1].poll() is not None:
                        raise RuntimeError(f'Client {i} exited before connecting')
                    time.sleep(.2)
                else:
                    raise RuntimeError(f'Client {i} did not connect in time')
        deadline = time.monotonic() + 150
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        ok = all(p.poll() == 0 for p in processes)
        for log in sorted(logs.glob('*.log')):
            contents = log.read_text(errors='replace')
            lines = [line for line in contents.splitlines() if line.startswith(prefix)]
            disconnected = any(' DISCONNECT ' in line for line in lines)
            passed = prefix + ' FAIL' not in contents and 'Exception:' not in contents
            passed &= disconnected or prefix + ' PASS=True' in contents
            ok &= passed
            print(log.name, 'PASS' if passed else 'FAIL', '\n' + '\n'.join(lines), flush=True)
        if group:
            orders = [[line for line in log.read_text(errors='replace').splitlines() if line.startswith('GHOST_GROUP ORDER ')]
                      for log in sorted(logs.glob('*.log'))]
            ok &= len(orders) == count and all(o and o[0] == orders[0][0] for o in orders)
            second_orders = [o[1] for o in orders if len(o) > 1]
            ok &= len(second_orders) == count - (1 if args.scenario == 'multiple-disconnect' else 0) and len(set(second_orders)) == 1
        else:
            first = [line for log in logs.glob('*.log') for line in log.read_text(errors='replace').splitlines() if line.startswith('GHOST_CHECK FIRST ')]
            ok &= len(first) == 3 and len(set(first)) == 1
            if args.scenario != 'disconnect':
                second = [line for log in logs.glob('*.log') for line in log.read_text(errors='replace').splitlines() if line.startswith('GHOST_CHECK SECOND ')]
                ok &= len(second) == (2 if args.scenario == 'target-disconnect' else 3) and len(set(second)) == 1
        print('CANS GHOST:', 'PASS' if ok else 'FAIL', flush=True)
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
    raise SystemExit(main())
