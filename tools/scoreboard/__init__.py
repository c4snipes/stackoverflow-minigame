from __future__ import annotations

from typing import Any

__all__ = ["run_webhook", "forward_payload"]
def run_webhook(*args: Any, **kwargs: Any) -> Any:
    """Lazy wrapper around :func:`tools.scoreboard.webhook.main`."""
    from .webhook import main

    return main(*args, **kwargs)


def forward_payload(*args: Any, **kwargs: Any) -> Any:
    """Lazy wrapper around :func:`tools.scoreboard.forward_payload.main`."""
    from .forward_payload import main

    return main(*args, **kwargs)
