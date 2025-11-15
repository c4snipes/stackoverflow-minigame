#!/usr/bin/env python3
"""
A tiny HTTP endpoint (put it behind TLS with Fly/nginx/etc.) that receives scoreboard payloads from the game and
triggers the GitHub repository_dispatch workflow on behalf of players.

Configuration is done through environment variables:
  SCOREBOARD_HOST: Host interface to bind (default 0.0.0.0)
  SCOREBOARD_PORT: Port to listen on (default 8080 if unset)
  SCOREBOARD_SECRET: Shared secret expected in X-Scoreboard-Secret header
  SCOREBOARD_REPO: GitHub repo in the form owner/name
  SCOREBOARD_GITHUB_TOKEN: PAT with repo contents:write scope
  SCOREBOARD_EVENT: repository_dispatch event type (default scoreboard-entry)
  SCOREBOARD_API_BASE: GitHub API base URL (default https://api.github.com)
"""

import base64
import json
import logging
import os
import secrets
import time
from dataclasses import dataclass
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, HTTPServer
from typing import Optional
from urllib import error as urlerror
from urllib import request as urlrequest


HOST_ENV = "SCOREBOARD_HOST"
PORT_ENV = "SCOREBOARD_PORT"
FALLBACK_PLATFORM_PORT_ENV = "PORT"
SECRET_ENV = "SCOREBOARD_SECRET"
REPO_ENV = "SCOREBOARD_REPO"
TOKEN_ENV = "SCOREBOARD_GITHUB_TOKEN"
EVENT_ENV = "SCOREBOARD_EVENT"
API_ENV = "SCOREBOARD_API_BASE"
MAX_PAYLOAD_BYTES = 4096

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
LOGGER = logging.getLogger("scoreboard_webhook")


@dataclass(frozen=True)
class Config:
    repo: str
    token: str
    event_type: str
    api_base: str
    secret: Optional[str]


def _normalize(value: Optional[str]) -> Optional[str]:
    if value is None:
        return None
    trimmed = value.strip().strip('"').strip("'").strip()
    return trimmed or None


def load_config() -> Config:
    repo = _normalize(os.environ.get(REPO_ENV))
    token = _normalize(os.environ.get(TOKEN_ENV))
    if not repo or not token:
        raise SystemExit(
            "SCOREBOARD_REPO and SCOREBOARD_GITHUB_TOKEN must be set before starting the webhook."
        )
    event_type = _normalize(os.environ.get(EVENT_ENV)) or "scoreboard-entry"
    api_base = (_normalize(os.environ.get(API_ENV)) or "https://api.github.com").rstrip("/")
    secret = _normalize(os.environ.get(SECRET_ENV))
    return Config(repo=repo, token=token, event_type=event_type, api_base=api_base, secret=secret)


CONFIG = load_config()


