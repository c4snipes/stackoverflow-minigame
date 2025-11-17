"""
Database repository for scoreboard entries.
Handles SQLite operations, queries, and statistics.
"""

import sqlite3
from datetime import datetime
from pathlib import Path
from types import TracebackType
from typing import Dict, Optional, Type
from .models import ScoreEntryDict


class ScoreRepository:
    """
    Repository for managing scoreboard entries in SQLite database.

    Supports context manager protocol for automatic cleanup:
        with ScoreRepository('scoreboard.db') as repo:
            repo.upsert_entry(entry)
    """

    def __init__(self, db_path: str):
        """
        Initialize the score repository.

        Args:
            db_path: Path to the SQLite database file
        """
        resolved = Path(db_path).expanduser()
        if resolved.parent and not resolved.parent.exists():
            resolved.parent.mkdir(parents=True, exist_ok=True)
        self.path = str(resolved)
        self._conn: Optional[sqlite3.Connection] = None

        try:
            self._conn = sqlite3.connect(self.path, check_same_thread=False)
        except sqlite3.Error as exc:
            raise SystemExit(
                f"Failed to open scoreboard database at {self.path}: {exc}"
            ) from exc
        self._conn.row_factory = sqlite3.Row
        self._initialize()

    def close(self) -> None:
        """Close the database connection if it's open."""
        if self._conn is not None:
            try:
                self._conn.close()
            except sqlite3.Error:
                pass  # Ignore errors during cleanup
            finally:
                self._conn = None

    def __enter__(self) -> "ScoreRepository":
        """Context manager entry - returns self."""
        return self

    def __exit__(
        self,
        exc_type: Optional[Type[BaseException]],
        exc_val: Optional[BaseException],
        exc_tb: Optional[TracebackType]
    ) -> bool:
        """Context manager exit - closes connection."""
        self.close()
        return False  # Don't suppress exceptions

    def __del__(self):
        """Cleanup on garbage collection."""
        self.close()

    def _initialize(self) -> None:
        """Create the scoreboard table if it doesn't exist."""
        if self._conn is None:
            raise RuntimeError("Database connection not initialized")
        with self._conn:
            self._conn.execute(
                """
                CREATE TABLE IF NOT EXISTS scoreboard (
                    id TEXT PRIMARY KEY,
                    initials TEXT NOT NULL,
                    level INTEGER NOT NULL,
                    max_altitude REAL NOT NULL,
                    run_time_ticks INTEGER NOT NULL,
                    victory INTEGER NOT NULL,
                    timestamp_utc TEXT NOT NULL
                )
                """
            )

    def upsert_entry(self, entry: ScoreEntryDict) -> None:
        """
        Insert or update a scoreboard entry.

        Args:
            entry: Score entry dictionary to upsert
        """
        if self._conn is None:
            raise RuntimeError("Database connection not initialized")
        with self._conn:
            self._conn.execute(
                """
                INSERT INTO scoreboard (id, initials, level, max_altitude, run_time_ticks, victory, timestamp_utc)
                VALUES (:id, :initials, :level, :max_altitude, :run_time_ticks, :victory, :timestamp_utc)
                ON CONFLICT(id) DO UPDATE SET
                    initials=excluded.initials,
                    level=excluded.level,
                    max_altitude=excluded.max_altitude,
                    run_time_ticks=excluded.run_time_ticks,
                    victory=excluded.victory,
                    timestamp_utc=excluded.timestamp_utc
                """,
                {
                    "id": entry["id"],
                    "initials": entry["initials"],
                    "level": entry["level"],
                    "max_altitude": 0.0,  # No longer used, set to 0
                    "run_time_ticks": entry["runTimeTicks"],
                    "victory": int(entry["victory"]),
                    "timestamp_utc": entry["timestampUtc"],
                },
            )

    def _validate_timestamp(self, ts: Optional[str]) -> Optional[str]:
        """
        Validate that a timestamp string is a valid ISO format.

        Args:
            ts: Timestamp string to validate

        Returns:
            Validated timestamp string or None if invalid
        """
        if not ts:
            return None
        try:
            # Validate it's a valid ISO timestamp
            datetime.fromisoformat(ts)
            return ts
        except (ValueError, TypeError):
            # Invalid format, ignore the filter
            return None

    def leaderboard(
        self, limit: int, since: Optional[str] = None
    ) -> Dict[str, object]:
        """
        Get leaderboard data with top levels, fastest runs, and stats.

        Args:
            limit: Maximum number of entries to return (1-100)
            since: Optional ISO timestamp to filter by time range

        Returns:
            Dictionary containing count, topLevels, fastestRuns, and stats
        """
        limit = max(1, min(limit, 100))

        # Validate timestamp to prevent SQL injection
        since_validated = self._validate_timestamp(since)

        if self._conn is None:
            raise RuntimeError("Database connection not initialized")

        with self._conn:
            # Top levels query - use conditional execution instead of f-strings
            if since_validated:
                top_query = """
                    SELECT * FROM scoreboard
                    WHERE timestamp_utc >= ?
                    ORDER BY level DESC, run_time_ticks ASC
                    LIMIT ?
                """
                top_rows = self._conn.execute(top_query, (since_validated, limit)).fetchall()
            else:
                top_query = """
                    SELECT * FROM scoreboard
                    ORDER BY level DESC, run_time_ticks ASC
                    LIMIT ?
                """
                top_rows = self._conn.execute(top_query, (limit,)).fetchall()

            # Fastest runs query
            if since_validated:
                fast_query = """
                    SELECT * FROM scoreboard
                    WHERE timestamp_utc >= ? AND run_time_ticks > 0 AND level > 0
                    ORDER BY run_time_ticks ASC, level DESC
                    LIMIT ?
                """
                fast_rows = self._conn.execute(fast_query, (since_validated, limit)).fetchall()
            else:
                fast_query = """
                    SELECT * FROM scoreboard
                    WHERE run_time_ticks > 0 AND level > 0
                    ORDER BY run_time_ticks ASC, level DESC
                    LIMIT ?
                """
                fast_rows = self._conn.execute(fast_query, (limit,)).fetchall()

            # Count query
            if since_validated:
                count_query = "SELECT COUNT(*) AS count FROM scoreboard WHERE timestamp_utc >= ?"
                count_row = self._conn.execute(count_query, (since_validated,)).fetchone()
            else:
                count_query = "SELECT COUNT(*) AS count FROM scoreboard"
                count_row = self._conn.execute(count_query).fetchone()

        return {
            "count": count_row["count"] if count_row else 0,
            "topLevels": [self._row_to_entry(row) for row in top_rows],
            "fastestRuns": [self._row_to_entry(row) for row in fast_rows],
            "stats": self.get_global_stats(since_validated),
        }

    def get_global_stats(self, since: Optional[str] = None) -> Dict[str, object]:
        """
        Get global statistics for all scoreboard entries.

        Args:
            since: Optional ISO timestamp to filter by time range

        Returns:
            Dictionary containing global statistics
        """
        # Validate timestamp
        since_validated = self._validate_timestamp(since)

        if self._conn is None:
            raise RuntimeError("Database connection not initialized")

        with self._conn:
            # Get basic stats - use conditional execution
            if since_validated:
                stats_query = """
                    SELECT
                        COUNT(DISTINCT initials) as total_players,
                        COUNT(*) as total_runs,
                        CAST(AVG(level) AS INTEGER) as average_level,
                        MAX(level) as highest_level,
                        MIN(CASE WHEN run_time_ticks > 0 THEN run_time_ticks END) as fastest_time_ticks
                    FROM scoreboard
                    WHERE timestamp_utc >= ?
                """
                stats_row = self._conn.execute(stats_query, (since_validated,)).fetchone()
            else:
                stats_query = """
                    SELECT
                        COUNT(DISTINCT initials) as total_players,
                        COUNT(*) as total_runs,
                        CAST(AVG(level) AS INTEGER) as average_level,
                        MAX(level) as highest_level,
                        MIN(CASE WHEN run_time_ticks > 0 THEN run_time_ticks END) as fastest_time_ticks
                    FROM scoreboard
                """
                stats_row = self._conn.execute(stats_query).fetchone()

            if not stats_row or stats_row["total_runs"] == 0:
                return {
                    "totalPlayers": 0,
                    "totalRuns": 0,
                    "averageLevel": 0,
                    "highestLevel": 0,
                    "fastestTimeTicks": 0,
                    "topPlayer": "N/A",
                    "fastestPlayer": "N/A",
                }

            # Get top player
            if since_validated:
                top_player_query = """
                    SELECT initials, MAX(level) as max_level
                    FROM scoreboard
                    WHERE timestamp_utc >= ?
                    GROUP BY initials
                    ORDER BY max_level DESC
                    LIMIT 1
                """
                top_player_row = self._conn.execute(top_player_query, (since_validated,)).fetchone()
            else:
                top_player_query = """
                    SELECT initials, MAX(level) as max_level
                    FROM scoreboard
                    GROUP BY initials
                    ORDER BY max_level DESC
                    LIMIT 1
                """
                top_player_row = self._conn.execute(top_player_query).fetchone()

            # Get fastest player
            if since_validated:
                fastest_player_query = """
                    SELECT initials
                    FROM scoreboard
                    WHERE timestamp_utc >= ? AND run_time_ticks > 0
                    ORDER BY run_time_ticks ASC
                    LIMIT 1
                """
                fastest_player_row = self._conn.execute(fastest_player_query, (since_validated,)).fetchone()
            else:
                fastest_player_query = """
                    SELECT initials
                    FROM scoreboard
                    WHERE run_time_ticks > 0
                    ORDER BY run_time_ticks ASC
                    LIMIT 1
                """
                fastest_player_row = self._conn.execute(fastest_player_query).fetchone()

        return {
            "totalPlayers": stats_row["total_players"] or 0,
            "totalRuns": stats_row["total_runs"] or 0,
            "averageLevel": stats_row["average_level"] or 0,
            "highestLevel": stats_row["highest_level"] or 0,
            "fastestTimeTicks": stats_row["fastest_time_ticks"] or 0,
            "topPlayer": top_player_row["initials"] if top_player_row else "N/A",
            "fastestPlayer": (
                fastest_player_row["initials"] if fastest_player_row else "N/A"
            ),
        }

    @staticmethod
    def _row_to_entry(row: sqlite3.Row) -> ScoreEntryDict:
        """
        Convert a database row to a ScoreEntryDict.

        Args:
            row: SQLite row object

        Returns:
            ScoreEntryDict with entry data
        """
        return {
            "id": row["id"],
            "initials": row["initials"],
            "level": row["level"],
            "runTimeTicks": row["run_time_ticks"],
            "victory": bool(row["victory"]),
            "timestampUtc": row["timestamp_utc"],
        }
