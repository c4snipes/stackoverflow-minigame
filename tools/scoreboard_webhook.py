#!/usr/bin/env python3
"""
A tiny HTTPS-capable endpoint that receives scoreboard payloads from the game and
triggers the GitHub repository_dispatch workflow on behalf of players.

Configuration is done through environment variables:
  SCOREBOARD_HOST: Host interface to bind (default 0.0.0.0)
  SCOREBOARD_PORT: Port to listen on (default 8443)
  SCOREBOARD_TLS_CERT: Path to PEM certificate (optional)
  SCOREBOARD_TLS_KEY: Path to PEM private key (required if CERT provided)
  SCOREBOARD_SECRET: Shared secret expected in X-Scoreboard-Secret header
  SCOREBOARD_REPO: GitHub repo in the form owner/name
  SCOREBOARD_GITHUB_TOKEN: PAT with repo contents:write scope
  SCOREBOARD_EVENT: repository_dispatch event type (default scoreboard-entry)
  SCOREBOARD_API_BASE: GitHub API base URL (default https://api.github.com)
"""

import base64
import json
import os
import secrets
import ssl
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib import error as urlerror
from urllib import request as urlrequest


HOST_ENV = "SCOREBOARD_HOST"
PORT_ENV = "SCOREBOARD_PORT"
FALLBACK_PLATFORM_PORT_ENV = "PORT"
CERT_ENV = "SCOREBOARD_TLS_CERT"
KEY_ENV = "SCOREBOARD_TLS_KEY"
SECRET_ENV = "SCOREBOARD_SECRET"
REPO_ENV = "SCOREBOARD_REPO"
TOKEN_ENV = "SCOREBOARD_GITHUB_TOKEN"
EVENT_ENV = "SCOREBOARD_EVENT"
API_ENV = "SCOREBOARD_API_BASE"


class ScoreboardHandler(BaseHTTPRequestHandler):
    server_version = "ScoreboardWebhook/1.0"

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
            self.send_error(HTTPStatus.INTERNAL_SERVER_ERROR, str(exc))
            return

        self.send_response(HTTPStatus.ACCEPTED)
        self.end_headers()
        self.wfile.write(b"queued")

    def log_message(self, format: str, *args: object):
        # Basic stdout logging
        print("%s - - [%s] %s" % (self.address_string(), self.log_date_time_string(), format % args))

    def _authorize(self) -> bool:
        secret = os.environ.get(SECRET_ENV)
        if not secret:
            return True
        provided = self.headers.get("X-Scoreboard-Secret", "")
        if not secrets.compare_digest(provided.strip(), secret.strip()):
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

        raw = self.rfile.read(length)
        try:
            return json.loads(raw)
        except json.JSONDecodeError:
            self.send_error(HTTPStatus.BAD_REQUEST, "Request body must be valid JSON.")
            return None


def dispatch(encoded_line: str) -> None:
    repo = os.environ.get(REPO_ENV)
    token = os.environ.get(TOKEN_ENV)
    if not repo or not token:
        raise RuntimeError("Server missing SCOREBOARD_REPO or SCOREBOARD_GITHUB_TOKEN env vars.")

    api_base = (os.environ.get(API_ENV) or "https://api.github.com").rstrip("/")
    url = f"{api_base}/repos/{repo.strip('/')}/dispatches"
    payload: dict[str, str | dict[str, str]] = {
        "event_type": os.environ.get(EVENT_ENV, "scoreboard-entry"),
        "client_payload": {"line_b64": encoded_line},
    }
    body = json.dumps(payload).encode("utf-8")
    req = urlrequest.Request(url, data=body, method="POST")
    req.add_header("Accept", "application/vnd.github+json")
    req.add_header("Authorization", f"token {token.strip()}")
    req.add_header("Content-Type", "application/json")
    req.add_header("User-Agent", "stackoverflow-minigame-webhook/1.0")

    try:
        with urlrequest.urlopen(req, timeout=15) as resp:
            resp.read()
    except urlerror.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"GitHub dispatch failed: {exc.code} {exc.reason} - {detail}") from exc
    except urlerror.URLError as exc:
        raise RuntimeError(f"Network error while calling GitHub: {exc}") from exc


def build_server():
    host = os.environ.get(HOST_ENV, "0.0.0.0")
    port = int(os.environ.get(PORT_ENV) or os.environ.get(FALLBACK_PLATFORM_PORT_ENV, "8443"))
    httpd = HTTPServer((host, port), ScoreboardHandler)
    cert_path = os.environ.get(CERT_ENV)
    key_path = os.environ.get(KEY_ENV)
    if cert_path and key_path:
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(certfile=cert_path, keyfile=key_path)
        httpd.socket = context.wrap_socket(httpd.socket, server_side=True)
        scheme = "https"
    else:
        scheme = "http"
        print("WARNING: TLS cert/key not provided; running without HTTPS.")
    print(f"Listening on {scheme}://{host}:{port}/scoreboard")
    return httpd


def main():
    server = build_server()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("Shutting down…")
        server.server_close()


if __name__ == "__main__":
    main()