class ScoreboardHandler(BaseHTTPRequestHandler):
    server_version = "ScoreboardWebhook/1.0"

    def do_GET(self):
        if self.path == "/healthz":
            self.send_response(HTTPStatus.OK)
            self.end_headers()
            self.wfile.write(b"ok")
            return
        if self.path == "/scoreboard":
            message = (
                "This endpoint accepts POSTs from the game to sync the leaderboard. "
                "Run 'dotnet run' and tap 'L' (or ./launch-leaderboard.sh) to view results locally, "
                "or watch GitHub commits for updates."
            )
            self.send_response(HTTPStatus.OK)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.end_headers()
            self.wfile.write(message.encode("utf-8"))
            return
        self.send_error(HTTPStatus.NOT_FOUND, "Unknown path.")

    def do_POST(self):
        if self.path != "/scoreboard":
            self.send_error(HTTPStatus.NOT_FOUND, "Unknown path.")
            return

        if not self._authorize():
            return

        payload = self._read_json_body()
        if payload is None:
            return

        line = payload.get("line")
        encoded = payload.get("line_b64")
        if not encoded:
            if not isinstance(line, str):
                self.send_error(HTTPStatus.BAD_REQUEST, "line or line_b64 required.")
                return
            encoded = base64.b64encode(line.encode("utf-8")).decode("ascii")

        try:
            dispatch(encoded)
        except RuntimeError as exc:
            LOGGER.warning("Dispatch failed: %s", exc)
            self.send_error(HTTPStatus.INTERNAL_SERVER_ERROR, str(exc))
            return

        self.send_response(HTTPStatus.ACCEPTED)
        self.end_headers()
        self.wfile.write(b"queued")

    def log_message(self, format: str, *args: object):
        LOGGER.info("%s - - %s", self.address_string(), format % args)

    def _authorize(self) -> bool:
        if CONFIG.secret is None:
            return True
        provided = self.headers.get("X-Scoreboard-Secret", "")
        if not secrets.compare_digest(provided.strip(), CONFIG.secret):
            self.send_error(HTTPStatus.UNAUTHORIZED, "Invalid X-Scoreboard-Secret header.")
            return False
        return True

    def _read_json_body(self):
        length_header = self.headers.get("Content-Length")
        if length_header is None:
            self.send_error(HTTPStatus.LENGTH_REQUIRED, "Missing Content-Length header.")
            return None
        try:
            length = int(length_header)
        except (TypeError, ValueError):
            self.send_error(HTTPStatus.LENGTH_REQUIRED, "Invalid Content-Length header.")
            return None

        if length <= 0 or length > MAX_PAYLOAD_BYTES:
            self.send_error(HTTPStatus.REQUEST_ENTITY_TOO_LARGE, "Payload too large.")
            return None

        raw = self.rfile.read(length)
        try:
            return json.loads(raw)
        except json.JSONDecodeError:
            self.send_error(HTTPStatus.BAD_REQUEST, "Request body must be valid JSON.")
            return None


def dispatch(encoded_line: str) -> None:
    url = f"{CONFIG.api_base}/repos/{CONFIG.repo.strip('/')}/dispatches"
    payload: dict[str, str | dict[str, str]] = {
        "event_type": CONFIG.event_type,
        "client_payload": {"line_b64": encoded_line},
    }
    body = json.dumps(payload).encode("utf-8")

    backoff = 1.0
    for attempt in range(5):
        req = urlrequest.Request(url, data=body, method="POST")
        req.add_header("Accept", "application/vnd.github+json")
        req.add_header("Authorization", f"token {CONFIG.token}")
        req.add_header("Content-Type", "application/jsonl")
        req.add_header("User-Agent", "stackoverflow-minigame-webhook/1.0")

        try:
            with urlrequest.urlopen(req, timeout=15) as resp:
                resp.read()
            return
        except urlerror.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            LOGGER.error("GitHub dispatch failed (attempt %s/5): %s %s %s", attempt + 1, exc.code, exc.reason, detail)
            if 400 <= exc.code < 500 and exc.code != 429:
                raise RuntimeError(f"GitHub dispatch failed: {exc.code} {exc.reason} - {detail}") from exc
        except urlerror.URLError as exc:
            LOGGER.error("Network error while calling GitHub (attempt %s/5): %s", attempt + 1, exc)

        time.sleep(backoff)
        backoff = min(backoff * 2, 10)

    raise RuntimeError("GitHub dispatch failed after multiple attempts. Check webhook logs for details.")


def build_server():
    host = os.environ.get(HOST_ENV, "0.0.0.0")
    port_value = os.environ.get(PORT_ENV) or os.environ.get(FALLBACK_PLATFORM_PORT_ENV) or "8080"
    port = int(port_value)
    httpd = HTTPServer((host, port), ScoreboardHandler)
    LOGGER.info("Listening on http://%s:%s/scoreboard", host, port)
    return httpd


def main():
    LOGGER.info("Loaded configuration: repo=%s event=%s api_base=%s secret=%s",
                CONFIG.repo, CONFIG.event_type, CONFIG.api_base, "set" if CONFIG.secret else "unset")
    server = build_server()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        LOGGER.info("Shutting down…")
        server.server_close()


if __name__ == "__main__":
    main()
