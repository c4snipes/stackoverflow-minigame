#!/usr/bin/env python3
"""Delete the old 'scores' table from the database."""
import sqlite3
import sys
from pathlib import Path

# Use production database path on Fly.io or local for testing
db_path = sys.argv[1] if len(sys.argv) > 1 else "/data/scoreboard.db"

try:
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()

    # Check if scores table exists
    cursor.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='scores';")
    if cursor.fetchone():
        print(f"Deleting 'scores' table from {db_path}...")
        cursor.execute("DROP TABLE scores;")
        conn.commit()
        print("✓ Successfully deleted 'scores' table")
    else:
        print(f"'scores' table does not exist in {db_path}")

    # Show remaining tables
    cursor.execute("SELECT name FROM sqlite_master WHERE type='table';")
    tables = cursor.fetchall()
    print(f"\nRemaining tables: {[t[0] for t in tables]}")

    conn.close()
except Exception as e:
    print(f"Error: {e}", file=sys.stderr)
    sys.exit(1)
