#!/usr/bin/env python3
"""Real NGO players: keyboard flight or bot rules, with optional bidirectional UDP delay."""
import argparse
import json
import pathlib
import subprocess
import time
from check_sumo import DelayedLink


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--players', type=int, choices=range(3, 9), default=3)
    parser.add_argument('--scenario', default='input')
    parser.add_argument('--giant', type=int, default=0)
    parser.add_argument('--latency-ms', type=int, default=0)
    parser.add_argument('--jitter-ms', type=int, default=0)
    parser.add_argument('--seconds', type=int, default=90)
    parser.add_argument('--capture', action='store_true', help='Render host at 1280x720 and capture navigation milestones')
    args = parser.parse_args()
    if args.scenario == 'giant-leaves' and args.giant == 0:
        parser.error('giant-leaves requires a client giant')
    if args.scenario == 'mosquitoes-leave' and args.giant != 0:
        parser.error('mosquitoes-leave requires the host giant')
    def leaves(i):
        return args.scenario == 'giant-leaves' and i == args.giant or args.scenario == 'mosquitoes-leave' and i != args.giant
    root = pathlib.Path(__file__).resolve().parents[1]
    app = root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    logs = root / 'igruha/Builds/Autotest/logs' / f'mosquitoes-{args.scenario}-{args.players}-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    processes, links = [], []
    (logs / 'network.txt').write_text(f'one-way delay={args.latency_ms} ms; jitter={args.jitter_ms} ms; giant={args.giant}\n')
    try:
        for i in range(args.players):
            command = [str(app), '--mosquito-check', args.scenario, '--mosquito-giant', str(args.giant),
                       '-logFile', str(logs / f'player-{i}.log')]
            if i == 0 and args.capture:
                command += ['--mosquito-capture', str(logs / 'captures'), '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720']
            else:
                command += ['-batchmode', '-nographics']
            command += ['--mosquito-input-check', args.scenario] if args.scenario in ('input', 'navigation', 'swat') else ['--bot']
            if i == 0:
                command += ['--autostart', 'Mosquitoes', '--wait-players', str(args.players)]
            else:
                port = 7777
                if args.latency_ms or args.jitter_ms:
                    link = DelayedLink(args.latency_ms, args.jitter_ms, i)
                    links.append(link)
                    port = link.port
                command += ['--client', '--host', '127.0.0.1', '--port', str(port)]
            processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            time.sleep(5 if i == 0 else 1)
        deadline = time.monotonic() + args.seconds
        while time.monotonic() < deadline:
            contents = [p.read_text(errors='replace') for p in sorted(logs.glob('*.log'))]
            if len(contents) == args.players and all(('[MosquitoFixture] graceful leave' if leaves(i) else '[MosquitoResults]') in s for i, s in enumerate(contents)):
                time.sleep(2)
                break
            if all(p.poll() is not None for p in processes):
                break
            time.sleep(1)
        ok = True
        results = []
        peers = []
        for i, path in enumerate(sorted(logs.glob('*.log'))):
            content = path.read_text(errors='replace')
            lines = [line for line in content.splitlines() if line.startswith(('MOSQUITO_INPUT', 'MOSQUITO_SPAWN', 'MOSQUITO_NAV', 'MOSQUITO_SWAT', '[MosquitoResults]', '[MosquitoBoundary]'))]
            passed = '[MosquitoResults]' in content and 'Exception:' not in content and 'MOSQUITO_INPUT FAIL' not in content and 'MOSQUITO_SPAWN FAIL' not in content
            if leaves(i):
                passed = '[MosquitoFixture] graceful leave' in content and 'Exception:' not in content
            if args.scenario == 'input' and i != args.giant:
                passed &= content.count('MOSQUITO_INPUT PASS') == 3
                passed &= 'MOSQUITO_SPAWN PASS' in content
            if args.scenario == 'flight-boundaries' and i != args.giant:
                passed &= all(f'probe={probe} corrected=' in content for probe in range(3))
                passed &= 'server failed to correct' not in content
            if args.scenario == 'navigation':
                passed &= 'MOSQUITO_NAV PASS' in content and 'MOSQUITO_NAV FAIL' not in content
            if args.scenario == 'swat':
                passed &= 'MOSQUITO_SWAT PASS' in content and 'MOSQUITO_SWAT FAIL' not in content
            for line in lines:
                if line.startswith('[MosquitoResults]'):
                    results.append(line.split(' places=')[1].split(' restored=')[0])
                    passed &= line.endswith(' bodies=0')
            ok &= passed
            peers.append({'player': i, 'passed': passed, 'checks': lines})
            print(path.name, 'PASS' if passed else 'FAIL', '\n' + '\n'.join(lines), flush=True)
        ok &= len(results) == args.players - sum(leaves(i) for i in range(args.players)) and len(set(results)) == 1
        (logs / 'report.json').write_text(json.dumps({'scenario': args.scenario, 'players': args.players,
            'giant': args.giant, 'one_way_delay_ms': args.latency_ms, 'jitter_ms': args.jitter_ms,
            'passed': ok, 'peers': peers, 'results': results}, indent=2) + '\n')
        print('MOSQUITO CHECK:', 'PASS' if ok else 'FAIL', flush=True)
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
