FROM python:3.12-slim

ENV PYTHONUNBUFFERED=1
WORKDIR /app

# Install dependencies
RUN pip install --no-cache-dir Flask flask-cors

# Create data directory for database
RUN mkdir -p /data && chmod 777 /data

# Copy application code
COPY tools tools

# Expose port 8080
EXPOSE 8080

# Health check on correct port
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD python -c "import socket; s=socket.socket(); s.settimeout(2); s.connect(('127.0.0.1', 8080))" || exit 1

# Run as non-root user for security
RUN useradd --create-home appuser && \
    chown -R appuser:appuser /app /data
USER appuser

# Set environment variables for production
ENV SCOREBOARD_HOST=0.0.0.0
ENV SCOREBOARD_PORT=8080
ENV SCOREBOARD_DB_PATH=/data/scoreboard.db

CMD ["python", "-m", "tools.scoreboard.webhook"]
