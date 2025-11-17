import base64
import html
import json
import logging
import os
import secrets
import sqlite3
import time
import uuid
from collections import defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from socketserver import ThreadingMixIn
from typing import Dict, Iterable, Optional, cast
from urllib import error as urlerror
from urllib import parse as urlparse
from urllib import request as urlrequest

from .database import ScoreRepository
from .models import ScoreEntryDict

EntryMapping = Dict[str, object]



HOST_ENV = "SCOREBOARD_HOST"
PORT_ENV = "SCOREBOARD_PORT"
FALLBACK_PLATFORM_PORT_ENV = "PORT"
SECRET_ENV = "SCOREBOARD_SECRET"
REPO_ENV = "SCOREBOARD_REPO"
TOKEN_ENV = "SCOREBOARD_GITHUB_TOKEN"
EVENT_ENV = "SCOREBOARD_EVENT"
API_ENV = "SCOREBOARD_API_BASE"
DB_ENV = "SCOREBOARD_DB_PATH"
DEFAULT_DB_PATH = "scoreboard.db"
LEADERBOARD_LIMIT_ENV = "SCOREBOARD_LEADERBOARD_LIMIT"
MAX_PAYLOAD_BYTES = 4096

logging.basicConfig(level=logging.INFO,
                    format="%(asctime)s %(levelname)s %(message)s")
LOGGER = logging.getLogger("scoreboard_webhook")


@dataclass(frozen=True)
class Config:
    repo: Optional[str]
    token: Optional[str]
    event_type: str
    api_base: str
    secret: Optional[str]

    def __str__(self) -> str:
        """Safe string representation that masks secrets."""
        return (
            f"Config(repo={self.repo}, "
            f"token={'***REDACTED***' if self.token else 'None'}, "
            f"event_type={self.event_type}, "
            f"api_base={self.api_base}, "
            f"secret={'***REDACTED***' if self.secret else 'None'})"
        )


def _normalize(value: Optional[str]) -> Optional[str]:
    if value is None:
        return None
    trimmed = value.strip().strip('"').strip("'").strip()
    return trimmed or None


def load_config() -> Config:
    repo = _normalize(os.environ.get(REPO_ENV))
    token = _normalize(os.environ.get(TOKEN_ENV))
    if not repo or not token:
        LOGGER.info(
            "GitHub dispatch disabled (missing SCOREBOARD_REPO or SCOREBOARD_GITHUB_TOKEN).")
        repo = None
        token = None
    event_type = _normalize(os.environ.get(EVENT_ENV)) or "scoreboard-entry"
    api_base = (_normalize(os.environ.get(API_ENV))
                or "https://api.github.com").rstrip("/")
    secret = _normalize(os.environ.get(SECRET_ENV))
    return Config(repo=repo, token=token, event_type=event_type, api_base=api_base, secret=secret)


CONFIG = load_config()

# Simple rate limiter for bot protection
class RateLimiter:
    def __init__(self, max_requests: int = 60, window_seconds: int = 60):
        self.max_requests = max_requests
        self.window_seconds = window_seconds
        self.requests: Dict[str, list[float]] = defaultdict(list)

    def is_allowed(self, client_ip: str) -> bool:
        now = time.time()
        cutoff = now - self.window_seconds

        # Clean old requests
        self.requests[client_ip] = [
            timestamp for timestamp in self.requests[client_ip]
            if timestamp > cutoff
        ]

        # Check if limit exceeded
        if len(self.requests[client_ip]) >= self.max_requests:
            return False

        # Add current request
        self.requests[client_ip].append(now)
        return True

    def cleanup_old_entries(self):
        """Periodically clean up old IP entries to prevent memory buildup"""
        now = time.time()
        cutoff = now - (self.window_seconds * 2)

        for ip in list(self.requests.keys()):
            self.requests[ip] = [
                timestamp for timestamp in self.requests[ip]
                if timestamp > cutoff
            ]
            if not self.requests[ip]:
                del self.requests[ip]

RATE_LIMITER = RateLimiter(max_requests=60, window_seconds=60)

def resolve_db_path() -> str:
    env_path = _normalize(os.environ.get(DB_ENV))
    if env_path:
        return env_path
    return DEFAULT_DB_PATH


