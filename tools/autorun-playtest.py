#!/usr/bin/env python3
"""IGR-580 opt-in real Host + 3 Client regression checks; macOS development build."""
import argparse
import datetime
import pathlib
import subprocess
import time

ROOT = pathlib.Path(__file__).resolve().parents[1]
APP = ROOT / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
GAMES = {'carry': 'CarryItem', 'infection': 'Infection', 'exam': 'Exam', 'cans': 'CansOrder',
         'memory': 'MemoryRun', 'angels': 'CryingAngels', 'angels-roles': 'CryingAngels', 'tutorial': 'MemoryRun', 'footsteps': None,
         'countdown-infection': 'Infection', 'countdown-carry': 'CarryItem',
         'countdown-angels': 'CryingAngels', 'countdown-duck': 'DuckHunt',
         'countdown-sumo': 'SumoRing', 'countdown-bullet': 'OneBullet',
         'circus-cans': 'CansOrder', 'circus-stopwatch': 'Stopwatch'}


def run(mode, port, players=4, visible=False, circus_motion_review=False):
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    logs = ROOT / 'igruha/Builds/Autotest/logs' / f'playtest-{mode}-{stamp}'
    logs.mkdir(parents=True)
    processes = []
    print(f'{mode}: {logs}', flush=True)
    try:
        for index in range(players):
            role = ['--autostart', GAMES[mode], '--wait-players', str(players)] if index == 0 else ['--client', '--host', '127.0.0.1']
            if mode == 'footsteps' and index == 0:
                role = []
            path = logs / ('host.log' if index == 0 else f'client-{index}.log')
            args = [str(APP), *role, '--port', str(port), '--bot', '--playtest-check', mode,
                    '-batchmode', '-logFile', str(path)]
            if mode != 'footsteps':
                args.append('-nographics')
            if visible and index < 2:
                args.remove('-batchmode'); args.remove('-nographics')
                args += ['-screen-fullscreen','0','-screen-width','1280','-screen-height','720',
                         '--playtest-screenshots',str(logs/('host' if index==0 else 'client-1'))]
                if circus_motion_review and mode.startswith('circus-'):
                    args.append('--circus-motion-review')
            if mode == 'tutorial':
                args += ['--tutorial-check', 'repeat']
            if mode.startswith('countdown-') or mode == 'angels-roles':
                args += ['--tutorial-check', 'countdown']
            processes.append(subprocess.Popen(args, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT))
            time.sleep(6 if index == 0 else 1.5)
        deadline = time.monotonic() + 360
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        passed = True
        for path in sorted(logs.glob('*.log')):
            contents = path.read_text(errors='replace')
            lines = [line for line in contents.splitlines() if 'PLAYTEST_CHECK' in line and 'cans stage=' not in line]
            print(path.name + ':\n' + '\n'.join(lines), flush=True)
            passed &= ('PLAYTEST_CHECK PASS' in contents and 'PLAYTEST_CHECK FAIL' not in contents
                       and 'Exception:' not in contents and 'NetworkConfig mismatch' not in contents)
            if mode == 'tutorial':
                passed &= 'TUTORIAL_CHECK PASS' in contents and 'TUTORIAL_CHECK FAIL' not in contents
        passed &= len(list(logs.glob('*.log'))) == players
        passed &= all(p.poll() == 0 for p in processes)
        print(f'{mode}: {"PASS" if passed else "FAIL"}', flush=True)
        return passed
    finally:
        for process in processes:
            if process.poll() is None:
                process.terminate()
        for process in processes:
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('modes', nargs='*', default=list(GAMES), choices=list(GAMES))
    parser.add_argument('--port', type=int, default=17600)
    parser.add_argument('--players',type=int,default=4,choices=range(2,9))
    parser.add_argument('--visible',action='store_true')
    parser.add_argument('--circus-motion-review',action='store_true',help='Capture 12 fps circus motion frames in visible windows')
    options = parser.parse_args()
    if not APP.is_file():
        parser.error(f'Missing development build: {APP}')
    results = [run(mode, options.port + index, options.players, options.visible, options.circus_motion_review)
               for index, mode in enumerate(options.modes)]
    raise SystemExit(0 if all(results) else 1)
