#!/usr/bin/env python3
"""Decode payload env vars and forward them to the Fly webhook."""

from __future__ import annotations

import base64
import json
import os
import sys
import time
from typing import Optional
from urllib import error as urlerror
from urllib import parse as urlparse
from urllib import request as urlrequest

WEBHOOK_URL_ENV = "STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL"
WEBHOOK_SECRET_ENV = "STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET"
PLAIN_ENV = "PAYLOAD_LINE"


def main() -> int:
    line = _resolve_line()
    if line is None:
        return 1

    webhook_url = os.environ.get(WEBHOOK_URL_ENV)
    if not webhook_url:
        print(f"{WEBHOOK_URL_ENV} is not set; cannot forward entry.")
        return 1
    parsed_url = urlparse.urlparse(webhook_url)
    if parsed_url.scheme not in {"http", "https"}:
        print(
            f"Invalid webhook URL scheme: {parsed_url.scheme!r}; only http/https allowed."
        )
        return 1

    body = json.dumps({"line": line}).encode("utf-8")
    req = urlrequest.Request(webhook_url, data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Content-Type", "application/json")
    secret = os.environ.get(WEBHOOK_SECRET_ENV)
    if secret:
        req.add_header("X-Scoreboard-Secret", secret.strip())

    if _post_with_retries(req):
        print("Forwarded entry to the Fly scoreboard.")
        return 0
    return 1


def _resolve_line() -> Optional[str]:
    """Return the decoded JSON line from env vars or None on failure."""
    raw_line = os.environ.get(PLAIN_ENV)
    if raw_line:
        line = raw_line.strip()
    else:
        payload = os.environ.get("PAYLOAD")
        if not payload:
            print("Neither PAYLOAD nor PAYLOAD_LINE was provided.")
            return None
        try:
            line = base64.b64decode(payload).decode("utf-8").strip()
        except Exception as exc:  # noqa: BLE001
            print(f"Failed to decode payload: {exc}")
            return None

    if not line:
        print("Decoded payload is empty; nothing to append.")
        return None

    try:
        json.loads(line)
    except json.JSONDecodeError as exc:
        print(f"Payload is not valid JSON: {exc}")
        return None
    return line


def _post_with_retries(req: urlrequest.Request, attempts: int = 3) -> bool:
    backoff = 1.0
    for attempt in range(1, attempts + 1):
        try:
            with urlrequest.urlopen(req, timeout=20) as resp:
                resp_body = resp.read().decode("utf-8", errors="replace")
                print(f"Webhook responded with {resp.status}: {resp_body}")
                return True
        except urlerror.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            print(
                f"[attempt {attempt}] Webhook returned {exc.code} {exc.reason}: {detail}"
            )
            if 400 <= exc.code < 500:
                return False
        except urlerror.URLError as exc:
            print(f"[attempt {attempt}] Failed to reach webhook: {exc}")
        time.sleep(backoff)
        backoff = min(backoff * 2, 8)
    print("Exhausted retries while contacting the webhook.")
    return False


if __name__ == "__main__":
    sys.exit(main())
