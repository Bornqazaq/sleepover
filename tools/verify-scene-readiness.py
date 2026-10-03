#!/usr/bin/env python3
"""Host + clients: scene barrier, delayed load, same-arena reset, and keyboard input.

Uses the development build by default. --app also accepts a release executable
with --mode release (checks the production barrier without development probes).
Only stops processes started by this invocation; logs survive cleanup.
"""
import argparse
import datetime
import json
import os
from pathlib import Path
import signal
import subprocess
import time

ROOT = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("games")
parser.add_argument("--players", type=int, default=4, choices=range(2, 9))
parser.add_argument("--delay", type=float, default=0)
parser.add_argument("--expect-drop", action="store_true", help="Last client exceeds the transport timeout; remaining peers must start")
parser.add_argument("--port", type=int, default=7785)
parser.add_argument("--mode", choices=["flow", "input", "release"], default="flow")
parser.add_argument("--timeout", type=float, default=600)
parser.add_argument("--app", type=Path, default=ROOT / "igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover")
args = parser.parse_args()
logs = ROOT / "igruha/Builds/Autotest/logs" / ("igr705-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S") + "-" + str(args.port))
logs.mkdir(parents=True)
processes = []
events = []
started = time.monotonic()
passed = False


def event(message):
    line = f"{time.monotonic() - started:.3f}s {message}"
    events.append(line)
    print(line, flush=True)


def read(path):
    return path.read_text(errors="replace") if path.exists() else ""


try:
    event(str(logs))
    paths = [logs / ("host.log" if i == 0 else f"client-{i}.log") for i in range(args.players)]
    for i, path in enumerate(paths):
        role = ["--autostart", args.games, "--series", "--wait-players", str(args.players)] if i == 0 else ["--client", "--host", "127.0.0.1"]
        bot = [] if args.mode == "input" else ["--bot"]
        probe = [] if args.mode == "release" else ["--scene-ready-check", args.mode]
        command = [str(args.app), *role, *bot, *probe, "--port", str(args.port), "-batchmode", "-nographics", "-logFile", str(path)]
        processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT))
        time.sleep(5 if i == 0 else 1)
    paused_at = None
    resumed = not args.delay
    seen = 0
    while time.monotonic() - started < args.timeout:
        host = read(paths[0])
        if paused_at is None and "HOST: гружу мини-игру '" in host:
            paused_at = time.monotonic()
            if args.delay:
                processes[-1].send_signal(signal.SIGSTOP)
                event(f"PAUSED last client for {args.delay}s")
        if not resumed and paused_at is not None:
            if time.monotonic() - paused_at >= args.delay:
                if not args.expect_drop:
                    assert ": START participants=" not in host, "Started while a client was paused"
                processes[-1].send_signal(signal.SIGCONT)
                resumed = True
                event("RESUMED last client" + (" after transport timeout" if args.expect_drop else "; barrier held"))
        lines = host.splitlines()
        for line in lines[seen:]:
            if any(s in line for s in ("[SceneReady]", "SCENE_READY_", "Exception", "DISCONNECTED")):
                event("HOST " + line)
        seen = len(lines)
        content = [read(path) for path in paths]
        active_content = content[:-1] if args.expect_drop else content
        if any("SCENE_READY_CHECK FAIL" in c or "SCENE_READY_INPUT FAIL" in c for c in content):
            raise AssertionError("Integration probe failed; inspect logs")
        if any(p.poll() is not None for p in processes):
            raise AssertionError("A player exited unexpectedly")
        expected = args.games.split(",")
        if args.mode == "flow":
            passed = all(all("SCENE_READY_CHECK PASS scene=" + game + " " in c for game in expected) for c in active_content)
        elif args.mode == "input":
            passed = all(c.count("SCENE_READY_INPUT PASS") >= 2 and "SCENE_READY_CHECK PASS" in c for c in content)
        else:
            passed = all(c.count(": START participants=" + str(args.players - int(args.expect_drop))) >= 2 for c in active_content)
            if passed and time.monotonic() - paused_at < args.delay + 15: passed = False
        if args.expect_drop:
            passed = passed and "CLIENT DISCONNECTED" in host and "START participants=" + str(args.players - 1) in host
        if resumed and passed:
            break
        time.sleep(0.05)
    assert passed, "Timed out before every peer passed"
finally:
    # Host first: this also exercises callbacks during actual application quit.
    for process in processes:
        if process.poll() is None:
            process.send_signal(signal.SIGCONT)
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
    (logs / "events.txt").write_text("\n".join(events) + "\n")

errors = {}
for path in paths:
    bad = [line for line in read(path).splitlines() if "Exception:" in line or "SCENE_READY_CHECK FAIL" in line or "SCENE_READY_INPUT FAIL" in line]
    if bad: errors[path.name] = bad
summary = {"passed": passed and not errors, "games": args.games, "players": args.players, "delay": args.delay, "mode": args.mode, "errors": errors}
(logs / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n")
event(json.dumps(summary, ensure_ascii=False))
if errors: raise SystemExit(1)
