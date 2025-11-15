FROM python:3.12-slim

ENV PYTHONUNBUFFERED=1
WORKDIR /app

# Create a non-root user and switch to it
RUN useradd --create-home appuser
USER appuser

COPY tools tools

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD python -c "import socket; s=socket.socket(); s.settimeout(2); s.connect(('127.0.0.1', 8000))" || exit 1

CMD ["python", "tools/scoreboard/webhook.py"]
