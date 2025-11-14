#!/usr/bin/env python3
"""Decode a base64 payload from the PAYLOAD env var and append to scoreboard.jsonl."""
import base64
import json
import os
import sys
from pathlib import Path

SCOREBOARD_PATH = Path("scoreboard.jsonl")

def main() -> int:
    payload = os.environ.get("PAYLOAD")
    if not payload:
        print("PAYLOAD env var not provided; skipping append.")
        return 0

    try:
        decoded = base64.b64decode(payload).decode("utf-8")
    except Exception as exc:  # noqa: BLE001
        print(f"Failed to decode payload: {exc}")
        return 1

    line = decoded.strip()
    if not line:
        print("Decoded payload is empty; nothing to append.")
        return 0

    try:
        json.loads(line)
    except json.JSONDecodeError as exc:
        print(f"Payload is not valid JSON: {exc}")
        return 1

    SCOREBOARD_PATH.parent.mkdir(parents=True, exist_ok=True)
    with SCOREBOARD_PATH.open("a", encoding="utf-8") as handle:
        handle.write(line + "\n")
    print("Appended scoreboard entry.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
