#!/usr/bin/env python3
"""Sync local scoreboard.db to fly.io remote server"""
import json
import os
import sqlite3
import sys
import urllib.request
import urllib.error
from typing import TypedDict
REMOTE_URL = "https://stackoverflow-minigame.fly.dev/scoreboard"
LOCAL_DB = "scoreboard.db"
SECRET = os.environ.get("STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET", "")

class ScoreEntry(TypedDict):
    id: str  # Database uses TEXT PRIMARY KEY
    initials: str
    level: int
    runTimeTicks: int
    victory: bool
    timestampUtc: str

def main():
    # Connect to local database
    try:
        conn = sqlite3.connect(LOCAL_DB)
        conn.row_factory = sqlite3.Row
        cursor = conn.cursor()

        # Get all scores from local database
        cursor.execute("""
            SELECT id, initials, level, run_time_ticks, victory, timestamp_utc
            FROM scoreboard
            ORDER BY timestamp_utc
        """)

        scores = cursor.fetchall()
        conn.close()

        success_count = 0
        for score in scores:
            entry: ScoreEntry = {
                "id": score["id"],
                "initials": str(score["initials"]),
                "level": int(score["level"]),
                "runTimeTicks": int(score["run_time_ticks"]),
                "victory": bool(score["victory"]),
                "timestampUtc": str(score["timestamp_utc"])
            }

            # Create payload
            payload = {
                "line": json.dumps(entry)
            }

            # POST to fly.io
            try:
                req = urllib.request.Request(
                    REMOTE_URL,
                    data=json.dumps(payload).encode('utf-8'),
                    headers={
                        'Content-Type': 'application/json',
                        'X-Scoreboard-Secret': SECRET
                    },
                    method='POST'
                )

                with urllib.request.urlopen(req, timeout=10) as response:
                    if response.status in (200, 202):
                        success_count += 1
                        print(f"✓ Uploaded: {entry['initials']} - Level {entry['level']}")
                    else:
                        print(f"✗ Failed: {entry['initials']} - Status {response.status}")

            except urllib.error.HTTPError as e:
                print(f"✗ HTTP Error uploading {entry['initials']}: {e.code} {e.reason}")
            except Exception as e:
                print(f"✗ Error uploading {entry['initials']}: {e}")

        print(f"\n✅ Sync complete: {success_count}/{len(scores)} scores uploaded successfully")

    except sqlite3.Error as e:
        print(f"Database error: {e}")
        sys.exit(1)
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()
