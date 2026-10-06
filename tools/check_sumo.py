#!/usr/bin/env python3
"""Run a real localhost SumoRing development build: punches, jump, collapse, results, hub.
Usage: python3 tools/check_sumo.py --players 8 [--scenario disconnect]
Build first with Igruha.EditorTools.AutotestBuild.BuildMac (Unity MCP).
This is an automated transport/physics check, not a human balance playtest.
"""
import argparse
import heapq
import pathlib
import random
import select
import socket
import subprocess
import sys
import threading
import time


class DelayedLink:
    """One client's real UDP traffic, delayed in both directions without touching the build."""
    def __init__(self, latency_ms, jitter_ms, seed):
        self.delay, self.jitter = latency_ms / 1000, jitter_ms / 1000
        self.random = random.Random(seed)
        self.front, self.back = socket.socket(socket.AF_INET, socket.SOCK_DGRAM), socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.front.bind(('127.0.0.1', 0))
        self.back.bind(('127.0.0.1', 0))
        self.port = self.front.getsockname()[1]
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def run(self):
        pending, client, sequence = [], None, 0
        while not self.stop.is_set():
            ready, _, _ = select.select([self.front, self.back], [], [], .005)
            for incoming in ready:
                data, address = incoming.recvfrom(65535)
                if incoming is self.front:
                    client = address
                    outgoing, target = self.back, ('127.0.0.1', 7777)
                else:
                    if client is None:
                        continue
                    outgoing, target = self.front, client
                due = time.monotonic() + max(0, self.delay + self.random.uniform(-self.jitter, self.jitter))
                sequence += 1
                heapq.heappush(pending, (due, sequence, outgoing, target, data))
            while pending and pending[0][0] <= time.monotonic():
                _, _, outgoing, target, data = heapq.heappop(pending)
                outgoing.sendto(data, target)

    def close(self):
        self.stop.set()
        self.thread.join(timeout=1)
        self.front.close()
        self.back.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--players', type=int, choices=range(2, 9), default=3)
    parser.add_argument('--scenario', choices=['basic', 'disconnect', 'combat', 'dash'], default='basic')
    parser.add_argument('--app', type=pathlib.Path)
    parser.add_argument('--capture', action='store_true', help='Capture PNGs; use separately from timing-sensitive regression')
    parser.add_argument('--trace', action='store_true', help='Log input arrival and authority windows')
    parser.add_argument('--visual', action='store_true', help='Combat plus all-avatar motions, edge balance and jump checks')
    parser.add_argument('--mutual', action='store_true', help='Short two-player same-tick heavy trade and scene return')
    parser.add_argument('--latency-ms', type=int, default=0, help='One-way UDP delay; 75 gives approximately 150 ms RTT')
    parser.add_argument('--jitter-ms', type=int, default=0, help='Variation of each packet delay')
    args = parser.parse_args()
    if args.visual and args.scenario != 'combat':
        parser.error('--visual requires --scenario combat')
    if args.mutual and (args.scenario != 'combat' or args.players != 2 or args.visual):
        parser.error('--mutual requires --scenario combat --players 2 and no --visual')
    if args.latency_ms < 0 or args.jitter_ms < 0:
        parser.error('Delay and jitter must be nonnegative')
    root = pathlib.Path(__file__).resolve().parents[1]
    app = args.app or root / 'igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover'
    if not app.is_file():
        parser.error(f'Development player not found: {app}')
    logs = root / 'igruha/Builds/Autotest/logs' / f'sumo-{args.scenario}-{args.players}-{time.strftime("%Y%m%d-%H%M%S")}'
    logs.mkdir(parents=True)
    print(logs, flush=True)
    processes, links = [], []
    (logs / 'network.txt').write_text(f'one-way latency={args.latency_ms} ms, jitter=+/-{args.jitter_ms} ms\n')
    try:
        for i in range(args.players):
            log = logs / ('host.log' if i == 0 else f'client-{i}.log')
            command = [str(app), '--sumo-check', args.scenario, '--bot', '-logFile', str(log)]
            if args.capture:
                command += ['--sumo-capture', '1']
            if args.trace:
                command += ['--sumo-trace', '1']
            if args.visual:
                command += ['--sumo-visual-check', '1']
            if args.mutual:
                command += ['--sumo-mutual-check', '1']
            if i == 0:
                command += ['--autostart', 'SumoRing', '--wait-players', str(args.players), '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720']
            else:
                command += ['--client', '--host', '127.0.0.1', '-batchmode', '-nographics']
                if args.latency_ms or args.jitter_ms:
                    link = DelayedLink(args.latency_ms, args.jitter_ms, seed=i)
                    links.append(link)
                    command += ['--port', str(link.port)]
            processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
            time.sleep(5 if i == 0 else 1)
        (logs / 'pids.txt').write_text('\n'.join(str(p.pid) for p in processes))
        deadline = time.monotonic() + 220
        while any(p.poll() is None for p in processes) and time.monotonic() < deadline:
            time.sleep(1)
        ok = all(p.poll() == 0 for p in processes)
        results = []
        for log in [logs / 'host.log'] + [logs / f'client-{i}.log' for i in range(1, args.players)]:
            text = log.read_text(errors='replace')
            intentional = args.scenario == 'disconnect' and log.name == f'client-{args.players - 1}.log'
            passed = 'SUMO_CHECK DISCONNECT' in text if intentional else 'SUMO_CHECK RETURN restored=True' in text
            passed &= 'SUMO_CHECK FAIL' not in text and 'SUMO_COMBAT FAIL' not in text and 'Exception:' not in text
            if args.scenario == 'combat' and not args.mutual: passed &= 'SUMO_COMBAT COMPLETE passed=11/11' in text
            if args.scenario == 'combat' and not args.mutual: passed &= 'SUMO_FEEDBACK FAIL' not in text and all('SUMO_FEEDBACK PASS sound=' + slot in text for slot in ('sumo_hit', 'sumo_block', 'sumo_heavy', 'sumo_break', 'sumo_parry'))
            if args.scenario == 'dash': passed &= 'SUMO_DASH COMPLETE passed=6/6' in text and 'SUMO_DASH FAIL' not in text
            if args.mutual: passed &= 'SUMO_MUTUAL PASS contacts=2 stumbleMask=3' in text and 'SUMO_MUTUAL FAIL' not in text
            if args.visual: passed &= 'SUMO_VISUAL COMPLETE passed=5/5' in text and 'SUMO_VISUAL FAIL' not in text
            ok &= passed
            lines = [line for line in text.splitlines() if line.startswith(('SUMO_CHECK', 'SUMO_COMBAT', 'SUMO_DASH')) or 'итоги раунда SumoRing' in line]
            print(log.name, 'PASS' if passed else 'FAIL', '\n' + '\n'.join(lines[-6:]), flush=True)
            results += [line for line in lines if 'итоги раунда SumoRing' in line]
        # Every remaining peer must agree on the complete ordered results record.
        ok &= len(set(results)) == 1 and len(results) == args.players - (args.scenario == 'disconnect')
        print('SUMO LOCALHOST CHECK:', 'PASS' if ok else 'FAIL', flush=True)
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
