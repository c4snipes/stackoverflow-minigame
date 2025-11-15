"""
Utilities for the Stackoverflow Skyscraper scoreboard.

Only expose lightweight callables so importing this package never triggers the
web server or webhook forwarding logic until explicitly invoked.
"""

from __future__ import annotations

from typing import Any

__all__ = ["run_webhook", "forward_payload"]

def run_webhook(*args: Any, **kwargs: Any) -> Any:
    """Lazy wrapper around :func:`tools.scoreboard.webhook.main`."""
    from .webhook import main

    return main(*args, **kwargs)
    return main(*args, **kwargs)

def forward_payload(*args: Any, **kwargs: Any) -> Any:
    """Lazy wrapper around :func:`tools.scoreboard.forward_payload.main`."""
    from .forward_payload import main

    return main(*args, **kwargs)
    return main(*args, **kwargs)