def resolve_leaderboard_limit() -> int:
    raw = _normalize(os.environ.get(LEADERBOARD_LIMIT_ENV))
    if not raw:
        return 10
    try:
        return max(1, min(100, int(raw)))
    except (TypeError, ValueError):
        return 10


try:
    REPOSITORY = ScoreRepository(resolve_db_path())
except SystemExit:
    raise
except Exception as exc:  # noqa: BLE001
    LOGGER.error("Unable to initialize the scoreboard repository: %s", exc)
    raise SystemExit(1) from exc
LEADERBOARD_LIMIT_DEFAULT = resolve_leaderboard_limit()
TEMPLATE_PATH = Path(__file__).with_name("leaderboard.html")
try:
    HTML_TEMPLATE = TEMPLATE_PATH.read_text(encoding="utf-8")
except FileNotFoundError as exc:
    LOGGER.error("Missing leaderboard template at %s", TEMPLATE_PATH)
    raise SystemExit(1) from exc


def normalize_entry(data: Dict[str, object]) -> ScoreEntryDict:
    entry_id = str(data.get("id") or uuid.uuid4().hex)
    initials_raw = str(data.get("initials") or "???")
    initials = initials_raw.strip().upper() or "???"
    initials = initials[:3]
    level_value = data.get("level", data.get("score", 0))
    run_time_value = data.get("runTimeTicks", data.get("run_time_ticks", 0))
    # max_altitude_value = data.get("maxAltitude", data.get("max_altitude", 0.0))  # REMOVED - No longer needed
    victory_value = data.get("victory", False)
    timestamp = data.get("timestampUtc") or datetime.now(timezone.utc).isoformat()
    try:
        level = int(str(level_value))
    except (TypeError, ValueError):
        level = 0
    try:
        run_time_ticks = int(str(run_time_value))
    except (TypeError, ValueError):
        run_time_ticks = 0
    # try:
    #     max_altitude = float(str(max_altitude_value))
    # except (TypeError, ValueError):
    #     max_altitude = 0.0
    victory = _to_bool(victory_value)
    return {
        "id": entry_id,
        "initials": initials,
        "level": level,
        # "maxAltitude": max_altitude,  # REMOVED - No longer tracked
        "runTimeTicks": run_time_ticks,
        "victory": victory,
        "timestampUtc": str(timestamp),
    }


def _to_bool(value: object) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    if isinstance(value, str):
        return value.strip().lower() in {"1", "true", "yes", "y"}
    return False


