FROM python:3.12-slim

ENV PYTHONUNBUFFERED=1
WORKDIR /app

COPY tools/scoreboard_webhook.py tools/scoreboard_webhook.py

CMD ["python", "tools/scoreboard_webhook.py"]
