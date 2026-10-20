#!/usr/bin/env python3
"""Real host/client checks of the circus duel, including roster transition and intro disconnect."""
import argparse
import pathlib
import subprocess
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', choices=['Stopwatch', 'CansOrder'], required=True)
    parser.add_argument('--scenario', choices=['pair', 'transition', 'disconnect', 'pit-client', 'pit-host'], default='pair')
    parser.add_argument('--graphics-client', type=int, default=-1,
                        help='Render this client as well, to inspect replicated portraits or the pit view.')
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parents[1]
    app = root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    logs = root / 'igruha/Builds/Autotest/logs' / f'duel-{args.game}-{args.scenario}-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    processes = []
    count = 4 if args.scenario.startswith('pit-') else 3 if args.scenario == 'transition' else 2
    try:
        for i in range(count):
            command = [str(app), '--circus-duel-check', args.scenario, '--bot',
                       '-logFile', str(logs / f'{i}.log')]
            if i == 0:
                command += ['--autostart', args.game, '--wait-players', str(count),
                            '--duel-screenshots', str(logs / 'host'),
                            '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720']
            else:
                command += ['--client', '--host', '127.0.0.1']
                if i == args.graphics_client:
                    command += ['--duel-screenshots', str(logs / f'client-{i}'),
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
        deadline = time.monotonic() + 125
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        ok = all(p.poll() == 0 for p in processes)
        states = []
        for log in sorted(logs.glob('*.log')):
            contents = log.read_text(errors='replace')
            lines = [line for line in contents.splitlines() if line.startswith('DUEL_CHECK')]
            disconnected = any(' DISCONNECT ' in line for line in lines)
            passed = 'DUEL_CHECK FAIL' not in contents and 'Exception:' not in contents
            passed &= disconnected or 'DUEL_CHECK PASS=True' in contents
            states += [line for line in lines if ' SNAPSHOT ' in line]
            ok &= passed
            print(log.name, 'PASS' if passed else 'FAIL', '\n' + '\n'.join(lines), flush=True)
        ok &= len(states) == (4 if args.scenario.startswith('pit-') else 2) and len(set(states)) == 1
        print('CIRCUS DUEL:', 'PASS' if ok else 'FAIL', flush=True)
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
    raise SystemExit(main())
