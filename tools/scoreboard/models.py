"""
Data models for scoreboard entries.
"""

from typing import TypedDict


class ScoreEntryDict(TypedDict):
    """Type definition for a scoreboard entry dictionary."""

    id: str
    initials: str
    level: int
    runTimeTicks: int
    victory: bool
    timestampUtc: str