class ScoreboardHandler(BaseHTTPRequestHandler):
    server_version = "ScoreboardWebhook/1.0"

    def _add_security_headers(self) -> None:
        """Add security headers to all responses."""
        # HSTS - Force HTTPS for 1 year (only applies if served over HTTPS)
        self.send_header("Strict-Transport-Security", "max-age=31536000; includeSubDomains")

        # Prevent MIME type sniffing
        self.send_header("X-Content-Type-Options", "nosniff")

        # Prevent clickjacking
        self.send_header("X-Frame-Options", "DENY")

        # XSS Protection (legacy but still good)
        self.send_header("X-XSS-Protection", "1; mode=block")

        # Referrer policy
        self.send_header("Referrer-Policy", "strict-origin-when-cross-origin")

        # Permissions policy - disable unnecessary features
        self.send_header("Permissions-Policy",
                        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), "
                        "magnetometer=(), microphone=(), payment=(), usb=()")

    def _check_rate_limit(self) -> bool:
        """Check if client is rate limited. Returns True if allowed."""
        client_ip = self.client_address[0]

        if not RATE_LIMITER.is_allowed(client_ip):
            LOGGER.warning("Rate limit exceeded for IP: %s", client_ip)
            self.send_error(
                HTTPStatus.TOO_MANY_REQUESTS,
                "Rate limit exceeded. Please try again later."
            )
            return False
        return True

    def do_GET(self):
        if not self._check_rate_limit():
            return

        parsed = urlparse.urlparse(self.path)
        if parsed.path == "/healthz":
            self.send_response(HTTPStatus.OK)
            self._add_security_headers()
            self.end_headers()
            self.wfile.write(b"ok")
            return
        if parsed.path == "/":
            limit, since = self._resolve_query_params(parsed.query)
            payload = REPOSITORY.leaderboard(limit, since)
            self._write_html(payload)
            return
        if parsed.path in ("/scoreboard", "/leaderboard"):
            limit, since = self._resolve_query_params(parsed.query)
            payload = REPOSITORY.leaderboard(limit, since)
            self._write_json(payload)
            return
        self.send_error(HTTPStatus.NOT_FOUND, "Unknown path.")

    def do_POST(self):
        if not self._check_rate_limit():
            return

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
        decoded_line: Optional[str] = None
        if not encoded:
            if not isinstance(line, str):
                self.send_error(HTTPStatus.BAD_REQUEST,
                                "line or line_b64 required.")
                return
            decoded_line = line
            encoded = base64.b64encode(line.encode("utf-8")).decode("ascii")
        if decoded_line is None:
            try:
                decoded_line = base64.b64decode(encoded).decode("utf-8")
            except (ValueError, UnicodeDecodeError) as exc:
                self.send_error(HTTPStatus.BAD_REQUEST,
                                f"Invalid base64 payload: {exc}")
                return

        try:
            parsed_entry = json.loads(decoded_line)
        except json.JSONDecodeError as exc:
            self.send_error(HTTPStatus.BAD_REQUEST,
                            f"Payload is not valid JSON: {exc.msg}")
            return

        normalized_entry = normalize_entry(parsed_entry)
        try:
            REPOSITORY.upsert_entry(normalized_entry)
        except sqlite3.Error as exc:
            LOGGER.error("Failed to persist scoreboard entry: %s", exc)
            self.send_error(HTTPStatus.INTERNAL_SERVER_ERROR,
                            "Failed to store entry.")
            return

        dispatch_warning = None
        try:
            dispatch(encoded)
        except RuntimeError as exc:
            dispatch_warning = str(exc)
            LOGGER.warning("Dispatch failed (entry stored in DB): %s", exc)

        self.send_response(HTTPStatus.ACCEPTED)
        if dispatch_warning:
            self.send_header("X-Dispatch-Status", "failed")
        self._add_security_headers()
        self.end_headers()
        self.wfile.write(b"queued")

    def log_message(self, format: str, *args: object):
        LOGGER.info("%s - - %s", self.address_string(), format % args)

    def _resolve_query_params(self, query: str) -> tuple[int, Optional[str]]:
        """Parse limit and since parameters from query string.

        Supported parameters:
        - limit: number of entries to return (1-100)
        - since: ISO timestamp or relative time (today, week, month)
        """
        if not query:
            return LEADERBOARD_LIMIT_DEFAULT, None

        params = urlparse.parse_qs(query)

        # Parse limit
        limit = LEADERBOARD_LIMIT_DEFAULT
        limit_value = params.get("limit")
        if limit_value:
            try:
                limit = max(1, min(100, int(limit_value[0])))
            except (TypeError, ValueError):
                pass

        # Parse since parameter
        since = None
        since_value = params.get("since")
        if since_value:
            since_str = since_value[0].lower()
            now = datetime.now(timezone.utc)

            # Handle relative time ranges
            if since_str == "today":
                since = now.replace(hour=0, minute=0, second=0, microsecond=0).isoformat()
            elif since_str == "week":
                from datetime import timedelta
                since = (now - timedelta(days=7)).isoformat()
            elif since_str == "month":
                from datetime import timedelta
                since = (now - timedelta(days=30)).isoformat()
            else:
                # Assume it's an ISO timestamp
                since = since_str

        return limit, since

    def _write_json(self, payload: dict[str, object]) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(HTTPStatus.OK)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self._add_security_headers()
        self.end_headers()
        self.wfile.write(body)

    def _write_html(self, payload: Dict[str, object]) -> None:
        top_entries = cast(Iterable[EntryMapping], payload.get("topLevels", []))
        fast_entries = cast(Iterable[EntryMapping], payload.get("fastestRuns", []))
        top_html = self._render_table(top_entries, show_levels=True, tbody_id="top-levels-body")
        fast_html = self._render_table(fast_entries, show_levels=False, tbody_id="fastest-runs-body")
        document = (
            HTML_TEMPLATE
            .replace("{{TOP_LEVELS}}", top_html, 1)
            .replace("{{FASTEST_RUNS}}", fast_html, 1)
        )
        body = document.encode("utf-8")
        self.send_response(HTTPStatus.OK)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Cache-Control", "no-store")

        # Add CSP for HTML pages - allow necessary external resources
        self.send_header("Content-Security-Policy",
                        "default-src 'none'; "
                        "script-src 'unsafe-inline'; "
                        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "
                        "img-src 'self' data: https://api.qrserver.com; "
                        "font-src 'self' https://fonts.gstatic.com; "
                        "connect-src 'self'")

        self._add_security_headers()
        self.end_headers()
        self.wfile.write(body)

    @staticmethod
    def _render_table(
        entries: Iterable[EntryMapping], show_levels: bool, tbody_id: str = ""
    ) -> str:
        if not entries:
            return "<p>No runs recorded yet.</p>"
        header_main = "<th>Levels</th>" if show_levels else "<th>Run Time</th>"
        header_aux = "<th>Run Time</th>" if show_levels else "<th>Levels</th>"
        tbody_tag = f'<tbody id="{tbody_id}">' if tbody_id else "<tbody>"
        rows = [
            "<table>",
            "<thead><tr><th>#</th><th>Initials</th>",
            header_main,
            header_aux,
            "</tr></thead>",
            tbody_tag
        ]
        for idx, entry in enumerate(entries, start=1):
            initials_raw = str(entry.get("initials", "???"))
            initials = html.escape(initials_raw)
            levels = int(str(entry.get("level", 0)))
            run_time_ticks = entry.get("runTimeTicks", 0)
            run_time = ScoreboardHandler._format_duration(run_time_ticks)
            if show_levels:
                rows.append(
                    f"<tr><td>{idx}</td><td>{initials}</td><td>{levels}</td><td>{run_time}</td></tr>")
            else:
                rows.append(
                    f"<tr><td>{idx}</td><td>{initials}</td><td>{run_time}</td><td>{levels}</td></tr>")
        rows.append("</tbody></table>")
        return "".join(rows)

    @staticmethod
    def _format_duration(ticks: object) -> str:
        try:
            total_seconds = float(str(ticks)) / 10_000_000
        except (TypeError, ValueError):
            return "00:00.000"
        minutes = int(total_seconds // 60)
        seconds = total_seconds % 60
        return f"{minutes:02d}:{seconds:06.3f}"

    def _authorize(self) -> bool:
        if CONFIG.secret is None:
            LOGGER.critical(
                "⚠️  AUTHENTICATION DISABLED! SCOREBOARD_SECRET not set. "
                "Anyone can POST scores to this endpoint. "
                "This is INSECURE for production use!"
            )
            return True

        provided = self.headers.get("X-Scoreboard-Secret", "")
        if not provided:
            LOGGER.warning(
                "Authentication attempt with missing secret from IP: %s",
                self.client_address[0]
            )
            self.send_error(HTTPStatus.UNAUTHORIZED,
                            "Missing X-Scoreboard-Secret header.")
            return False

        if not secrets.compare_digest(provided.strip(), CONFIG.secret):
            LOGGER.warning(
                "Failed authentication attempt from IP: %s, User-Agent: %s",
                self.client_address[0],
                self.headers.get("User-Agent", "unknown")
            )
            self.send_error(HTTPStatus.UNAUTHORIZED,
                            "Invalid X-Scoreboard-Secret header.")
            return False
        return True

    def _read_json_body(self):
        length_header = self.headers.get("Content-Length")
        if length_header is None:
            self.send_error(HTTPStatus.LENGTH_REQUIRED,
                            "Missing Content-Length header.")
            return None
        try:
            length = int(length_header)
        except (TypeError, ValueError):
            self.send_error(HTTPStatus.LENGTH_REQUIRED,
                            "Invalid Content-Length header.")
            return None

        if length <= 0 or length > MAX_PAYLOAD_BYTES:
            self.send_error(HTTPStatus.REQUEST_ENTITY_TOO_LARGE,
                            "Payload too large.")
            return None

        raw = self.rfile.read(length)
        try:
            return json.loads(raw)
        except json.JSONDecodeError:
            self.send_error(HTTPStatus.BAD_REQUEST,
                            "Request body must be valid JSON.")
            return None


def dispatch(encoded_line: str) -> None:
    if not CONFIG.repo or not CONFIG.token:
        return
    url = f"{CONFIG.api_base}/repos/{CONFIG.repo.strip('/')}/dispatches"
    parsed_url = urlparse.urlparse(url)
    if parsed_url.scheme not in ("http", "https"):
        raise RuntimeError(f"Unsupported URL scheme for GitHub dispatch: {parsed_url.scheme}")
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
        req.add_header("Content-Type", "application/json")
        req.add_header("User-Agent", "stackoverflow-minigame-webhook/1.0")

        try:
            with urlrequest.urlopen(req, timeout=15) as resp:
                resp.read()
            return
        except urlerror.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            LOGGER.error("GitHub dispatch failed (attempt %s/5): %s %s %s",
                         attempt + 1, exc.code, exc.reason, detail)
            if 400 <= exc.code < 500 and exc.code != 429:
                raise RuntimeError(
                    f"GitHub dispatch failed: {exc.code} {exc.reason} - {detail}") from exc
        except urlerror.URLError as exc:
            LOGGER.error(
                "Network error while calling GitHub (attempt %s/5): %s", attempt + 1, exc)

        time.sleep(backoff)
        backoff = min(backoff * 2, 10)

    raise RuntimeError(
        "GitHub dispatch failed after multiple attempts. Check webhook logs for details.")


class ThreadingHTTPServer(ThreadingMixIn, HTTPServer):
    daemon_threads = True


def build_server():
    # Default to localhost for security - set SCOREBOARD_HOST=0.0.0.0 for network access
    host = os.environ.get(HOST_ENV, "127.0.0.1")
    port_value = os.environ.get(PORT_ENV) or os.environ.get(
        FALLBACK_PLATFORM_PORT_ENV) or "8080"
    port = int(port_value)
    httpd = ThreadingHTTPServer((host, port), ScoreboardHandler)
    LOGGER.info("Listening on http://%s:%s/scoreboard", host, port)
    if host == "127.0.0.1":
        LOGGER.info("🔒 Bound to localhost only - set SCOREBOARD_HOST=0.0.0.0 for network access")
    return httpd


def main():
    LOGGER.info("Loaded configuration: repo=%s event=%s api_base=%s secret=%s",
                CONFIG.repo, CONFIG.event_type, CONFIG.api_base, "set" if CONFIG.secret else "unset")
    LOGGER.info("Using SQLite database at %s", REPOSITORY.path)
    LOGGER.info("Rate limiting enabled: %d requests per %d seconds per IP",
                RATE_LIMITER.max_requests, RATE_LIMITER.window_seconds)

    # Security warning if authentication is disabled
    if CONFIG.secret:
        LOGGER.info("✅ Authentication enabled - SCOREBOARD_SECRET is set")
    else:
        LOGGER.critical("=" * 80)
        LOGGER.critical("⚠️  SECURITY WARNING: AUTHENTICATION DISABLED!")
        LOGGER.critical("⚠️  SCOREBOARD_SECRET environment variable is NOT set!")
        LOGGER.critical("⚠️  Anyone can POST scores to this server without authentication!")
        LOGGER.critical("⚠️  This is INSECURE for production deployments!")
        LOGGER.critical("⚠️  Set SCOREBOARD_SECRET environment variable to enable authentication.")
        LOGGER.critical("=" * 80)

    server = build_server()

    # Periodically clean up rate limiter
    last_cleanup = time.time()
    cleanup_interval = 300  # 5 minutes

    try:
        LOGGER.info("Server started - press Ctrl+C to stop")
        while True:
            server.handle_request()

            # Cleanup rate limiter periodically
            now = time.time()
            if now - last_cleanup > cleanup_interval:
                RATE_LIMITER.cleanup_old_entries()
                last_cleanup = now
    except KeyboardInterrupt:
        LOGGER.info("Shutting down…")
        server.server_close()


if __name__ == "__main__":
    main()
