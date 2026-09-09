"""Send one command to the installed Blender MCP socket server.

Examples:
  python3 tools/blender_client.py get_scene_info
  python3 tools/blender_client.py execute_code --file /tmp/model.py
  python3 tools/blender_client.py get_object_info --params '{"name":"Cube"}'
"""
import argparse
import json
from pathlib import Path
import socket
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", nargs="?", default="get_scene_info")
    parser.add_argument("--params", default="{}")
    parser.add_argument("--file", type=Path)
    parser.add_argument("--timeout", type=float, default=120)
    args = parser.parse_args()
    params = json.loads(args.params)
    if args.file:
        if args.command != "execute_code":
            parser.error("--file requires execute_code")
        params["code"] = f"__file__ = {str(args.file.resolve())!r}\n" + args.file.read_text()
    payload = json.dumps({"type": args.command, "params": params}).encode()
    with socket.create_connection(("127.0.0.1", 9876), timeout=args.timeout) as conn:
        conn.settimeout(args.timeout)
        conn.sendall(payload)
        received = bytearray()
        while True:
            chunk = conn.recv(65536)
            if not chunk:
                raise ConnectionError("Blender disconnected before a complete response")
            received.extend(chunk)
            try:
                result = json.loads(received)
                break
            except (json.JSONDecodeError, UnicodeDecodeError):
                continue
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result.get("status") == "success" else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError) as exc:
        print(f"Blender bridge: {exc}", file=sys.stderr)
        sys.exit(1)
