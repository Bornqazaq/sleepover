#!/usr/bin/env python3
"""IGR-664: real Host + 3 clients, bottle loss/replication and network teardown.
Requires a development player built with Unity MCP in Builds/Autotest.
"""
import argparse
import pathlib
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--scenario', choices=[
        'host-exit', 'host-quit', 'host-held-quit', 'client-exit', 'drop', 'throw'],
        default='host-exit')
    parser.add_argument('--port', type=int, default=17594)
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parents[1]
    app = root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    if not app.is_file():
        parser.error('Build the development player first')
    logs = root / 'igruha/Builds/Autotest/logs' / f'carry-lifecycle-{args.scenario}-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    processes = []
    try:
        for i in range(4):
            log = logs / ('host.log' if i == 0 else f'client-{i}.log')
            command = [str(app), '--carry-lifecycle-check', args.scenario,
                       '--port', str(args.port), '-batchmode', '-nographics', '-logFile', str(log)]
            command += (['--autostart', 'CarryItem', '--wait-players', '4'] if i == 0
                        else ['--client', '--host', '127.0.0.1'])
            processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            time.sleep(5 if i == 0 else 1)
        deadline = time.monotonic() + 130
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        ok = all(p.poll() == 0 for p in processes)
        for log in sorted(logs.glob('*.log')):
            contents = log.read_text(errors='replace')
            passed = ('CARRY_LIFECYCLE PASS' in contents and 'CARRY_LIFECYCLE READY' in contents
                      and 'CARRY_LIFECYCLE FAIL' not in contents and 'Exception:' not in contents
                      and 'NetworkConfig mismatch' not in contents)
            ok &= passed
            lines = [line for line in contents.splitlines()
                     if line.startswith('CARRY_LIFECYCLE') or 'Exception:' in line]
            print(log.name, 'PASS' if passed else 'FAIL', '\n' + '\n'.join(lines), flush=True)
        print('CARRY LIFECYCLE CHECK:', 'PASS' if ok else 'FAIL', flush=True)
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
