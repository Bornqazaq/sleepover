#!/usr/bin/env python3
"""Extract Mosquitoes development probes without Unity stack trace noise.

Usage: python tools/report-mosquitoes-network.py <log-directory> [--output <json>]
Disconnected processes can legitimately have no results; inspect each participant.
"""
import argparse
import json
import re
from pathlib import Path

CHECK = re.compile(
    r"\[MosquitoCheck\] local=(\d+) giant=(\d+) phase=(\w+) sleep=([\d.,]+) "
    r"state=(\w+) lamp=(\w+) alive=(\d+) bodies=(\d+) countdown=([\d.,]+) "
    r"elapsed=([\d.,]+) netTime=([\d.,]+)"
)
FIELDS = ("local", "giant", "phase", "sleep", "state", "lamp", "alive", "bodies", "countdown", "elapsed", "netTime")
EVENT = re.compile(r"\[Mosquito(?:Boundary|Fixture|Authority|Results|Looks)\]|итоги раунда|CLIENT (?:STOPPED|DISCONNECTED)|управление возвращено|\[Hub\]")
ERROR = re.compile(r"(?:\w+Exception:|server failed to correct|Could not enqueue received packet|Transport failure!|Assertion failed)")


def parse(path):
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    samples = []
    for line in lines:
        match = CHECK.search(line)
        if not match:
            continue
        sample = dict(zip(FIELDS, match.groups()))
        for key in ("local", "giant", "alive", "bodies"):
            sample[key] = int(sample[key])
        for key in ("sleep", "countdown", "elapsed", "netTime"):
            sample[key] = float(sample[key].replace(",", "."))
        samples.append(sample)
    return {
        "file": str(path),
        "events": [line for line in lines if EVENT.search(line)],
        "errors": [line for line in lines if ERROR.search(line)],
        "results": [line for line in lines if "📊 итоги раунда" in line],
        "samples": samples,
        "flights": [line for line in lines if "[MosquitoFlight]" in line],
    }


def compare(host, client):
    pairs = []
    for sample in client["samples"]:
        if sample["phase"] != "Round" or not host["samples"]:
            continue
        nearest = min(host["samples"], key=lambda other: abs(other["netTime"] - sample["netTime"]))
        skew = abs(nearest["netTime"] - sample["netTime"])
        if nearest["phase"] != "Round" or skew > .5:
            continue
        pairs.append({
            "clientTime": sample["netTime"], "skew": round(skew, 3),
            "sleepDelta": round(abs(nearest["sleep"] - sample["sleep"]), 3),
            "sameState": all(sample[key] == nearest[key] for key in ("giant", "state", "lamp", "alive")),
        })
    return {"pairedSamples": len(pairs), "pairs": pairs,
            "maxSleepDelta": max((pair["sleepDelta"] for pair in pairs), default=None),
            "stateMismatches": sum(not pair["sameState"] for pair in pairs)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    logs = {path.stem: parse(path) for path in sorted(args.directory.glob("*.log"))}
    if not logs or "host" not in logs:
        parser.error("Expected host.log and client logs")
    host_results = logs["host"]["results"]
    report = {"directory": str(args.directory.resolve()), "participants": logs,
              "comparisons": {name: compare(logs["host"], log) for name, log in logs.items() if name != "host"}}
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    summary = {
        name: {"samples": len(log["samples"]), "last": log["samples"][-1] if log["samples"] else None,
               "resultsAgree": bool(host_results and log["results"] == host_results),
               "restoredControl": any("управление возвращено" in line for line in log["events"]),
               "errors": log["errors"]}
        for name, log in logs.items()
    }
    print(json.dumps({"participants": summary, "comparisons": {
        name: {key: value for key, value in result.items() if key != "pairs"}
        for name, result in report["comparisons"].items()
    }}, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
