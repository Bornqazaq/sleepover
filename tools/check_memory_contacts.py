#!/usr/bin/env python3
"""Exercise short contacts, skipped rows and complete attempts over real NGO transport.
Build first through Unity MCP: Igruha.EditorTools.AutotestBuild.BuildMac.
The development probe stages owner positions; it never calls a gameplay verdict.
"""
import argparse
import pathlib
import subprocess
import time

from check_sumo import DelayedLink


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--latency-ms', type=int, default=0)
    parser.add_argument('--jitter-ms', type=int, default=0)
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parents[1]
    app = root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    logs = root / 'igruha/Builds/Autotest/logs' / f'memory-contacts-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    processes, links = [], []
    try:
        for i in range(3):
            command = [str(app), '--memory-contact-check', '-batchmode', '-nographics',
                       '-logFile', str(logs / f'player-{i}.log')]
            if i == 0:
                command += ['--autostart', 'MemoryRun', '--wait-players', '3']
            else:
                port = 7777
                if args.latency_ms or args.jitter_ms:
                    link = DelayedLink(args.latency_ms, args.jitter_ms, i)
                    links.append(link)
                    port = link.port
                command += ['--client', '--host', '127.0.0.1', '--port', str(port)]
            processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            time.sleep(5 if i == 0 else 1)
        deadline = time.monotonic() + 240
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        ok = all(p.poll() == 0 for p in processes)
        snapshots = []
        for path in sorted(logs.glob('*.log')):
            contents = path.read_text(errors='replace')
            lines = [line for line in contents.splitlines() if line.startswith('MEMORY_CONTACT')]
            passed = ('MEMORY_CONTACT FAIL' not in contents and 'Exception:' not in contents and
                      any('RETURN passed=True' in line for line in lines) and
                      any('BRIEF_MINE' in line for line in lines))
            snapshots += [line for line in lines if ' FINAL' in line]
            ok &= passed
            print(path.name, 'PASS' if passed else 'FAIL', '\n' + '\n'.join(lines), flush=True)
        ok &= len(snapshots) == 3 and len(set(snapshots)) == 1
        print('MEMORY CONTACT CHECK:', 'PASS' if ok else 'FAIL', flush=True)
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
