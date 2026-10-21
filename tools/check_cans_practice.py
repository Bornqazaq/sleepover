#!/usr/bin/env python3
"""Check private live practice feedback and its absence in the real CansOrder round."""
import pathlib
import subprocess
import time
from check_sumo import DelayedLink


def main():
    root = pathlib.Path(__file__).resolve().parents[1]
    app = root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    logs = root / 'igruha/Builds/Autotest/logs' / f'cans-practice-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    processes, links = [], []
    try:
        for i in range(3):
            # The reserved countdown mode disables automatic bot readiness without
            # installing TutorialNetworkProbe; CansPracticeProbe owns readiness.
            command = [str(app), '--cans-practice-check', '--tutorial-check', 'countdown', '--bot',
                       '-logFile', str(logs / f'{i}.log')]
            if i == 0:
                command += ['--autostart', 'CansOrder', '--wait-players', '3']
            else:
                link = DelayedLink(75, 20, i)
                links.append(link)
                command += ['--client', '--host', '127.0.0.1', '--port', str(link.port)]
            if i < 2:
                command += ['-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
                            '--practice-screenshot', str(logs / f'{i}.png')]
            else:
                command += ['-batchmode', '-nographics']
            processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            if i == 0:
                time.sleep(4)
            else:
                deadline = time.monotonic() + 25
                client_log = logs / f'{i}.log'
                while time.monotonic() < deadline:
                    if client_log.exists() and 'CLIENT: подключился к хосту' in client_log.read_text(errors='replace'):
                        break
                    if processes[-1].poll() is not None:
                        raise RuntimeError(f'Client {i} exited before connecting')
                    time.sleep(.2)
                else:
                    raise RuntimeError(f'Client {i} did not connect')
        deadline = time.monotonic() + 150
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        ok = all(p.poll() == 0 for p in processes)
        for p in sorted(logs.glob('*.log')):
            contents = p.read_text(errors='replace')
            passed = 'CANS_PRACTICE PASS=True' in contents and 'CANS_PRACTICE FAIL' not in contents and 'Exception:' not in contents
            ok &= passed
            print(p.name, 'PASS' if passed else 'FAIL', flush=True)
            print('\n'.join(s for s in contents.splitlines() if s.startswith('CANS_PRACTICE')), flush=True)
        print('CANS PRACTICE:', 'PASS' if ok else 'FAIL', flush=True)
        return 0 if ok else 1
    finally:
        for p in processes:
            if p.poll() is None:
                p.terminate()
        for p in processes:
            try:
                p.wait(timeout=8)
            except subprocess.TimeoutExpired:
                p.kill()
                p.wait()
        for link in links:
            link.close()


if __name__ == '__main__':
    raise SystemExit(main())
