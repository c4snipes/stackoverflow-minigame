from __future__ import annotations

from typing import Any

__all__ = ["run_webhook"]

def run_webhook(*args: Any, **kwargs: Any) -> Any:
    """Lazy wrapper around :func:`tools.scoreboard.webhook.main`."""
    from .webhook import main

    return main(*args, **kwargs)
